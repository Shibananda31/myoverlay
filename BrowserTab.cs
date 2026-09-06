using System.Diagnostics;
using System.Reflection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MyOverlay;

public class BrowserTab : IDisposable
{
    public WebView2 WebView { get; }
    public string Title { get; private set; } = "New Tab";
    public string CurrentUrl { get; private set; } = "about:blank";
    public bool CanGoBack => WebView.CanGoBack;
    public bool CanGoForward => WebView.CanGoForward;
    public bool IsLoading { get; private set; }

    public event Action<BrowserTab>? StateChanged;
    public event Action<BrowserTab, string>? NewTabRequested;
    public event Action<Keys>? ShortcutTriggered;

    private CoreWebView2Controller? _controller;
    private bool _isDisposed;
    private bool _isInitializing;
    private bool _isInitialized;
    private string? _pendingUrl;

    public BrowserTab()
    {
        WebView = new WebView2
        {
            Dock = DockStyle.Fill,
            Visible = false
        };

        WebView.KeyDown += OnWebViewKeyDown;
    }

    public async Task InitializeAsync(CoreWebView2Environment env, string initialUrl)
    {
        if (_isDisposed || WebView.IsDisposed)
        {
            Debug.WriteLine("[MyOverlay] InitializeAsync skipped: control is already disposed.");
            return;
        }

        if (_isInitialized)
        {
            if (!string.IsNullOrWhiteSpace(initialUrl))
            {
                Navigate(initialUrl);
            }
            return;
        }

        if (_isInitializing)
        {
            if (!string.IsNullOrWhiteSpace(initialUrl))
            {
                _pendingUrl = initialUrl;
            }
            return;
        }

        _isInitializing = true;
        _pendingUrl = initialUrl;

        try
        {
            // Ensure the control's handle is created on the WinForms UI thread
            if (!WebView.IsHandleCreated)
            {
                WebView.CreateControl();
            }

            // Initialize WebView2 on the UI thread without ConfigureAwait(false)
            await WebView.EnsureCoreWebView2Async(env);

            _isInitialized = true;
            _isInitializing = false;

            var settings = WebView.CoreWebView2.Settings;
            settings.IsScriptEnabled = true;
            settings.AreDefaultScriptDialogsEnabled = true;
            settings.IsWebMessageEnabled = true;
            settings.AreDevToolsEnabled = true;
            settings.AreDefaultContextMenusEnabled = true;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = true;

            // Hook navigation events
            WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            WebView.CoreWebView2.SourceChanged += OnSourceChanged;
            WebView.CoreWebView2.DocumentTitleChanged += OnDocumentTitleChanged;
            WebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            WebView.CoreWebView2.PermissionRequested += OnPermissionRequested;

            // 
            // Hook accelerator keys on the underlying controller if accessible
            try
            {
                var field = typeof(WebView2).GetField("_coreWebView2Controller", BindingFlags.NonPublic | BindingFlags.Instance);
                _controller = field?.GetValue(WebView) as CoreWebView2Controller;
                if (_controller != null)
                {
                    _controller.AcceleratorKeyPressed += OnAcceleratorKeyPressed;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MyOverlay] Could not bind accelerator controller: {ex.Message}");
            }

            // Navigate to pending or initial URL
            string targetUrl = !string.IsNullOrWhiteSpace(_pendingUrl) ? _pendingUrl : initialUrl;
            _pendingUrl = null;

            if (!string.IsNullOrWhiteSpace(targetUrl))
            {
                Navigate(targetUrl);
            }
        }
        catch (Exception ex)
        {
            _isInitializing = false;
            Debug.WriteLine($"[MyOverlay] WebView2 tab initialization failed: {ex}");
            throw;
        }
    }

    public void Navigate(string url)
    {
        if (_isDisposed || WebView.IsDisposed) return;

        if (string.IsNullOrWhiteSpace(url)) return;

        if (!_isInitialized || WebView.CoreWebView2 == null)
        {
            // Store pending URL if initialization hasn't finished yet
            _pendingUrl = url;
            return;
        }

        try
        {
            WebView.CoreWebView2.Navigate(url);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MyOverlay] Navigation to '{url}' failed: {ex}");
        }
    }

    public void GoBack()
    {
        if (_isInitialized && WebView.CanGoBack)
        {
            WebView.GoBack();
        }
    }

    public void GoForward()
    {
        if (_isInitialized && WebView.CanGoForward)
        {
            WebView.GoForward();
        }
    }

    public void Reload()
    {
        if (_isInitialized)
        {
            WebView.Reload();
        }
    }

    public void Stop()
    {
        if (_isInitialized)
        {
            WebView.Stop();
        }
    }

    public void OpenDevTools()
    {
        WebView.CoreWebView2?.OpenDevToolsWindow();
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        IsLoading = true;
        CurrentUrl = e.Uri;
        StateChanged?.Invoke(this);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        IsLoading = false;
        if (WebView.CoreWebView2 != null)
        {
            CurrentUrl = WebView.CoreWebView2.Source;
            if (!string.IsNullOrWhiteSpace(WebView.CoreWebView2.DocumentTitle))
            {
                Title = WebView.CoreWebView2.DocumentTitle;
            }
        }
        StateChanged?.Invoke(this);
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        if (WebView.CoreWebView2 != null)
        {
            CurrentUrl = WebView.CoreWebView2.Source;
            StateChanged?.Invoke(this);
        }
    }

    private void OnDocumentTitleChanged(object? sender, object e)
    {
        if (WebView.CoreWebView2 != null)
        {
            string title = WebView.CoreWebView2.DocumentTitle;
            Title = string.IsNullOrWhiteSpace(title) ? "New Tab" : title;
            StateChanged?.Invoke(this);
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        string target = string.IsNullOrWhiteSpace(e.Uri) ? "about:blank" : e.Uri;
        NewTabRequested?.Invoke(this, target);
    }

    private void OnAcceleratorKeyPressed(object? sender, CoreWebView2AcceleratorKeyPressedEventArgs e)
    {
        if (e.KeyEventKind != CoreWebView2KeyEventKind.KeyDown &&
            e.KeyEventKind != CoreWebView2KeyEventKind.SystemKeyDown)
        {
            return;
        }

        bool ctrl = (Control.ModifierKeys & Keys.Control) != 0;
        bool shift = (Control.ModifierKeys & Keys.Shift) != 0;
        bool alt = (Control.ModifierKeys & Keys.Alt) != 0;
        Keys key = (Keys)e.VirtualKey;

        if (ctrl && !alt && !shift && key == Keys.T)
        {
            e.Handled = true;
            ShortcutTriggered?.Invoke(Keys.Control | Keys.T);
        }
        else if (ctrl && !alt && !shift && key == Keys.W)
        {
            e.Handled = true;
            ShortcutTriggered?.Invoke(Keys.Control | Keys.W);
        }
        else if (ctrl && !alt && !shift && key == Keys.L)
        {
            e.Handled = true;
            ShortcutTriggered?.Invoke(Keys.Control | Keys.L);
        }
        else if ((ctrl && !alt && !shift && key == Keys.R) || (!ctrl && !alt && !shift && key == Keys.F5))
        {
            e.Handled = true;
            ShortcutTriggered?.Invoke(Keys.Control | Keys.R);
        }
        else if (ctrl && !alt && !shift && key == Keys.Tab)
        {
            e.Handled = true;
            ShortcutTriggered?.Invoke(Keys.Control | Keys.Tab);
        }
        else if (ctrl && !alt && shift && key == Keys.Tab)
        {
            e.Handled = true;
            ShortcutTriggered?.Invoke(Keys.Control | Keys.Shift | Keys.Tab);
        }
        else if (ctrl && !alt && !shift && key == Keys.B)
        {
            e.Handled = true;
            ShortcutTriggered?.Invoke(Keys.Control | Keys.B);
        }
        else if (!ctrl && !alt && !shift && key == Keys.F12)
        {
            e.Handled = true;
            OpenDevTools();
        }
        else if (ctrl && !alt && !shift && key == Keys.F)
        {
            e.Handled = true;
            ShortcutTriggered?.Invoke(Keys.Control | Keys.F);
        }
        else if (ctrl && alt)
        {
            if (key == Keys.C) ShortcutTriggered?.Invoke(Keys.Control | Keys.Alt | Keys.C);
            else if (key == Keys.S) ShortcutTriggered?.Invoke(Keys.Control | Keys.Alt | Keys.S);
            else if (key == Keys.H) ShortcutTriggered?.Invoke(Keys.Control | Keys.Alt | Keys.H);
        }
    }

    private void OnWebViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.T)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ShortcutTriggered?.Invoke(Keys.Control | Keys.T);
        }
        else if (e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.W)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ShortcutTriggered?.Invoke(Keys.Control | Keys.W);
        }
        else if (e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.L)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ShortcutTriggered?.Invoke(Keys.Control | Keys.L);
        }
        else if ((e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.R) || (!e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.F5))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ShortcutTriggered?.Invoke(Keys.Control | Keys.R);
        }
        else if (e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.Tab)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ShortcutTriggered?.Invoke(Keys.Control | Keys.Tab);
        }
        else if (e.Control && !e.Alt && e.Shift && e.KeyCode == Keys.Tab)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ShortcutTriggered?.Invoke(Keys.Control | Keys.Shift | Keys.Tab);
        }
        else if (e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.B)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ShortcutTriggered?.Invoke(Keys.Control | Keys.B);
        }
        else if (!e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.F12)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            OpenDevTools();
        }
        else if (e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.F)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ShortcutTriggered?.Invoke(Keys.Control | Keys.F);
        }
        else if (e.Control && e.Alt)
        {
            if (e.KeyCode == Keys.C) ShortcutTriggered?.Invoke(Keys.Control | Keys.Alt | Keys.C);
            else if (e.KeyCode == Keys.S) ShortcutTriggered?.Invoke(Keys.Control | Keys.Alt | Keys.S);
            else if (e.KeyCode == Keys.H) ShortcutTriggered?.Invoke(Keys.Control | Keys.Alt | Keys.H);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_controller != null)
        {
            try
            {
                _controller.AcceleratorKeyPressed -= OnAcceleratorKeyPressed;
            }
            catch { }
            _controller = null;
        }

        if (WebView.CoreWebView2 != null)
        {
            try
            {
                WebView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                WebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                WebView.CoreWebView2.SourceChanged -= OnSourceChanged;
                WebV
            i//uttyutkhuuyew.CoreWebView2.DocumentTitleChanged -= OnDocumentTitleChanged;
                WebView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
