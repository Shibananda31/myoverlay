using System.Diagnostics;
using System.Drawing;
using Microsoft.Web.WebView2.Core;

namespace MyOverlay;

public class BrowserPanel : UserControl
{
    private readonly AppSettings _settings;
    private CoreWebView2Environment? _env;
    private bool _isInitializing;
    private bool _initFailed;

    // Sub-panels
    private Panel _pnlTopContainer = null!;
    private FlowLayoutPanel _tabFlowPanel = null!;
    private Button _btnNewTab = null!;
    private Panel _pnlNavBar = null!;
    private Panel _pnlBrowserContainer = null!;
    private Panel _pnlFindBar = null!;
    private TextBox _txtFind = null!;

    // Nav controls
    private Button _btnBack = null!;
    private Button _btnForward = null!;
    private Button _btnReload = null!;
    private Button _btnHome = null!;
    private TextBox _txtAddress = null!;
    private Button _btnGo = null!;
    private Button _btnCloseCurrentTab = null!;

    // Tabs
    private readonly List<BrowserTab> _tabs = new();
    private readonly Dictionary<BrowserTab, Panel> _tabButtons = new();
    private BrowserTab? _activeTab;

    // Events
    public event Action? RequestDragWindow;
    public event Action<Keys>? ShortcutTriggered;

    public BrowserPanel(AppSettings settings)
    {
        _settings = settings;
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(24, 24, 27);

        // Top Container holding Tab Strip and Nav Bar
        _pnlTopContainer = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72,
            BackColor = Color.FromArgb(28, 28, 35)
        };
        _pnlTopContainer.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) RequestDragWindow?.Invoke();
        };

        // 1. Tab Flow Panel
        _tabFlowPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = Color.FromArgb(20, 20, 25),
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(4, 3, 4, 0)
        };
        _tabFlowPanel.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) RequestDragWindow?.Invoke();
        };

        _btnNewTab = new Button
        {
            Text = "+",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(45, 45, 55),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(30, 26),
            Margin = new Padding(2, 2, 2, 2),
            Cursor = Cursors.Hand
        };
        _btnNewTab.FlatAppearance.BorderSize = 0;
        _btnNewTab.Click += (s, e) => AddTab();
        _tabFlowPanel.Controls.Add(_btnNewTab);

        // 2. Navigation Bar
        _pnlNavBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            BackColor = Color.FromArgb(32, 32, 40),
            Padding = new Padding(6, 4, 6, 4)
        };
        _pnlNavBar.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) RequestDragWindow?.Invoke();
        };

        _btnBack = CreateNavButton("◀", "Back", (s, e) => _activeTab?.GoBack());
        _btnForward = CreateNavButton("▶", "Forward", (s, e) => _activeTab?.GoForward());
        _btnReload = CreateNavButton("↻", "Reload (Ctrl+R / F5)", (s, e) => _activeTab?.Reload());
        _btnHome = CreateNavButton("🏠", "Home", (s, e) => NavigateToUrl(_settings.BrowserHomePage));

        _btnGo = CreateNavButton("➜", "Go (Enter)", (s, e) => NavigateFromAddressBar());
        _btnCloseCurrentTab = CreateNavButton("✕", "Close Tab (Ctrl+W)", (s, e) =>
        {
            if (_activeTab != null) CloseTab(_activeTab);
        });

        _txtAddress = new TextBox
        {
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(20, 20, 25),
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(145, 6),
            Height = 26,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        _txtAddress.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                NavigateFromAddressBar();
            }
        };

        // Layout Navigation Bar
        _btnBack.Location = new Point(6, 5);
        _btnForward.Location = new Point(38, 5);
        _btnReload.Location = new Point(70, 5);
        _btnHome.Location = new Point(102, 5);

        _pnlNavBar.Controls.Add(_btnBack);
        _pnlNavBar.Controls.Add(_btnForward);
        _pnlNavBar.Controls.Add(_btnReload);
        _pnlNavBar.Controls.Add(_btnHome);
        _pnlNavBar.Controls.Add(_txtAddress);
        _pnlNavBar.Controls.Add(_btnGo);
        _pnlNavBar.Controls.Add(_btnCloseCurrentTab);

        _pnlNavBar.Resize += (s, e) => RepositionNavBarControls();
        RepositionNavBarControls();

        _pnlTopContainer.Controls.Add(_tabFlowPanel);
        _pnlTopContainer.Controls.Add(_pnlNavBar);
        Controls.Add(_pnlTopContainer);

        // 3. Find on Page Bar (Initially Hidden)
        _pnlFindBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 32,
            BackColor = Color.FromArgb(40, 40, 50),
            Visible = false,
            Padding = new Padding(8, 3, 8, 3)
        };

        var lblFind = new Label
        {
            Text = "Find:",
            ForeColor = Color.Gainsboro,
            AutoSize = true,
            Location = new Point(10, 7)
        };

        _txtFind = new TextBox
        {
            Width = 200,
            Location = new Point(48, 4),
            BackColor = Color.FromArgb(20, 20, 25),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _txtFind.KeyDown += async (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await ExecuteFindOnPage(_txtFind.Text, false);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                _pnlFindBar.Visible = false;
            }
        };

        var btnFindNext = CreateSmallButton("Next", async (s, e) => await ExecuteFindOnPage(_txtFind.Text, false));
        btnFindNext.Location = new Point(255, 4);

        var btnFindPrev = CreateSmallButton("Prev", async (s, e) => await ExecuteFindOnPage(_txtFind.Text, true));
        btnFindPrev.Location = new Point(310, 4);

        var btnCloseFind = CreateSmallButton("✕", (s, e) => _pnlFindBar.Visible = false);
        btnCloseFind.Location = new Point(365, 4);
        btnCloseFind.Width = 26;

        _pnlFindBar.Controls.Add(lblFind);
        _pnlFindBar.Controls.Add(_txtFind);
        _pnlFindBar.Controls.Add(btnFindNext);
        _pnlFindBar.Controls.Add(btnFindPrev);
        _pnlFindBar.Controls.Add(btnCloseFind);

        Controls.Add(_pnlFindBar);

        // 4. Browser Container
        _pnlBrowserContainer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(18, 18, 22)
        };
        Controls.Add(_pnlBrowserContainer);

        // Bring top container to front
        _pnlTopContainer.BringToFront();
        _pnlFindBar.BringToFront();
        _pnlBrowserContainer.BringToFront();
    }

    private void RepositionNavBarControls()
    {
        int right = _pnlNavBar.ClientSize.Width - 6;

        _btnCloseCurrentTab.Location = new Point(right - 28, 5);
        _btnGo.Location = new Point(right - 60, 5);

        int addressWidth = Math.Max(120, (right - 66) - 138);
        _txtAddress.Location = new Point(138, 5);
        _txtAddress.Width = addressWidth;
    }

    public async Task InitializeBrowserAsync()
    {
        if (InvokeRequired)
        {
            await Invoke(async () => await InitializeBrowserAsync());
            return;
        }

        if (_env != null || _isInitializing || _initFailed) return;
        _isInitializing = true;

        try
        {
            // Verify WebView2 Runtime is installed
            string version;
            try
            {
                version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                _initFailed = true;
                _isInitializing = false;
                ShowRuntimeNotInstalledUI();
                return;
            }

            if (string.IsNullOrWhiteSpace(version))
            {
                _initFailed = true;
                _isInitializing = false;
                ShowRuntimeNotInstalledUI();
                return;
            }

            // Persistent user data folder
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string userDataFolder = Path.Combine(localAppData, "MyOverlay", "WebView2Data");

            if (!Directory.Exists(userDataFolder))
            {
                Directory.CreateDirectory(userDataFolder);
            }

            // Initialize WebView2 environment on the UI thread
            _env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            _isInitializing = false;

            // Open initial tab
            AddTab(_settings.BrowserHomePage);
        }
        catch (Exception ex)
        {
            _initFailed = true;
            _isInitializing = false;
            Debug.WriteLine($"[MyOverlay] WebView2 initialization error:\n{ex}");
            ShowInitErrorUI(ex.ToString());
        }
    }

    private void ShowRuntimeNotInstalledUI()
    {
        _pnlBrowserContainer.Controls.Clear();

        var pnlError = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(24, 24, 27),
            Padding = new Padding(30)
        };

        var lblIcon = new Label
        {
            Text = "⚠️",
            Font = new Font("Segoe UI", 32f),
            ForeColor = Color.Orange,
            AutoSize = true,
            Location = new Point(30, 30)
        };

        var lblTitle = new Label
        {
            Text = "Microsoft Edge WebView2 Runtime Required",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(90, 35)
        };

        var lblDesc = new Label
        {
            Text = "The embedded browser requires the Microsoft Edge WebView2 Evergreen Runtime.\n" +
                   "It provides modern Chromium rendering, security updates, and web compatibility.\n\n" +
                   "Please click the button below to download and install the free runtime from Microsoft.",
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.Gainsboro,
            Location = new Point(90, 75),
            Size = new Size(580, 80)
        };

        var btnDownload = new Button
        {
            Text = "Download WebView2 Runtime (Microsoft)",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            BackColor = Color.FromArgb(99, 102, 241),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(320, 38),
            Location = new Point(90, 170),
            Cursor = Cursors.Hand
        };
        btnDownload.FlatAppearance.BorderSize = 0;
        btnDownload.Click += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://go.microsoft.com/fwlink/p/?LinkId=2124703")
                {
                    UseShellExecute = true
                });
            }
            catch { }
        };

        var btnRetry = new Button
        {
            Text = "Check Again / Retry",
            Font = new Font("Segoe UI", 9.5f),
            BackColor = Color.FromArgb(45, 45, 55),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(160, 38),
            Location = new Point(420, 170),
            Cursor = Cursors.Hand
        };
        btnRetry.FlatAppearance.BorderSize = 0;
        btnRetry.Click += async (s, e) =>
        {
            _initFailed = false;
            await InitializeBrowserAsync();
        };

        pnlError.Controls.Add(lblIcon);
        pnlError.Controls.Add(lblTitle);
        pnlError.Controls.Add(lblDesc);
        pnlError.Controls.Add(btnDownload);
        pnlError.Controls.Add(btnRetry);

        _pnlBrowserContainer.Controls.Add(pnlError);
    }

    private void ShowInitErrorUI(string fullError)
    {
        _pnlBrowserContainer.Controls.Clear();
        var txtError = new TextBox
        {
            Text = $"Failed to initialize WebView2:\r\n\r\n{fullError}",
            ForeColor = Color.Red,
            BackColor = Color.FromArgb(20, 20, 25),
            Font = new Font("Consolas", 9f),
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both
        };
        _pnlBrowserContainer.Controls.Add(txtError);
    }

    public void AddTab(string? initialUrl = null)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => AddTab(initialUrl)));
            return;
        }

        if (_env == null)
        {
            if (!_isInitializing && !_initFailed)
            {
                _ = InitializeBrowserAsync();
            }
            return;
        }

        string url = string.IsNullOrWhiteSpace(initialUrl) ? _settings.BrowserHomePage : initialUrl;
        var tab = new BrowserTab();

        _tabs.Add(tab);

        // 1. Add WebView to its intended parent Controls collection BEFORE initialization
        _pnlBrowserContainer.Controls.Add(tab.WebView);

        // 2. Create Tab Button for the Tab Strip
        var tabBtn = CreateTabButton(tab);
        _tabButtons[tab] = tabBtn;

        // 3. Add tabBtn to _tabFlowPanel and ensure '+' button stays at the end
        // IMPORTANT: Controls.Add MUST be called before SetChildIndex!
        _tabFlowPanel.Controls.Add(tabBtn);
        int plusIndex = _tabFlowPanel.Controls.IndexOf(_btnNewTab);
        if (plusIndex >= 0)
        {
            _tabFlowPanel.Controls.SetChildIndex(_btnNewTab, _tabFlowPanel.Controls.Count - 1);
        }

        // 4. Hook tab events
        tab.StateChanged += OnTabStateChanged;
        tab.NewTabRequested += (srcTab, newUrl) => AddTab(newUrl);
        tab.ShortcutTriggered += HandleShortcut;

        // 5. Select this tab (makes it visible and active)
        SelectTab(tab);

        // 6. Initialize WebView2 on the UI thread
        _ = tab.InitializeAsync(_env, url);
    }

    public void SelectTab(BrowserTab tab)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => SelectTab(tab)));
            return;
        }

        if (!_tabs.Contains(tab)) return;

        _activeTab = tab;

        // Show active WebView, hide others
        foreach (var t in _tabs)
        {
            bool isActive = (t == tab);
            t.WebView.Visible = isActive;
            if (isActive)
            {
                t.WebView.BringToFront();
            }
        }

        // Update Tab Button Styles
        foreach (var kvp in _tabButtons)
        {
            bool isActive = (kvp.Key == tab);
            Panel pnl = kvp.Value;
            pnl.BackColor = isActive ? Color.FromArgb(40, 40, 52) : Color.FromArgb(24, 24, 30);
            foreach (Control c in pnl.Controls)
            {
                if (c is Label lbl)
                {
                    lbl.ForeColor = isActive ? Color.White : Color.Gray;
                    lbl.Font = new Font("Segoe UI", 9f, isActive ? FontStyle.Bold : FontStyle.Regular);
                }
            }
        }

        // Update Nav bar
        _txtAddress.Text = tab.CurrentUrl;
        _btnBack.Enabled = tab.CanGoBack;
        _btnForward.Enabled = tab.CanGoForward;
    }

    public void CloseTab(BrowserTab tab)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => CloseTab(tab)));
            return;
        }

        int index = _tabs.IndexOf(tab);
        if (index < 0) return;

        _tabs.Remove(tab);

        if (_tabButtons.TryGetValue(tab, out var tabBtn))
        {
            _tabFlowPanel.Controls.Remove(tabBtn);
            tabBtn.Dispose();
            _tabButtons.Remove(tab);
        }

        _pnlBrowserContainer.Controls.Remove(tab.WebView);
        tab.Dispose();

        if (_tabs.Count == 0)
        {
            // Always keep at least one tab open
            AddTab(_settings.BrowserHomePage);
        }
        else if (_activeTab == tab)
        {
            int nextIndex = Math.Clamp(index, 0, _tabs.Count - 1);
            SelectTab(_tabs[nextIndex]);
        }
    }

    public void SelectNextTab()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(SelectNextTab));
            return;
        }

        if (_tabs.Count <= 1 || _activeTab == null) return;
        int index = _tabs.IndexOf(_activeTab);
        int nextIndex = (index + 1) % _tabs.Count;
        SelectTab(_tabs[nextIndex]);
    }

    public void SelectPreviousTab()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(SelectPreviousTab));
            return;
        }

        if (_tabs.Count <= 1 || _activeTab == null) return;
        int index = _tabs.IndexOf(_activeTab);
        int prevIndex = (index - 1 + _tabs.Count) % _tabs.Count;
        SelectTab(_tabs[prevIndex]);
    }

    public void FocusAddressBar()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(FocusAddressBar));
            return;
        }

        _txtAddress.Focus();
        _txtAddress.SelectAll();
    }

    private Panel CreateTabButton(BrowserTab tab)
    {
        var pnlTab = new Panel
        {
            Height = 27,
            Width = 150,
            BackColor = Color.FromArgb(24, 24, 30),
            Margin = new Padding(2, 2, 2, 2),
            Cursor = Cursors.Hand
        };

        var lblTitle = new Label
        {
            Text = TruncateTitle(tab.Title),
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.Gray,
            AutoEllipsis = true,
            Location = new Point(6, 5),
            Size = new Size(118, 18),
            Cursor = Cursors.Hand
        };

        var btnClose = new Button
        {
            Text = "✕",
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Color.LightGray,
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(18, 18),
            Location = new Point(126, 4),
            Cursor = Cursors.Hand
        };
        btnClose.FlatAppearance.BorderSize = 0;
        btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 68, 68);

        // Click handlers
        lblTitle.Click += (s, e) => SelectTab(tab);
        pnlTab.Click += (s, e) => SelectTab(tab);

        // Middle click closes tab
        pnlTab.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Middle) CloseTab(tab);
            else if (e.Button == MouseButtons.Left) SelectTab(tab);
        };
        lblTitle.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Middle) CloseTab(tab);
            else if (e.Button == MouseButtons.Left) SelectTab(tab);
        };

        btnClose.Click += (s, e) => CloseTab(tab);

        pnlTab.Controls.Add(lblTitle);
        pnlTab.Controls.Add(btnClose);

        return pnlTab;
    }

    private void OnTabStateChanged(BrowserTab tab)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnTabStateChanged(tab)));
            return;
        }

        // Update tab header title
        if (_tabButtons.TryGetValue(tab, out var tabBtn))
        {
            foreach (Control c in tabBtn.Controls)
            {
                if (c is Label lbl)
                {
                    lbl.Text = TruncateTitle(tab.Title);
                    break;
                }
            }
        }

        // If this is the active tab, update nav controls
        if (_activeTab == tab)
        {
            if (!_txtAddress.Focused)
            {
                _txtAddress.Text = tab.CurrentUrl;
            }
            _btnBack.Enabled = tab.CanGoBack;
            _btnForward.Enabled = tab.CanGoForward;
            _btnReload.Text = tab.IsLoading ? "✕" : "↻";
        }
    }

    private void NavigateFromAddressBar()
    {
        string input = _txtAddress.Text.Trim();
        string resolvedUrl = ParseInputToUrl(input, _settings.SearchEngineTemplate);
        NavigateToUrl(resolvedUrl);
    }

    public void NavigateToUrl(string url)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => NavigateToUrl(url)));
            return;
        }

        if (_activeTab != null)
        {
            _activeTab.Navigate(url);
        }
        else if (_tabs.Count > 0)
        {
            _tabs[0].Navigate(url);
        }
        else
        {
            AddTab(url);
        }
    }

    public static string ParseInputToUrl(string input, string searchEngineTemplate)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "about:blank";

        input = input.Trim();

        // Check if input is a valid absolute URI
        if (Uri.TryCreate(input, UriKind.Absolute, out Uri? uriResult) &&
            (uriResult.Scheme == Uri.UriSchemeHttp ||
             uriResult.Scheme == Uri.UriSchemeHttps ||
             uriResult.Scheme == "file" ||
             uriResult.Scheme == "about" ||
             uriResult.Scheme == "edge"))
        {
            return uriResult.AbsoluteUri;
        }

        // Check for domain-like input without spaces
        bool hasSpaces = input.Contains(' ');
        bool isDomain = !hasSpaces && (
            input.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
            (input.Contains('.') && !input.EndsWith('.') && input.IndexOf('.') < input.Length - 1)
        );

        if (isDomain)
        {
            return "https://" + input;
        }

        // Search Query
        string query = Uri.EscapeDataString(input);
        string template = string.IsNullOrWhiteSpace(searchEngineTemplate)
            ? "https://www.google.com/search?q={0}"
            : searchEngineTemplate;

        return string.Format(template, query);
    }

    public async Task ExecuteFindOnPage(string text, bool backward)
    {
        if (_activeTab?.WebView.CoreWebView2 == null || string.IsNullOrWhiteSpace(text)) return;

        string escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string script = $"window.find(\"{escaped}\", false, {(backward ? "true" : "false")}, true, false, false, false);";
        try
        {
            await _activeTab.WebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MyOverlay] Find script execution failed: {ex.Message}");
        }
    }

    public void ToggleFindBar()
    {
        _pnlFindBar.Visible = !_pnlFindBar.Visible;
        if (_pnlFindBar.Visible)
        {
            _txtFind.Focus();
            _txtFind.SelectAll();
        }
    }

    public void HandleShortcut(Keys keys)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => HandleShortcut(keys)));
            return;
        }

        switch (keys)
        {
            case Keys.Control | Keys.T:
                AddTab();
                break;
            case Keys.Control | Keys.W:
                if (_activeTab != null) CloseTab(_activeTab);
                break;
            case Keys.Control | Keys.L:
                FocusAddressBar();
                break;
            case Keys.Control | Keys.R:
                _activeTab?.Reload();
                break;
            case Keys.Control | Keys.Tab:
                SelectNextTab();
                break;
            case Keys.Control | Keys.Shift | Keys.Tab:
                SelectPreviousTab();
                break;
            case Keys.Control | Keys.F:
                ToggleFindBar();
                break;
            default:
                ShortcutTriggered?.Invoke(keys);
                break;
        }
    }

    private Button CreateNavButton(string text, string toolTip, EventHandler onClick)
    {
        var btn = new Button
        {
            Text = text,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.Gainsboro,
            BackColor = Color.FromArgb(36, 36, 46),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(28, 26),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 75);
        btn.Click += onClick;

        var tip = new ToolTip();
        tip.SetToolTip(btn, toolTip);

        return btn;
    }

    private Button CreateSmallButton(string text, EventHandler onClick)
    {
        var btn = new Button
        {
            Text = text,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(60, 60, 72),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(48, 24),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.Click += onClick;
        return btn;
    }

    private static string TruncateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "New Tab";
        title = title.Trim();
        return title.Length > 20 ? title.Substring(0, 18) + "…" : title;
    }
}
