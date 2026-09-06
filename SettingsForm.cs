using System.Drawing;

namespace MyOverlay;

public class SettingsForm : Form
{
    private readonly OverlayForm _overlay;
    private readonly AppSettings _settings;

    // Controls - Appearance & Text
    private TextBox _txtOverlayText = null!;
    private Label _lblFontPreview = null!;
    private Panel _pnlTextColor = null!;
    private Panel _pnlBgColor = null!;
    private Panel _pnlBorderColor = null!;
    private TrackBar _trkOpacity = null!;
    private Label _lblOpacityValue = null!;

    // Controls - Position & Size
    private NumericUpDown _numX = null!;
    private NumericUpDown _numY = null!;
    private NumericUpDown _numWidth = null!;
    private NumericUpDown _numHeight = null!;

    // Controls - Browser Settings
    private TextBox _txtHomePage = null!;
    private TextBox _txtSearchTemplate = null!;
    private CheckBox _chkShowBrowserStartup = null!;

    // Controls - Audio Settings
    private CheckBox _chkCaptureSystemAudio = null!;
    private ComboBox _cboPlaybackDevice = null!;
    private Button _btnRefreshAudioDevices = null!;
    private Label _lblSystemAudioStatus = null!;
    private ProgressBar _prgSystemAudioLevel = null!;

    private ComboBox _cboAudioSource = null!;
    private ComboBox _cboMicrophoneDevice = null!;
    private Label _lblMicrophoneStatus = null!;
    private ProgressBar _prgMicrophoneLevel = null!;

    // Controls - Behaviors
    private CheckBox _chkAlwaysOnTop = null!;
    private CheckBox _chkClickThrough = null!;
    private CheckBox _chkShowBorder = null!;
    private CheckBox _chkScreenCaptureProtection = null!;

    // Working copies
    private Font _selectedFont;
    private Color _selectedTextColor;
    private Color _selectedBgColor;
    private Color _selectedBorderColor;

    public SettingsForm(OverlayForm overlay, AppSettings settings)
    {
        _overlay = overlay;
        _settings = settings;

        _selectedFont = (Font)settings.Font.Clone();
        _selectedTextColor = settings.GetTextColor();
        _selectedBgColor = settings.GetBackgroundColor();
        _selectedBorderColor = settings.GetBorderColor();

        InitializeComponents();
        LoadValuesFromSettings();
        ScreenCaptureProtection.ProtectForm(this);

        // Hook real-time audio pipeline events for live level meters and status updates
        _overlay.AudioManager.SystemAudio.AudioLevelChanged += OnSystemAudioLevelChanged;
        _overlay.AudioManager.SystemAudio.StatusChanged += OnSystemAudioStatusChanged;
        _overlay.AudioManager.Microphone.AudioLevelChanged += OnMicrophoneLevelChanged;
        _overlay.AudioManager.Microphone.StatusChanged += OnMicrophoneStatusChanged;
        _overlay.AudioManager.SystemAudio.DeviceChanged += OnAudioDevicesChanged;
    }

    private void InitializeComponents()
    {
        Text = "MyOverlay - Settings";
        Size = new Size(570, 580);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        TopMost = true;

        var tabControl = new TabControl
        {
            Dock = DockStyle.Top,
            Height = 465,
            Padding = new Point(12, 6)
        };

        // --- TAB 1: Browser & Navigation ---
        var tabBrowser = new TabPage("Browser");
        BuildBrowserTab(tabBrowser);
        tabControl.TabPages.Add(tabBrowser);

        // --- TAB 2: Notes & Appearance ---
        var tabAppearance = new TabPage("Notes & Appearance");
        BuildAppearanceTab(tabAppearance);
        tabControl.TabPages.Add(tabAppearance);

        // --- TAB 3: Position & Behaviors ---
        var tabBehavior = new TabPage("Position & Behavior");
        BuildBehaviorTab(tabBehavior);
        tabControl.TabPages.Add(tabBehavior);

        // --- TAB 4: Audio & Sound ---
        var tabAudio = new TabPage("Audio & Sound");
        BuildAudioTab(tabAudio);
        tabControl.TabPages.Add(tabAudio);

        Controls.Add(tabControl);

        // Bottom Buttons Panel
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 65,
            Padding = new Padding(12)
        };

        var btnApply = new Button
        {
            Text = "Apply",
            Width = 90,
            Height = 32,
            Location = new Point(bottomPanel.Width - 305, 14),
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        btnApply.Click += (s, e) => ApplySettings(false);

        var btnSaveClose = new Button
        {
            Text = "Save & Close",
            Width = 100,
            Height = 32,
            Location = new Point(bottomPanel.Width - 205, 14),
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        btnSaveClose.Click += (s, e) =>
        {
            ApplySettings(true);
            Close();
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            Width = 85,
            Height = 32,
            Location = new Point(bottomPanel.Width - 95, 14),
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        btnCancel.Click += (s, e) => Close();

        bottomPanel.Controls.Add(btnApply);
        bottomPanel.Controls.Add(btnSaveClose);
        bottomPanel.Controls.Add(btnCancel);
        Controls.Add(bottomPanel);
    }

    private void BuildBrowserTab(TabPage tab)
    {
        tab.Padding = new Padding(12);

        var grpGeneral = new GroupBox
        {
            Text = "Embedded Chromium Browser (WebView2)",
            Location = new Point(12, 12),
            Size = new Size(515, 230)
        };

        var lblHome = new Label
        {
            Text = "Default Home Page URL:",
            Location = new Point(15, 28),
            AutoSize = true
        };
        _txtHomePage = new TextBox
        {
            Location = new Point(15, 50),
            Width = 480,
            Font = new Font("Segoe UI", 9.5f)
        };

        var lblSearch = new Label
        {
            Text = "Search Engine Template ({0} is replaced by query):",
            Location = new Point(15, 88),
            AutoSize = true
        };
        _txtSearchTemplate = new TextBox
        {
            Location = new Point(15, 110),
            Width = 480,
            Font = new Font("Segoe UI", 9.5f)
        };

        var lblSearchExamples = new Label
        {
            Text = "Examples:\r\n• Google: https://www.google.com/search?q={0}\r\n• DuckDuckGo: https://duckduckgo.com/?q={0}\r\n• Bing: https://www.bing.com/search?q={0}",
            Location = new Point(15, 140),
            Size = new Size(480, 50),
            ForeColor = Color.DimGray,
            Font = new Font("Segoe UI", 8.5f)
        };

        _chkShowBrowserStartup = new CheckBox
        {
            Text = "Open Browser panel automatically on startup",
            Location = new Point(15, 195),
            AutoSize = true
        };

        grpGeneral.Controls.Add(lblHome);
        grpGeneral.Controls.Add(_txtHomePage);
        grpGeneral.Controls.Add(lblSearch);
        grpGeneral.Controls.Add(_txtSearchTemplate);
        grpGeneral.Controls.Add(lblSearchExamples);
        grpGeneral.Controls.Add(_chkShowBrowserStartup);
        tab.Controls.Add(grpGeneral);

        var grpShortcuts = new GroupBox
        {
            Text = "Browser Keyboard Shortcuts",
            Location = new Point(12, 250),
            Size = new Size(515, 160)
        };

        var lblBrowserShortcuts = new Label
        {
            Text = "• Ctrl + B : Toggle Browser / Notes view\r\n" +
                   "• Ctrl + T : Open New Tab\r\n" +
                   "• Ctrl + W : Close Current Tab\r\n" +
                   "• Ctrl + L : Focus Address Bar\r\n" +
                   "• Ctrl + R / F5 : Reload current page\r\n" +
                   "• Ctrl + Tab / Ctrl + Shift + Tab : Switch between tabs\r\n" +
                   "• Ctrl + F : Find text on page\r\n" +
                   "• F12 : Open Chromium Developer Tools",
            Location = new Point(15, 22),
            Size = new Size(480, 130),
            Font = new Font("Segoe UI", 9f)
        };
        grpShortcuts.Controls.Add(lblBrowserShortcuts);
        tab.Controls.Add(grpShortcuts);
    }

    private void BuildAppearanceTab(TabPage tab)
    {
        tab.Padding = new Padding(12);

        // 1. Text Group
        var grpText = new GroupBox
        {
            Text = "Overlay Notes Content",
            Location = new Point(12, 10),
            Size = new Size(515, 165)
        };

        _txtOverlayText = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = true,
            Location = new Point(12, 22),
            Size = new Size(490, 130),
            Font = new Font("Segoe UI", 9.5f)
        };
        grpText.Controls.Add(_txtOverlayText);
        tab.Controls.Add(grpText);

        // 2. Font & Colors Group
        var grpColors = new GroupBox
        {
            Text = "Typography & Colors",
            Location = new Point(12, 185),
            Size = new Size(515, 125)
        };

        var btnChooseFont = new Button
        {
            Text = "Choose Font...",
            Location = new Point(12, 24),
            Size = new Size(110, 28)
        };
        _lblFontPreview = new Label
        {
            Text = "Segoe UI, 12pt",
            Location = new Point(130, 29),
            Size = new Size(370, 20),
            AutoEllipsis = true
        };
        btnChooseFont.Click += (s, e) =>
        {
            using var fd = new FontDialog { Font = _selectedFont };
            if (fd.ShowDialog(this) == DialogResult.OK)
            {
                _selectedFont = fd.Font;
                UpdateFontPreviewLabel();
            }
        };
        grpColors.Controls.Add(btnChooseFont);
        grpColors.Controls.Add(_lblFontPreview);

        var btnTextColor = new Button
        {
            Text = "Text Color...",
            Location = new Point(12, 62),
            Size = new Size(110, 28)
        };
        _pnlTextColor = new Panel
        {
            Location = new Point(130, 64),
            Size = new Size(24, 24),
            BorderStyle = BorderStyle.FixedSingle
        };
        btnTextColor.Click += (s, e) =>
        {
            using var cd = new ColorDialog { Color = _selectedTextColor };
            if (cd.ShowDialog(this) == DialogResult.OK)
            {
                _selectedTextColor = cd.Color;
                _pnlTextColor.BackColor = _selectedTextColor;
            }
        };
        grpColors.Controls.Add(btnTextColor);
        grpColors.Controls.Add(_pnlTextColor);

        var btnBgColor = new Button
        {
            Text = "Background...",
            Location = new Point(180, 62),
            Size = new Size(110, 28)
        };
        _pnlBgColor = new Panel
        {
            Location = new Point(298, 64),
            Size = new Size(24, 24),
            BorderStyle = BorderStyle.FixedSingle
        };
        btnBgColor.Click += (s, e) =>
        {
            using var cd = new ColorDialog { Color = _selectedBgColor };
            if (cd.ShowDialog(this) == DialogResult.OK)
            {
                _selectedBgColor = cd.Color;
                _pnlBgColor.BackColor = _selectedBgColor;
            }
        };
        grpColors.Controls.Add(btnBgColor);
        grpColors.Controls.Add(_pnlBgColor);

        var btnBorderColor = new Button
        {
            Text = "Border Color...",
            Location = new Point(345, 62),
            Size = new Size(110, 28)
        };
        _pnlBorderColor = new Panel
        {
            Location = new Point(463, 64),
            Size = new Size(24, 24),
            BorderStyle = BorderStyle.FixedSingle
        };
        btnBorderColor.Click += (s, e) =>
        {
            using var cd = new ColorDialog { Color = _selectedBorderColor };
            if (cd.ShowDialog(this) == DialogResult.OK)
            {
                _selectedBorderColor = cd.Color;
                _pnlBorderColor.BackColor = _selectedBorderColor;
            }
        };
        grpColors.Controls.Add(btnBorderColor);
        grpColors.Controls.Add(_pnlBorderColor);

        tab.Controls.Add(grpColors);

        // 3. Opacity Group
        var grpOpacity = new GroupBox
        {
            Text = "Window Opacity",
            Location = new Point(12, 320),
            Size = new Size(515, 75)
        };

        _trkOpacity = new TrackBar
        {
            Minimum = 10,
            Maximum = 100,
            TickFrequency = 10,
            Location = new Point(12, 22),
            Size = new Size(430, 45)
        };
        _lblOpacityValue = new Label
        {
            Text = "95%",
            Location = new Point(450, 26),
            Size = new Size(50, 20),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        _trkOpacity.ValueChanged += (s, e) =>
        {
            _lblOpacityValue.Text = $"{_trkOpacity.Value}%";
            _overlay.Opacity = _trkOpacity.Value / 100.0;
        };

        grpOpacity.Controls.Add(_trkOpacity);
        grpOpacity.Controls.Add(_lblOpacityValue);
        tab.Controls.Add(grpOpacity);
    }

    private void BuildBehaviorTab(TabPage tab)
    {
        tab.Padding = new Padding(12);

        // 1. Position & Size Group
        var grpPos = new GroupBox
        {
            Text = "Position & Dimensions (Pixels)",
            Location = new Point(12, 10),
            Size = new Size(515, 105)
        };

        var lblX = new Label { Text = "X:", Location = new Point(15, 26), AutoSize = true };
        _numX = new NumericUpDown { Location = new Point(35, 24), Width = 75, Minimum = -5000, Maximum = 10000 };

        var lblY = new Label { Text = "Y:", Location = new Point(125, 26), AutoSize = true };
        _numY = new NumericUpDown { Location = new Point(145, 24), Width = 75, Minimum = -5000, Maximum = 10000 };

        var lblW = new Label { Text = "Width:", Location = new Point(235, 26), AutoSize = true };
        _numWidth = new NumericUpDown { Location = new Point(285, 24), Width = 75, Minimum = 200, Maximum = 10000 };

        var lblH = new Label { Text = "Height:", Location = new Point(370, 26), AutoSize = true };
        _numHeight = new NumericUpDown { Location = new Point(420, 24), Width = 75, Minimum = 150, Maximum = 10000 };

        var btnCenter = new Button
        {
            Text = "Center on Screen",
            Location = new Point(15, 62),
            Size = new Size(130, 28)
        };
        btnCenter.Click += (s, e) =>
        {
            Rectangle screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            int w = (int)_numWidth.Value;
            int h = (int)_numHeight.Value;
            _numX.Value = Math.Max(0, (screen.Width - w) / 2);
            _numY.Value = Math.Max(0, (screen.Height - h) / 2);
        };

        var btnSyncCurrent = new Button
        {
            Text = "Read Current Bounds",
            Location = new Point(155, 62),
            Size = new Size(140, 28)
        };
        btnSyncCurrent.Click += (s, e) =>
        {
            _numX.Value = _overlay.Location.X;
            _numY.Value = _overlay.Location.Y;
            _numWidth.Value = _overlay.Width;
            _numHeight.Value = _overlay.Height;
        };

        grpPos.Controls.Add(lblX);
        grpPos.Controls.Add(_numX);
        grpPos.Controls.Add(lblY);
        grpPos.Controls.Add(_numY);
        grpPos.Controls.Add(lblW);
        grpPos.Controls.Add(_numWidth);
        grpPos.Controls.Add(lblH);
        grpPos.Controls.Add(_numHeight);
        grpPos.Controls.Add(btnCenter);
        grpPos.Controls.Add(btnSyncCurrent);
        tab.Controls.Add(grpPos);

        // 2. Behavior Group
        var grpSec = new GroupBox
        {
            Text = "Window Behavior & Privacy",
            Location = new Point(12, 125),
            Size = new Size(515, 145)
        };

        _chkAlwaysOnTop = new CheckBox
        {
            Text = "Always On Top (Overlay stays above other windows)",
            Location = new Point(15, 24),
            AutoSize = true
        };

        _chkShowBorder = new CheckBox
        {
            Text = "Show Window Border (Accent border for easy sizing/viewing)",
            Location = new Point(15, 52),
            AutoSize = true
        };

        _chkClickThrough = new CheckBox
        {
            Text = "Click-Through Mode (Mouse clicks pass directly through to apps below)",
            Location = new Point(15, 80),
            AutoSize = true
        };

        _chkScreenCaptureProtection = new CheckBox
        {
            Text = "Anti-Capture Shield (Invisible to Zoom, Teams, Meet, OBS, Snipping Tool)",
            Location = new Point(15, 108),
            AutoSize = true
        };

        grpSec.Controls.Add(_chkAlwaysOnTop);
        grpSec.Controls.Add(_chkShowBorder);
        grpSec.Controls.Add(_chkClickThrough);
        grpSec.Controls.Add(_chkScreenCaptureProtection);
        tab.Controls.Add(grpSec);

        // 3. Hotkeys Info Group
        var grpHotkeys = new GroupBox
        {
            Text = "Overlay Global Hotkeys",
            Location = new Point(12, 280),
            Size = new Size(515, 115)
        };

        var lblHotkeys = new Label
        {
            Text = "• Ctrl + B : Toggle Browser Panel on / off\r\n" +
                   "• Ctrl + Alt + C : Toggle Click-Through mode\r\n" +
                   "• Ctrl + Alt + S : Open this Settings dialog\r\n" +
                   "• Ctrl + Alt + H : Hide / Show Overlay window\r\n" +
                   "• System Tray icon is always available in notification area.",
            Location = new Point(15, 20),
            Size = new Size(485, 85),
            Font = new Font("Segoe UI", 9f)
        };
        grpHotkeys.Controls.Add(lblHotkeys);
        tab.Controls.Add(grpHotkeys);
    }

    private void BuildAudioTab(TabPage tab)
    {
        tab.Padding = new Padding(12);

        // 1. Group: System / Computer Audio Capture (WASAPI Loopback)
        var grpSystem = new GroupBox
        {
            Text = "System / Computer Audio Capture (WASAPI Loopback)",
            Location = new Point(12, 10),
            Size = new Size(525, 205)
        };

        _chkCaptureSystemAudio = new CheckBox
        {
            Text = "Capture computer/system audio (WASAPI Loopback)",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Location = new Point(15, 24),
            AutoSize = true
        };

        var lblPlaybackDev = new Label
        {
            Text = "Audio Output / Playback Device (Speakers, Wired Earphones, USB/Bluetooth):",
            Location = new Point(15, 54),
            AutoSize = true
        };

        _cboPlaybackDevice = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(15, 75),
            Width = 395,
            Font = new Font("Segoe UI", 9f)
        };

        _btnRefreshAudioDevices = new Button
        {
            Text = "🔄 Refresh",
            Location = new Point(418, 74),
            Width = 92,
            Height = 27
        };
        _btnRefreshAudioDevices.Click += (s, e) => PopulateAudioDeviceDropdowns();

        var lblLevelTitle = new Label
        {
            Text = "Live System Audio Output Level:",
            Location = new Point(15, 110),
            AutoSize = true
        };

        _prgSystemAudioLevel = new ProgressBar
        {
            Location = new Point(15, 130),
            Width = 495,
            Height = 16,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous
        };

        _lblSystemAudioStatus = new Label
        {
            Text = "Status: Disabled",
            Location = new Point(15, 155),
            Size = new Size(495, 38),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.DimGray
        };

        _chkCaptureSystemAudio.CheckedChanged += (s, e) =>
        {
            bool enabled = _chkCaptureSystemAudio.Checked;
            _cboPlaybackDevice.Enabled = enabled;
            _btnRefreshAudioDevices.Enabled = enabled;
            if (!enabled)
            {
                _prgSystemAudioLevel.Value = 0;
                _lblSystemAudioStatus.Text = "Status: System audio capture disabled";
            }
        };

        grpSystem.Controls.Add(_chkCaptureSystemAudio);
        grpSystem.Controls.Add(lblPlaybackDev);
        grpSystem.Controls.Add(_cboPlaybackDevice);
        grpSystem.Controls.Add(_btnRefreshAudioDevices);
        grpSystem.Controls.Add(lblLevelTitle);
        grpSystem.Controls.Add(_prgSystemAudioLevel);
        grpSystem.Controls.Add(_lblSystemAudioStatus);
        tab.Controls.Add(grpSystem);

        // 2. Group: Voice / Speech-to-Text Audio Source Pipeline
        var grpPipeline = new GroupBox
        {
            Text = "Voice / Transcription Audio Source Pipeline",
            Location = new Point(12, 222),
            Size = new Size(525, 185)
        };

        var lblPipelineDesc = new Label
        {
            Text = "Select audio input source for voice transcription & speech processing:",
            Location = new Point(15, 22),
            AutoSize = true
        };

        _cboAudioSource = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(15, 43),
            Width = 495,
            Font = new Font("Segoe UI", 9f)
        };
        _cboAudioSource.Items.Add("Computer / System Audio (WASAPI Loopback)");
        _cboAudioSource.Items.Add("Microphone");
        _cboAudioSource.Items.Add("Both (Computer Audio + Microphone)");

        var lblMicDev = new Label
        {
            Text = "Microphone Device (Separate input source):",
            Location = new Point(15, 75),
            AutoSize = true
        };

        _cboMicrophoneDevice = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(15, 96),
            Width = 495,
            Font = new Font("Segoe UI", 9f)
        };

        var lblMicLevelTitle = new Label
        {
            Text = "Live Microphone Input Level:",
            Location = new Point(15, 126),
            AutoSize = true
        };

        _prgMicrophoneLevel = new ProgressBar
        {
            Location = new Point(15, 146),
            Width = 360,
            Height = 16,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous
        };

        _lblMicrophoneStatus = new Label
        {
            Text = "Status: Idle",
            Location = new Point(382, 146),
            Size = new Size(130, 18),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.DimGray
        };

        _cboAudioSource.SelectedIndexChanged += (s, e) =>
        {
            bool micActive = (_cboAudioSource.SelectedIndex == 1 || _cboAudioSource.SelectedIndex == 2);
            _cboMicrophoneDevice.Enabled = micActive;
            if (!micActive)
            {
                _prgMicrophoneLevel.Value = 0;
                _lblMicrophoneStatus.Text = "Status: Disabled";
            }
        };

        grpPipeline.Controls.Add(lblPipelineDesc);
        grpPipeline.Controls.Add(_cboAudioSource);
        grpPipeline.Controls.Add(lblMicDev);
        grpPipeline.Controls.Add(_cboMicrophoneDevice);
        grpPipeline.Controls.Add(lblMicLevelTitle);
        grpPipeline.Controls.Add(_prgMicrophoneLevel);
        grpPipeline.Controls.Add(_lblMicrophoneStatus);
        tab.Controls.Add(grpPipeline);
    }

    private void PopulateAudioDeviceDropdowns()
    {
        // 1. Playback Devices (WASAPI Loopback)
        string? currentSelectedId = (_cboPlaybackDevice.SelectedItem as AudioDeviceInfo)?.Id ?? _settings.SystemAudioDeviceId;
        _cboPlaybackDevice.Items.Clear();

        var defaultPlaybackItem = new AudioDeviceInfo
        {
            Id = "default",
            Name = "[Default] Windows Default Output (Follows wired earphones/speakers)",
            IsDefault = true,
            IsPlayback = true
        };
        _cboPlaybackDevice.Items.Add(defaultPlaybackItem);

        var playbackDevices = SystemAudioCapture.GetPlaybackDevices();
        int selectedIndex = 0;

        for (int i = 0; i < playbackDevices.Count; i++)
        {
            var dev = playbackDevices[i];
            _cboPlaybackDevice.Items.Add(dev);

            if (!string.IsNullOrEmpty(currentSelectedId) &&
                dev.Id.Equals(currentSelectedId, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i + 1;
            }
        }

        if (string.IsNullOrEmpty(currentSelectedId) || currentSelectedId.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            selectedIndex = 0;
        }

        if (_cboPlaybackDevice.Items.Count > 0)
        {
            _cboPlaybackDevice.SelectedIndex = Math.Clamp(selectedIndex, 0, _cboPlaybackDevice.Items.Count - 1);
        }

        // 2. Microphone Devices
        string? currentMicId = (_cboMicrophoneDevice.SelectedItem as AudioDeviceInfo)?.Id ?? _settings.MicrophoneDeviceId;
        _cboMicrophoneDevice.Items.Clear();

        var defaultMicItem = new AudioDeviceInfo
        {
            Id = "default",
            Name = "[Default] Windows Default Microphone",
            IsDefault = true,
            IsPlayback = false
        };
        _cboMicrophoneDevice.Items.Add(defaultMicItem);

        var captureDevices = MicrophoneAudioCapture.GetCaptureDevices();
        int micIndex = 0;

        for (int i = 0; i < captureDevices.Count; i++)
        {
            var dev = captureDevices[i];
            _cboMicrophoneDevice.Items.Add(dev);

            if (!string.IsNullOrEmpty(currentMicId) &&
                dev.Id.Equals(currentMicId, StringComparison.OrdinalIgnoreCase))
            {
                micIndex = i + 1;
            }
        }

        if (string.IsNullOrEmpty(currentMicId) || currentMicId.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            micIndex = 0;
        }

        if (_cboMicrophoneDevice.Items.Count > 0)
        {
            _cboMicrophoneDevice.SelectedIndex = Math.Clamp(micIndex, 0, _cboMicrophoneDevice.Items.Count - 1);
        }
    }

    private void OnSystemAudioLevelChanged(object? sender, float level)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed && _prgSystemAudioLevel != null && _prgSystemAudioLevel.IsHandleCreated)
                {
                    _prgSystemAudioLevel.Value = Math.Clamp((int)(level * 100), 0, 100);
                }
            });
        }
        catch {}
    }

    private void OnSystemAudioStatusChanged(object? sender, string status)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed && _lblSystemAudioStatus != null && _lblSystemAudioStatus.IsHandleCreated)
                {
                    _lblSystemAudioStatus.Text = $"Status: {status}";
                }
            });
        }
        catch {}
    }

    private void OnMicrophoneLevelChanged(object? sender, float level)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed && _prgMicrophoneLevel != null && _prgMicrophoneLevel.IsHandleCreated)
                {
                    _prgMicrophoneLevel.Value = Math.Clamp((int)(level * 100), 0, 100);
                }
            });
        }
        catch {}
    }

    private void OnMicrophoneStatusChanged(object? sender, string status)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed && _lblMicrophoneStatus != null && _lblMicrophoneStatus.IsHandleCreated)
                {
                    _lblMicrophoneStatus.Text = $"Status: {status}";
                }
            });
        }
        catch {}
    }

    private void OnAudioDevicesChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(PopulateAudioDeviceDropdowns);
        }
        catch {}
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _overlay.AudioManager.SystemAudio.AudioLevelChanged -= OnSystemAudioLevelChanged;
        _overlay.AudioManager.SystemAudio.StatusChanged -= OnSystemAudioStatusChanged;
        _overlay.AudioManager.Microphone.AudioLevelChanged -= OnMicrophoneLevelChanged;
        _overlay.AudioManager.Microphone.StatusChanged -= OnMicrophoneStatusChanged;
        _overlay.AudioManager.SystemAudio.DeviceChanged -= OnAudioDevicesChanged;

        base.OnFormClosed(e);
    }

    private void UpdateFontPreviewLabel()
    {
        string style = _selectedFont.Bold ? "Bold" : (_selectedFont.Italic ? "Italic" : "Regular");
        _lblFontPreview.Text = $"{_selectedFont.FontFamily.Name}, {_selectedFont.SizeInPoints:0.#}pt ({style})";
    }

    private void LoadValuesFromSettings()
    {
        _txtHomePage.Text = _settings.BrowserHomePage;
        _txtSearchTemplate.Text = _settings.SearchEngineTemplate;
        _chkShowBrowserStartup.Checked = _settings.ShowBrowser;

        _txtOverlayText.Text = _settings.Text;

        UpdateFontPreviewLabel();
        _pnlTextColor.BackColor = _selectedTextColor;
        _pnlBgColor.BackColor = _selectedBgColor;
        _pnlBorderColor.BackColor = _selectedBorderColor;

        int opacityPct = Math.Clamp((int)(_settings.Opacity * 100), 10, 100);
        _trkOpacity.Value = opacityPct;
        _lblOpacityValue.Text = $"{opacityPct}%";

        _numX.Value = _settings.X;
        _numY.Value = _settings.Y;
        _numWidth.Value = Math.Max(200, _settings.Width);
        _numHeight.Value = Math.Max(150, _settings.Height);

        _chkAlwaysOnTop.Checked = _settings.AlwaysOnTop;
        _chkClickThrough.Checked = _settings.ClickThrough;
        _chkShowBorder.Checked = _settings.ShowBorder;
        _chkScreenCaptureProtection.Checked = _settings.ScreenCaptureProtection;

        // Audio Settings
        _chkCaptureSystemAudio.Checked = _settings.CaptureSystemAudio;
        _cboPlaybackDevice.Enabled = _settings.CaptureSystemAudio;
        _btnRefreshAudioDevices.Enabled = _settings.CaptureSystemAudio;

        PopulateAudioDeviceDropdowns();

        switch (_settings.AudioSource)
        {
            case AudioSourceMode.SystemAudioOnly:
                _cboAudioSource.SelectedIndex = 0;
                break;
            case AudioSourceMode.MicrophoneOnly:
                _cboAudioSource.SelectedIndex = 1;
                break;
            case AudioSourceMode.Both:
                _cboAudioSource.SelectedIndex = 2;
                break;
        }

        bool micActive = (_settings.AudioSource == AudioSourceMode.MicrophoneOnly || _settings.AudioSource == AudioSourceMode.Both);
        _cboMicrophoneDevice.Enabled = micActive;

        if (_overlay.AudioManager.SystemAudio.IsCapturing)
        {
            _lblSystemAudioStatus.Text = $"Status: Capturing from {_overlay.AudioManager.SystemAudio.CurrentDeviceName ?? "Default Output"}";
        }
        else
        {
            _lblSystemAudioStatus.Text = _settings.CaptureSystemAudio ? "Status: Initializing..." : "Status: Capture disabled";
        }
    }

    private void ApplySettings(bool saveToDisk)
    {
        _settings.BrowserHomePage = _txtHomePage.Text.Trim();
        _settings.SearchEngineTemplate = _txtSearchTemplate.Text.Trim();
        _settings.ShowBrowser = _chkShowBrowserStartup.Checked;

        _settings.Text = _txtOverlayText.Text;

        _settings.FontFamily = _selectedFont.FontFamily.Name;
        _settings.FontSize = _selectedFont.SizeInPoints;
        _settings.FontBold = _selectedFont.Bold;
        _settings.FontItalic = _selectedFont.Italic;

        _settings.TextColorHex = ColorTranslator.ToHtml(_selectedTextColor);
        _settings.BackgroundColorHex = ColorTranslator.ToHtml(_selectedBgColor);
        _settings.BorderColorHex = ColorTranslator.ToHtml(_selectedBorderColor);

        _settings.Opacity = _trkOpacity.Value / 100.0;

        _settings.X = (int)_numX.Value;
        _settings.Y = (int)_numY.Value;
        _settings.Width = (int)_numWidth.Value;
        _settings.Height = (int)_numHeight.Value;

        _settings.AlwaysOnTop = _chkAlwaysOnTop.Checked;
        _settings.ClickThrough = _chkClickThrough.Checked;
        _settings.ShowBorder = _chkShowBorder.Checked;
        _settings.ScreenCaptureProtection = _chkScreenCaptureProtection.Checked;

        // Audio Settings
        _settings.CaptureSystemAudio = _chkCaptureSystemAudio.Checked;

        var selectedPlayback = _cboPlaybackDevice.SelectedItem as AudioDeviceInfo;
        _settings.SystemAudioDeviceId = (selectedPlayback == null || selectedPlayback.Id == "default") ? null : selectedPlayback.Id;

        switch (_cboAudioSource.SelectedIndex)
        {
            case 0:
                _settings.AudioSource = AudioSourceMode.SystemAudioOnly;
                break;
            case 1:
                _settings.AudioSource = AudioSourceMode.MicrophoneOnly;
                break;
            case 2:
                _settings.AudioSource = AudioSourceMode.Both;
                break;
        }

        var selectedMic = _cboMicrophoneDevice.SelectedItem as AudioDeviceInfo;
        _settings.MicrophoneDeviceId = (selectedMic == null || selectedMic.Id == "default") ? null : selectedMic.Id;

        _overlay.ApplySettings(_settings);

        if (saveToDisk)
        {
            _settings.Save();
        }
    }
}
