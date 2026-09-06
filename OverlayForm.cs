using System.Drawing;
using System.Drawing.Drawing2D;

namespace MyOverlay;

public class OverlayForm : Form
{
    private const int HOTKEY_CLICK_THROUGH = 1001;
    private const int HOTKEY_SETTINGS = 1002;
    private const int HOTKEY_VISIBILITY = 1003;
    private const int HOTKEY_TOGGLE_BROWSER = 1004;

    private readonly AppSettings _settings;
    private readonly AudioCaptureManager _audioManager;
    private NotifyIcon _notifyIcon = null!;
    private ContextMenuStrip _trayContextMenu = null!;
    private ContextMenuStrip _overlayContextMenu = null!;
    private SettingsForm? _settingsForm;

    public AudioCaptureManager AudioManager => _audioManager;

    // Header & Controls
    private Panel _pnlHeader = null!;
    private Label _lblTitle = null!;
    private Button _btnToggleBrowser = null!;
    private Button _btnSettings = null!;
    private Button _btnMinimize = null!;
    private Button _btnClose = null!;

    // Content panels
    private Panel _pnlContentHost = null!;
    private BrowserPanel _browserPanel = null!;
    private Panel _notesPanel = null!;
    private Label _lblNotes = null!;

    private Icon? _appIcon;

    public OverlayForm()
    {
        _settings = AppSettings.Load();
        _audioManager = new AudioCaptureManager(_settings);

        InitializeFormProperties();
        InitializeHeader();
        InitializeContentPanels();
        InitializeTrayIcon();
        InitializeContextMenus();

        ApplySettings(_settings);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // ToolWindow extended style excludes window from Alt+Tab switcher and ordinary window-sharing pickers
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    private void InitializeFormProperties()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        KeyPreview = true;
        Padding = new Padding(4); // 4px margin allows edge resizing via WM_NCHITTEST

        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true
        );

        UpdateStyles();

        // Hook lifecycle events to maintain capture exclusion across all states
        ScreenCaptureProtection.ProtectForm(this);
    }

    private void InitializeHeader()
    {
        _pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = Color.FromArgb(20, 20, 26),
            Padding = new Padding(6, 2, 6, 2)
        };

        _pnlHeader.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left && !_settings.ClickThrough)
            {
                DragWindow();
            }
        };

        _lblTitle = new Label
        {
            Text = "⚡ MyOverlay",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(165, 180, 252),
            AutoSize = true,
            Location = new Point(8, 6),
            Cursor = Cursors.SizeAll
        };
        _lblTitle.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left && !_settings.ClickThrough)
            {
                DragWindow();
            }
        };

        _btnToggleBrowser = CreateHeaderButton(
            _settings.ShowBrowser ? "📝 Notes" : "🌐 Browser",
            "Toggle Browser Panel (Ctrl+B)",
            (s, e) => ToggleBrowserPanel()
        );
        _btnToggleBrowser.Width = 75;

        _btnSettings = CreateHeaderButton("⚙️", "Settings (Ctrl+Alt+S)", (s, e) => OpenSettings());
        _btnMinimize = CreateHeaderButton("─", "Hide Overlay (Ctrl+Alt+H)", (s, e) => ToggleVisibility());
        _btnClose = CreateHeaderButton("✕", "Exit Application", (s, e) => ExitApplication());
        _btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 68, 68);

        _pnlHeader.Controls.Add(_lblTitle);
        _pnlHeader.Controls.Add(_btnToggleBrowser);
        _pnlHeader.Controls.Add(_btnSettings);
        _pnlHeader.Controls.Add(_btnMinimize);
        _pnlHeader.Controls.Add(_btnClose);

        _pnlHeader.Resize += (s, e) => RepositionHeaderControls();
        RepositionHeaderControls();

        Controls.Add(_pnlHeader);
    }

    private void RepositionHeaderControls()
    {
        int right = _pnlHeader.ClientSize.Width - 4;

        _btnClose.Location = new Point(right - 24, 2);
        _btnMinimize.Location = new Point(right - 50, 2);
        _btnSettings.Location = new Point(right - 78, 2);
        _btnToggleBrowser.Location = new Point(right - 158, 2);
    }

    private void InitializeContentPanels()
    {
        _pnlContentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(24, 24, 27)
        };

        // 1. Browser Panel
        _browserPanel = new BrowserPanel(_settings)
        {
            Dock = DockStyle.Fill,
            Visible = _settings.ShowBrowser
        };
        _browserPanel.RequestDragWindow += DragWindow;
        _browserPanel.ShortcutTriggered += HandleCustomShortcut;

        // 2. Notes Panel
        _notesPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _settings.GetBackgroundColor(),
            Padding = new Padding(12),
            Visible = !_settings.ShowBrowser
        };
        _notesPanel.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left && !_settings.ClickThrough)
            {
                DragWindow();
            }
        };

        _lblNotes = new Label
        {
            Dock = DockStyle.Fill,
            Font = _settings.Font,
            ForeColor = _settings.GetTextColor(),
            BackColor = Color.Transparent,
            Text = _settings.Text,
            Cursor = Cursors.Default
        };
        _lblNotes.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left && !_settings.ClickThrough)
            {
                DragWindow();
            }
        };

        _notesPanel.Controls.Add(_lblNotes);

        _pnlContentHost.Controls.Add(_browserPanel);
        _pnlContentHost.Controls.Add(_notesPanel);
        Controls.Add(_pnlContentHost);

        _pnlHeader.BringToFront();
        _pnlContentHost.BringToFront();
    }

    private void InitializeTrayIcon()
    {
        _appIcon = CreateAppIcon();
        Icon = _appIcon;

        _trayContextMenu = new ContextMenuStrip();

        var headerItem = new ToolStripMenuItem("MyOverlay")
        {
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Enabled = false
        };
        _trayContextMenu.Items.Add(headerItem);
        _trayContextMenu.Items.Add(new ToolStripSeparator());

        var itemBrowser = new ToolStripMenuItem("Toggle Browser Panel (Ctrl+B)", null, (s, e) => ToggleBrowserPanel());
        var itemAudioCapture = new ToolStripMenuItem("Capture System Audio", null, (s, e) => ToggleSystemAudioCapture());
        var itemSettings = new ToolStripMenuItem("Settings... (Ctrl+Alt+S)", null, (s, e) => OpenSettings());
        var itemClickThrough = new ToolStripMenuItem("Click-Through (Ctrl+Alt+C)", null, (s, e) => ToggleClickThrough());
        var itemCaptureProtection = new ToolStripMenuItem("Anti-Capture Shield (Zoom/Teams/OBS)", null, (s, e) => ToggleCaptureProtection());
        var itemAlwaysOnTop = new ToolStripMenuItem("Always On Top", null, (s, e) => ToggleAlwaysOnTop());
        var itemCenter = new ToolStripMenuItem("Center Window", null, (s, e) => CenterOnScreen());
        var itemHideShow = new ToolStripMenuItem("Hide / Show (Ctrl+Alt+H)", null, (s, e) => ToggleVisibility());
        var itemExit = new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication());

        _trayContextMenu.Items.Add(itemBrowser);
        _trayContextMenu.Items.Add(itemAudioCapture);
        _trayContextMenu.Items.Add(itemSettings);
        _trayContextMenu.Items.Add(itemClickThrough);
        _trayContextMenu.Items.Add(itemCaptureProtection);
        _trayContextMenu.Items.Add(itemAlwaysOnTop);
        _trayContextMenu.Items.Add(itemCenter);
        _trayContextMenu.Items.Add(new ToolStripSeparator());
        _trayContextMenu.Items.Add(itemHideShow);
        _trayContextMenu.Items.Add(itemExit);

        _trayContextMenu.Opening += (s, e) =>
        {
            itemBrowser.Checked = _settings.ShowBrowser;
            itemAudioCapture.Checked = _settings.CaptureSystemAudio;
            itemClickThrough.Checked = _settings.ClickThrough;
            itemCaptureProtection.Checked = _settings.ScreenCaptureProtection;
            itemAlwaysOnTop.Checked = _settings.AlwaysOnTop;
        };

        ScreenCaptureProtection.ProtectContextMenu(_trayContextMenu);

        _notifyIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "MyOverlay (Floating Browser & Notes)",
            Visible = true,
            ContextMenuStrip = _trayContextMenu
        };

        _notifyIcon.DoubleClick += (s, e) => OpenSettings();
    }

    private void InitializeContextMenus()
    {
        _overlayContextMenu = new ContextMenuStrip();

        var mnuBrowser = new ToolStripMenuItem("Toggle Browser Panel (Ctrl+B)", null, (s, e) => ToggleBrowserPanel());
        var mnuAudioCapture = new ToolStripMenuItem("Capture System Audio", null, (s, e) => ToggleSystemAudioCapture());
        var mnuSettings = new ToolStripMenuItem("Settings... (Ctrl+Alt+S)", null, (s, e) => OpenSettings());
        var mnuClickThrough = new ToolStripMenuItem("Enable Click-Through (Ctrl+Alt+C)", null, (s, e) => ToggleClickThrough());
        var mnuCapture = new ToolStripMenuItem("Anti-Capture Shield", null, (s, e) => ToggleCaptureProtection());
        var mnuCenter = new ToolStripMenuItem("Center Window", null, (s, e) => CenterOnScreen());
        var mnuHide = new ToolStripMenuItem("Hide Overlay (Ctrl+Alt+H)", null, (s, e) => ToggleVisibility());
        var mnuExit = new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication());

        _overlayContextMenu.Items.Add(mnuBrowser);
        _overlayContextMenu.Items.Add(mnuAudioCapture);
        _overlayContextMenu.Items.Add(mnuSettings);
        _overlayContextMenu.Items.Add(mnuClickThrough);
        _overlayContextMenu.Items.Add(mnuCapture);
        _overlayContextMenu.Items.Add(mnuCenter);
        _overlayContextMenu.Items.Add(new ToolStripSeparator());
        _overlayContextMenu.Items.Add(mnuHide);
        _overlayContextMenu.Items.Add(mnuExit);

        _overlayContextMenu.Opening += (s, e) =>
        {
            mnuAudioCapture.Checked = _settings.CaptureSystemAudio;
            mnuCapture.Checked = _settings.ScreenCaptureProtection;
        };

        ScreenCaptureProtection.ProtectContextMenu(_overlayContextMenu);

        _notesPanel.ContextMenuStrip = _overlayContextMenu;
        _lblNotes.ContextMenuStrip = _overlayContextMenu;
        _pnlHeader.ContextMenuStrip = _overlayContextMenu;
    }

    public void ApplySettings(AppSettings settings)
    {
        Location = new Point(settings.X, settings.Y);
        Size = new Size(Math.Max(200, settings.Width), Math.Max(150, settings.Height));
        TopMost = settings.AlwaysOnTop;
        Opacity = Math.Clamp(settings.Opacity, 0.10, 1.00);

        // Update Notes View
        _notesPanel.BackColor = settings.GetBackgroundColor();
        _lblNotes.Font = settings.Font;
        _lblNotes.ForeColor = settings.GetTextColor();
        _lblNotes.Text = settings.Text;

        // Update Browser Visibility
        UpdateBrowserVisibility(settings.ShowBrowser);

        // Update Audio Capture Settings
        _audioManager.ApplySettings(settings);

        if (IsHandleCreated)
        {
            NativeMethods.SetClickThrough(Handle, settings.ClickThrough);
            ScreenCaptureProtection.Enabled = settings.ScreenCaptureProtection;
            ScreenCaptureProtection.ApplyProtection(Handle);
        }

        Invalidate();
    }

    public void ToggleBrowserPanel()
    {
        _settings.ShowBrowser = !_settings.ShowBrowser;
        UpdateBrowserVisibility(_settings.ShowBrowser);
        _settings.Save();
    }

    private void UpdateBrowserVisibility(bool showBrowser)
    {
        _browserPanel.Visible = showBrowser;
        _notesPanel.Visible = !showBrowser;
        _btnToggleBrowser.Text = showBrowser ? "📝 Notes" : "🌐 Browser";

        if (showBrowser)
        {
            _ = _browserPanel.InitializeBrowserAsync();
        }
    }

    private void DragWindow()
    {
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HT_CAPTION, IntPtr.Zero);

        _settings.X = Location.X;
        _settings.Y = Location.Y;
        _settings.Save();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        NativeMethods.SetClickThrough(Handle, _settings.ClickThrough);

        // Apply screen capture exclusion affinity (WDA_EXCLUDEFROMCAPTURE)
        ScreenCaptureProtection.Enabled = _settings.ScreenCaptureProtection;
        ScreenCaptureProtection.ApplyProtection(Handle);

        // Register Global Hotkeys
        NativeMethods.RegisterHotKey(Handle, HOTKEY_CLICK_THROUGH,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
            (uint)Keys.C);

        NativeMethods.RegisterHotKey(Handle, HOTKEY_SETTINGS,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
            (uint)Keys.S);

        NativeMethods.RegisterHotKey(Handle, HOTKEY_VISIBILITY,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
            (uint)Keys.H);

        NativeMethods.RegisterHotKey(Handle, HOTKEY_TOGGLE_BROWSER,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT,
            (uint)Keys.B);

        // Initialize browser if enabled on start
        if (_settings.ShowBrowser)
        {
            _ = _browserPanel.InitializeBrowserAsync();
        }
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);

        _settings.X = Location.X;
        _settings.Y = Location.Y;
        _settings.Width = Width;
        _settings.Height = Height;
        _settings.Save();

        // Ensure display affinity persists across resizes
        ScreenCaptureProtection.ApplyProtection(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        // Handle Global Hotkeys
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            switch (id)
            {
                case HOTKEY_CLICK_THROUGH:
                    ToggleClickThrough();
                    break;
                case HOTKEY_SETTINGS:
                    OpenSettings();
                    break;
                case HOTKEY_VISIBILITY:
                    ToggleVisibility();
                    break;
                case HOTKEY_TOGGLE_BROWSER:
                    ToggleBrowserPanel();
                    break;
            }
            return;
        }

        // Enable borderless window edge/corner resizing when not in click-through mode
        if (m.Msg == NativeMethods.WM_NCHITTEST && !_settings.ClickThrough)
        {
            base.WndProc(ref m);
            if (m.Result == (IntPtr)NativeMethods.HTCLIENT)
            {
                int x = unchecked((short)(m.LParam.ToInt64() & 0xFFFF));
                int y = unchecked((short)((m.LParam.ToInt64() >> 16) & 0xFFFF));
                Point clientPt = PointToClient(new Point(x, y));

                const int margin = 8;
                bool left = clientPt.X < margin;
                bool right = clientPt.X >= ClientSize.Width - margin;
                bool top = clientPt.Y < margin;
                bool bottom = clientPt.Y >= ClientSize.Height - margin;

                if (top && left) m.Result = (IntPtr)NativeMethods.HTTOPLEFT;
                else if (top && right) m.Result = (IntPtr)NativeMethods.HTTOPRIGHT;
                else if (bottom && left) m.Result = (IntPtr)NativeMethods.HTBOTTOMLEFT;
                else if (bottom && right) m.Result = (IntPtr)NativeMethods.HTBOTTOMRIGHT;
                else if (left) m.Result = (IntPtr)NativeMethods.HTLEFT;
                else if (right) m.Result = (IntPtr)NativeMethods.HTRIGHT;
                else if (top) m.Result = (IntPtr)NativeMethods.HTTOP;
                else if (bottom) m.Result = (IntPtr)NativeMethods.HTBOTTOM;
            }
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (_settings.ShowBorder)
        {
            int thickness = Math.Max(1, _settings.BorderThickness);
            using var borderPen = new Pen(_settings.GetBorderColor(), thickness);
            e.Graphics.DrawRectangle(borderPen, thickness / 2, thickness / 2, Width - thickness, Height - thickness);
        }
    }

    public void ToggleClickThrough()
    {
        _settings.ClickThrough = !_settings.ClickThrough;
        NativeMethods.SetClickThrough(Handle, _settings.ClickThrough);
        _settings.Save();

        string msg = _settings.ClickThrough
            ? "Click-Through ENABLED. Clicks will pass through to background apps.\nPress Ctrl+Alt+C to disable."
            : "Click-Through DISABLED. Overlay can now be clicked and dragged.";

        _notifyIcon.ShowBalloonTip(1800, "MyOverlay", msg, ToolTipIcon.Info);
        Invalidate();
    }

    public void ToggleAlwaysOnTop()
    {
        _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
        TopMost = _settings.AlwaysOnTop;
        _settings.Save();
    }

    public void CenterOnScreen()
    {
        Rectangle screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(
            Math.Max(0, (screen.Width - Width) / 2),
            Math.Max(0, (screen.Height - Height) / 2)
        );
        _settings.X = Location.X;
        _settings.Y = Location.Y;
        _settings.Save();
    }

    public void ToggleVisibility()
    {
        Visible = !Visible;
        if (Visible)
        {
            BringToFront();
            if (IsHandleCreated && _settings.ScreenCaptureProtection)
            {
                ScreenCaptureProtection.ApplyProtection(Handle);
            }
        }
    }

    public void ToggleCaptureProtection()
    {
        _settings.ScreenCaptureProtection = !_settings.ScreenCaptureProtection;
        ScreenCaptureProtection.Enabled = _settings.ScreenCaptureProtection;
        if (IsHandleCreated)
        {
            ScreenCaptureProtection.ApplyProtection(Handle);
        }
        _settings.Save();

        string status = _settings.ScreenCaptureProtection ? "ENABLED" : "DISABLED";
        string detail = _settings.ScreenCaptureProtection
            ? "Overlay is completely invisible to Zoom, Teams, Meet, OBS, and Screenshots."
            : "Capture protection disabled. Window is now visible in screen recordings.";

        _notifyIcon.ShowBalloonTip(2000, "Screen Capture Protection", $"Anti-Capture Shield {status}\n{detail}", ToolTipIcon.Info);
        Invalidate();
    }

    public void ToggleSystemAudioCapture()
    {
        _settings.CaptureSystemAudio = !_settings.CaptureSystemAudio;
        _audioManager.ApplySettings(_settings);
        _settings.Save();

        string status = _settings.CaptureSystemAudio ? "ENABLED" : "DISABLED";
        string detail = _settings.CaptureSystemAudio
            ? $"Capturing computer audio from {_audioManager.SystemAudio.CurrentDeviceName ?? "Windows default output"} (WASAPI loopback)."
            : "System audio capture stopped.";

        _notifyIcon.ShowBalloonTip(2000, "System Audio Capture", $"System Audio {status}\n{detail}", ToolTipIcon.Info);
    }

    public void OpenSettings()
    {
        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            _settingsForm.BringToFront();
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(this, _settings);
        _settingsForm.Show();
    }

    private void HandleCustomShortcut(Keys keys)
    {
        if (keys == (Keys.Control | Keys.B))
        {
            ToggleBrowserPanel();
        }
        else if (keys == (Keys.Control | Keys.Alt | Keys.C))
        {
            ToggleClickThrough();
        }
        else if (keys == (Keys.Control | Keys.Alt | Keys.S))
        {
            OpenSettings();
        }
        else if (keys == (Keys.Control | Keys.Alt | Keys.H))
        {
            ToggleVisibility();
        }
    }

    private void ExitApplication()
    {
        Close();
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        NativeMethods.UnregisterHotKey(Handle, HOTKEY_CLICK_THROUGH);
        NativeMethods.UnregisterHotKey(Handle, HOTKEY_SETTINGS);
        NativeMethods.UnregisterHotKey(Handle, HOTKEY_VISIBILITY);
        NativeMethods.UnregisterHotKey(Handle, HOTKEY_TOGGLE_BROWSER);

        _audioManager.Dispose();

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        _appIcon?.Dispose();
        ScreenCaptureProtection.Shutdown();

        base.OnFormClosing(e);
    }

    private Button CreateHeaderButton(string text, string toolTip, EventHandler onClick)
    {
        var btn = new Button
        {
            Text = text,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.Gainsboro,
            BackColor = Color.FromArgb(32, 32, 40),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(24, 22),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 75);
        btn.Click += onClick;

        var tip = new ToolTip();
        tip.SetToolTip(btn, toolTip);

        return btn;
    }

    private static Icon CreateAppIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(99, 102, 241));
            g.FillEllipse(brush, 1, 1, 30, 30);

            using var textBrush = new SolidBrush(Color.White);
            using var font = new Font("Segoe UI", 13f, FontStyle.Bold);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("O", font, textBrush, new RectangleF(0, 0, 32, 32), sf);
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tempIcon = Icon.FromHandle(hIcon);
            return (Icon)tempIcon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }
}
