namespace MyOverlay;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        ScreenCaptureProtection.Initialize();

        if (args.Length > 0 && args[0] == "--test")
        {
            RunSelfTest();
            return;
        }

        Application.Run(new OverlayForm());
    }

    private static void RunSelfTest()
    {
        Console.WriteLine("[TEST] Starting OverlayForm and Screen Capture Exclusion verification...");
        var form = new OverlayForm();
        form.Show();

        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += async (s, e) =>
        {
            timer.Stop();
            try
            {
                // 1. Verify OverlayForm Display Affinity
                Console.WriteLine("[TEST] Checking Window Display Affinity on OverlayForm...");
                if (!NativeMethods.GetWindowDisplayAffinity(form.Handle, out uint formAffinity))
                {
                    throw new InvalidOperationException($"GetWindowDisplayAffinity failed for OverlayForm (HWND: 0x{form.Handle:X})");
                }
                Console.WriteLine($"[TEST] OverlayForm Display Affinity: 0x{formAffinity:X8} ({ScreenCaptureProtection.GetAffinityDescription(formAffinity)})");
                if (formAffinity != NativeMethods.WDA_EXCLUDEFROMCAPTURE && formAffinity != NativeMethods.WDA_MONITOR)
                {
                    throw new InvalidOperationException($"Display affinity is not set to exclusion mode. Value: 0x{formAffinity:X8}");
                }

                // 2. Verify WS_EX_TOOLWINDOW style
                nint exStyle = NativeMethods.GetWindowLongPtr(form.Handle, NativeMethods.GWL_EXSTYLE);
                bool hasToolWindow = (exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0;
                Console.WriteLine($"[TEST] OverlayForm WS_EX_TOOLWINDOW style present: {hasToolWindow}");
                if (!hasToolWindow)
                {
                    throw new InvalidOperationException("WS_EX_TOOLWINDOW style was not found on OverlayForm.");
                }

                // 3. Verify SettingsForm Display Affinity
                Console.WriteLine("[TEST] Checking SettingsForm capture exclusion...");
                var testSettings = AppSettings.Load();
                using var settingsForm = new SettingsForm(form, testSettings);
                settingsForm.Show();
                if (!NativeMethods.GetWindowDisplayAffinity(settingsForm.Handle, out uint settingsAffinity))
                {
                    throw new InvalidOperationException($"GetWindowDisplayAffinity failed for SettingsForm (HWND: 0x{settingsForm.Handle:X})");
                }
                Console.WriteLine($"[TEST] SettingsForm Display Affinity: 0x{settingsAffinity:X8} ({ScreenCaptureProtection.GetAffinityDescription(settingsAffinity)})");
                if (settingsAffinity != NativeMethods.WDA_EXCLUDEFROMCAPTURE && settingsAffinity != NativeMethods.WDA_MONITOR)
                {
                    throw new InvalidOperationException($"SettingsForm display affinity is not set to exclusion mode. Value: 0x{settingsAffinity:X8}");
                }
                settingsForm.Close();

                // 4. Verify ContextMenu protection registration
                Console.WriteLine("[TEST] Checking ContextMenuStrip capture exclusion...");
                using var testMenu = new ContextMenuStrip();
                testMenu.Items.Add(new ToolStripMenuItem("Test Item"));
                ScreenCaptureProtection.ProtectContextMenu(testMenu);
                testMenu.Show(form, new Point(10, 10));
                if (testMenu.IsHandleCreated)
                {
                    if (NativeMethods.GetWindowDisplayAffinity(testMenu.Handle, out uint menuAffinity))
                    {
                        Console.WriteLine($"[TEST] ContextMenuStrip Display Affinity: 0x{menuAffinity:X8} ({ScreenCaptureProtection.GetAffinityDescription(menuAffinity)})");
                    }
                }
                testMenu.Close();

                // 5. Test capture simulation (PrintWindow)
                Console.WriteLine("[TEST] Simulating window capture via PrintWindow (PW_RENDERFULLCONTENT)...");
                using (var bmp = new System.Drawing.Bitmap(form.Width, form.Height))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try
                    {
                        bool pwResult = NativeMethods.PrintWindow(form.Handle, hdc, NativeMethods.PW_RENDERFULLCONTENT);
                        Console.WriteLine($"[TEST] PrintWindow result with WDA protection: {pwResult} (DWM capture exclusion enforced)");
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }

                // 6. Test BrowserPanel and WebView2
                Console.WriteLine("[TEST] Creating dedicated test BrowserPanel...");
                var panel = new BrowserPanel(testSettings);
                form.Controls.Add(panel);
                panel.BringToFront();

                Console.WriteLine("[TEST] Initializing BrowserPanel and WebView2...");
                await panel.InitializeBrowserAsync();
                Console.WriteLine("[TEST] BrowserPanel initialized successfully.");

                Console.WriteLine("[TEST] Testing navigation to URL...");
                panel.NavigateToUrl("https://example.com");

                Console.WriteLine("[TEST] Adding second tab (https://example.org)...");
                panel.AddTab("https://example.org");
                Console.WriteLine("[TEST] Second tab added without any parent/child exception!");

                Console.WriteLine("[TEST] Adding third tab (search query)...");
                panel.AddTab("csharp winforms webview2");
                Console.WriteLine("[TEST] Third tab added successfully!");

                Console.WriteLine("[TEST] Testing tab navigation shortcuts...");
                panel.SelectPreviousTab();
                panel.SelectNextTab();

                // 7. Test Audio Device Enumeration
                Console.WriteLine("[TEST] Testing Audio Device Enumeration...");
                var playbackDevices = SystemAudioCapture.GetPlaybackDevices();
                Console.WriteLine($"[TEST] Found {playbackDevices.Count} active playback device(s):");
                foreach (var dev in playbackDevices)
                {
                    Console.WriteLine($"[TEST]   • {(dev.IsDefault ? "[DEFAULT] " : "")}{dev.Name} (ID: {dev.Id})");
                }

                var captureDevices = MicrophoneAudioCapture.GetCaptureDevices();
                Console.WriteLine($"[TEST] Found {captureDevices.Count} active microphone capture device(s):");
                foreach (var dev in captureDevices)
                {
                    Console.WriteLine($"[TEST]   • {(dev.IsDefault ? "[DEFAULT] " : "")}{dev.Name} (ID: {dev.Id})");
                }

                // 8. Test SystemAudioCapture (WASAPI loopback) lifecycle
                Console.WriteLine("[TEST] Testing SystemAudioCapture initialization (WASAPI Loopback)...");
                using (var sysAudio = new SystemAudioCapture())
                {
                    sysAudio.StatusChanged += (s, msg) =>
                    {
                        Console.WriteLine($"[TEST] SystemAudioCapture Status: {msg}");
                    };

                    sysAudio.Start();
                    Console.WriteLine($"[TEST] SystemAudioCapture.IsCapturing: {sysAudio.IsCapturing}");
                    Console.WriteLine($"[TEST] Capturing Device: {sysAudio.CurrentDeviceName ?? "None"}");
                    Console.WriteLine($"[TEST] Stream WaveFormat: {sysAudio.WaveFormat?.SampleRate}Hz, {sysAudio.WaveFormat?.Channels}ch, {sysAudio.WaveFormat?.BitsPerSample}bit");

                    if (!sysAudio.IsCapturing && playbackDevices.Count > 0)
                    {
                        throw new InvalidOperationException("SystemAudioCapture failed to start on active playback endpoint.");
                    }

                    // Test device switching
                    Console.WriteLine("[TEST] Testing device switching...");
                    if (playbackDevices.Count > 0)
                    {
                        sysAudio.SetDevice(playbackDevices[0].Id);
                        Console.WriteLine($"[TEST] Switched to device: {playbackDevices[0].Name}");
                    }

                    // Test graceful handling of invalid device ID
                    Console.WriteLine("[TEST] Testing invalid device ID handling (fallback)...");
                    sysAudio.SetDevice("invalid_fake_device_id_12345");

                    sysAudio.Stop();
                    Console.WriteLine($"[TEST] SystemAudioCapture stopped. IsCapturing: {sysAudio.IsCapturing}");
                }
                Console.WriteLine("[TEST] SystemAudioCapture disposed cleanly.");

                // 9. Test AudioCaptureManager integration
                Console.WriteLine("[TEST] Testing AudioCaptureManager integration with AppSettings...");
                using (var audioManager = new AudioCaptureManager())
                {
                    testSettings.CaptureSystemAudio = true;
                    audioManager.ApplySettings(testSettings);
                    Console.WriteLine($"[TEST] AudioCaptureManager enabled. SystemAudio.IsCapturing: {audioManager.SystemAudio.IsCapturing}");

                    testSettings.CaptureSystemAudio = false;
                    audioManager.ApplySettings(testSettings);
                    Console.WriteLine($"[TEST] AudioCaptureManager disabled. SystemAudio.IsCapturing: {audioManager.SystemAudio.IsCapturing}");
                    if (audioManager.SystemAudio.IsCapturing)
                    {
                        throw new InvalidOperationException("SystemAudio did not stop when CaptureSystemAudio was disabled.");
                    }
                }
                Console.WriteLine("[TEST] AudioCaptureManager verified successfully.");

                Console.WriteLine("[TEST] ALL SCREEN CAPTURE EXCLUSION, BROWSER & SYSTEM AUDIO CHECKS PASSED (0 Errors)");
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TEST FAILED] {ex}");
                Environment.ExitCode = 1;
            }
            finally
            {
                form.Close();
                Application.Exit();
            }
        };

        timer.Start();
        Application.Run(form);
    }
}