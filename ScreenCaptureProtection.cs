using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace MyOverlay;

/// <summary>
/// Provides comprehensive screen capture exclusion for the entire application.
/// Ensures windows are invisible to screen-sharing applications (Zoom, Teams, Google Meet),
/// streaming/recording software (OBS Studio), and screenshot tools (Snipping Tool, PrintScreen).
/// </summary>
public static class ScreenCaptureProtection
{
    private static bool _isEnabled = true;
    private static IntPtr _hHook = IntPtr.Zero;
    private static NativeMethods.HookProc? _hookDelegate;
    private static readonly HashSet<IntPtr> _knownWindows = new();
    private static readonly object _lock = new();

    /// <summary>
    /// Gets or sets whether screen capture protection is enabled.
    /// </summary>
    public static bool Enabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                RefreshAllCurrentWindows();
            }
        }
    }

    /// <summary>
    /// Initializes application-wide screen capture protection by installing a thread-level
    /// CBT hook on the UI thread to intercept and protect all windows, popups, and dialogs.
    /// </summary>
    public static void Initialize()
    {
        if (_hHook != IntPtr.Zero) return;

        try
        {
            uint threadId = NativeMethods.GetCurrentThreadId();
            _hookDelegate = CbtHookCallback;
            _hHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_CBT,
                _hookDelegate,
                IntPtr.Zero,
                threadId
            );

            MessageBox.Show($"[ScreenCaptureProtection] CBT Hook installed on thread {threadId}: {(_hHook != IntPtr.Zero ? "Success" : "Failed")}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"[ScreenCaptureProtection] Failed to install CBT Hook: {ex.Message}");
        }

        // Also sweep any windows that may already exist on this thread
        ProtectAllThreadWindows();
    }

    /// <summary>
    /// CBT Hook callback called on window creation, activation, etc.
    /// </summary>
    private static IntPtr CbtHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (_isEnabled && (nCode == NativeMethods.HCBT_ACTIVATE || nCode == NativeMethods.HCBT_CREATEWND))
        {
            IntPtr hWnd = wParam;
            if (hWnd != IntPtr.Zero && NativeMethods.IsWindow(hWnd))
            {
                ApplyProtection(hWnd);
            }
        }

        return NativeMethods.CallNextHookEx(_hHook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Protects a Form and hooks its lifecycle events so protection persists
    /// across moves, resizes, state changes, and handle recreations.
    /// </summary>
    public static void ProtectForm(Form form)
    {
        if (form == null || form.IsDisposed) return;

        if (form.IsHandleCreated)
        {
            ApplyProtection(form.Handle);
        }

        form.HandleCreated += (s, e) =>
        {
            if (form.IsHandleCreated)
            {
                ApplyProtection(form.Handle);
            }
        };

        form.VisibleChanged += (s, e) =>
        {
            if (form.Visible && form.IsHandleCreated)
            {
                ApplyProtection(form.Handle);
            }
        };

        form.Activated += (s, e) =>
        {
            if (form.IsHandleCreated)
            {
                ApplyProtection(form.Handle);
            }
        };

        form.ResizeEnd += (s, e) =>
        {
            if (form.IsHandleCreated)
            {
                ApplyProtection(form.Handle);
            }
        };
    }

    /// <summary>
    /// Protects a ContextMenuStrip or ToolStripDropDown so popup menus remain invisible in screen capture.
    /// </summary>
    public static void ProtectContextMenu(ToolStripDropDown dropDown)
    {
        if (dropDown == null || dropDown.IsDisposed) return;

        if (dropDown.IsHandleCreated)
        {
            ApplyProtection(dropDown.Handle);
        }

        dropDown.HandleCreated += (s, e) =>
        {
            if (dropDown.IsHandleCreated)
            {
                ApplyProtection(dropDown.Handle);
            }
        };

        dropDown.Opened += (s, e) =>
        {
            if (dropDown.IsHandleCreated)
            {
                ApplyProtection(dropDown.Handle);
            }
        };
    }

    /// <summary>
    /// Applies capture exclusion to a window handle using SetWindowDisplayAffinity.
    /// First attempts WDA_EXCLUDEFROMCAPTURE (0x11); falls back to WDA_MONITOR (0x01).
    /// Also sets DWM peek/alt-tab exclusion attributes and inspects child windows.
    /// </summary>
    public static bool ApplyProtection(IntPtr hWnd)
    {
        Console.WriteLine($"[ScreenCaptureProtection] Attempting protection on HWND 0x{hWnd:X}");
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd))
        {
            return false;
        }

        // Verify window belongs to this process
        NativeMethods.GetWindowThreadProcessId(hWnd, out uint windowProcessId);
        if (windowProcessId != NativeMethods.GetCurrentProcessId())
        {
            return false;
        }

        uint targetAffinity = _isEnabled ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE;

        bool success = false;
        if (_isEnabled)
        {
            // Primary: WDA_EXCLUDEFROMCAPTURE (completely transparent / invisible in capture)
            success = NativeMethods.SetWindowDisplayAffinity(hWnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

            if (!success)
            {
                // Fallback: WDA_MONITOR (monitor display only, blacked out in capture)
                int err = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[ScreenCaptureProtection] WDA_EXCLUDEFROMCAPTURE failed on HWND 0x{hWnd:X} (Error: {err}). Falling back to WDA_MONITOR...");
                success = NativeMethods.SetWindowDisplayAffinity(hWnd, NativeMethods.WDA_MONITOR);
            }

            if (success)
            {
                lock (_lock)
                {
                    _knownWindows.Add(hWnd);
                }

                // Exclude window from Aero Peek preview
                int excludeFromPeek = 1;
                NativeMethods.DwmSetWindowAttribute(
                    hWnd,
                    NativeMethods.DWMWA_EXCLUDED_FROM_PEEK,
                    ref excludeFromPeek,
                    sizeof(int)
                );

                // Exclude window from Flip3D / Alt-Tab switcher preview
                int flip3dPolicy = NativeMethods.DWMFLIP3D_EXCLUDEBELOW;
                NativeMethods.DwmSetWindowAttribute(
                    hWnd,
                    NativeMethods.DWMWA_FLIP3D_POLICY,
                    ref flip3dPolicy,
                    sizeof(int)
                );

                // Try applying affinity to any direct child windows
                NativeMethods.EnumChildWindows(hWnd, (childHwnd, lParam) =>
                {
                    try
                    {
                        NativeMethods.GetWindowThreadProcessId(childHwnd, out uint childPid);
                        if (childPid == NativeMethods.GetCurrentProcessId())
                        {
                            NativeMethods.SetWindowDisplayAffinity(childHwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                        }
                    }
                    catch
                    {
                        // Some child windows do not support display affinity; silently ignore
                    }
                    return true;
                }, IntPtr.Zero);
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[ScreenCaptureProtection] SetWindowDisplayAffinity failed on HWND 0x{hWnd:X} (Error: {err})");
            }
        }
        else
        {
            // Disable protection
            success = NativeMethods.SetWindowDisplayAffinity(hWnd, NativeMethods.WDA_NONE);
            lock (_lock)
            {
                _knownWindows.Remove(hWnd);
            }
        }

        return success;
    }

    /// <summary>
    /// Checks whether a window has screen capture protection currently active.
    /// </summary>
    public static bool IsWindowProtected(IntPtr hWnd, out uint affinity)
    {
        affinity = NativeMethods.WDA_NONE;
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd))
        {
            return false;
        }

        if (NativeMethods.GetWindowDisplayAffinity(hWnd, out affinity))
        {
            return affinity == NativeMethods.WDA_EXCLUDEFROMCAPTURE || affinity == NativeMethods.WDA_MONITOR;
        }

        return false;
    }

    /// <summary>
    /// Gets a human-readable description of the window's display affinity.
    /// </summary>
    public static string GetAffinityDescription(uint affinity) => affinity switch
    {
        NativeMethods.WDA_EXCLUDEFROMCAPTURE => "WDA_EXCLUDEFROMCAPTURE (Completely invisible in screen share & recordings)",
        NativeMethods.WDA_MONITOR => "WDA_MONITOR (Monitor only / Blacked out in capture)",
        NativeMethods.WDA_NONE => "WDA_NONE (Not protected / Visible in screen share)",
        _ => $"Affinity 0x{affinity:X8}"
    };

    /// <summary>
    /// Sweeps all windows belonging to the current thread and ensures protection is applied.
    /// </summary>
    public static void ProtectAllThreadWindows()
    {
        uint threadId = NativeMethods.GetCurrentThreadId();
        NativeMethods.EnumThreadWindows(threadId, (hWnd, lParam) =>
        {
            if (NativeMethods.IsWindow(hWnd))
            {
                ApplyProtection(hWnd);
            }
            return true;
        }, IntPtr.Zero);
    }

    /// <summary>
    /// Re-evaluates and reapplies protection to all currently known windows and thread windows.
    /// </summary>
    public static void RefreshAllCurrentWindows()
    {
        List<IntPtr> windowsToRefresh;
        lock (_lock)
        {
            windowsToRefresh = new List<IntPtr>(_knownWindows);
        }

        foreach (var hWnd in windowsToRefresh)
        {
            if (NativeMethods.IsWindow(hWnd))
            {
                ApplyProtection(hWnd);
            }
        }

        ProtectAllThreadWindows();
    }

    /// <summary>
    /// Cleans up installed hooks on application exit.
    /// </summary>
    public static void Shutdown()
    {
        if (_hHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hHook);
            _hHook = IntPtr.Zero;
            _hookDelegate = null;
        }
    }
}
