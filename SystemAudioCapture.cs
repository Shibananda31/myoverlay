using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace MyOverlay;

/// <summary>
/// Dedicated Windows System Audio / Computer Audio capture component.
/// Uses Windows WASAPI Loopback Capture (WasapiLoopbackCapture) via NAudio
/// to capture PCM audio from Windows playback devices (speakers, wired earphones,
/// USB headphones, Bluetooth headphones) without accessing or recording from the microphone.
/// </summary>
public sealed class SystemAudioCapture : IDisposable, IMMNotificationClient
{
    private readonly object _syncLock = new();
    private MMDeviceEnumerator? _deviceEnumerator;
    private WasapiLoopbackCapture? _capture;
    private MMDevice? _currentDevice;
    private string? _selectedDeviceId; // null or "default" = follow Windows default playback device
    private bool _isCapturing;
    private bool _isDisposed;
    private bool _isReconnecting;

    /// <summary>
    /// Event raised whenever a block of PCM audio data is captured from the system playback stream.
    /// </summary>
    public event EventHandler<AudioDataEventArgs>? AudioData;

    /// <summary>
    /// Event raised with normalized peak level [0.0 - 1.0] for real-time VU meter displays.
    /// </summary>
    public event EventHandler<float>? AudioLevelChanged;

    /// <summary>
    /// Event raised when capture status changes (connected, switched device, error, stopped).
    /// </summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>
    /// Event raised when an error occurs during capture initialization or recording.
    /// </summary>
    public event EventHandler<Exception>? CaptureError;

    /// <summary>
    /// Event raised when the active playback device changes or devices are plugged in / unplugged.
    /// </summary>
    public event EventHandler? DeviceChanged;

    /// <summary>
    /// Indicates whether system audio capture is currently running.
    /// </summary>
    public bool IsCapturing
    {
        get
        {
            lock (_syncLock)
            {
                return _isCapturing;
            }
        }
    }

    /// <summary>
    /// Returns the ID of the audio device currently being captured, or null if not capturing.
    /// </summary>
    public string? CurrentDeviceId { get; private set; }

    /// <summary>
    /// Returns the friendly name of the audio device currently being captured (e.g. "Speakers", "Headphones").
    /// </summary>
    public string? CurrentDeviceName { get; private set; }

    /// <summary>
    /// Gets the current PCM WaveFormat (sample rate, channels, bit depth) of the active loopback capture.
    /// </summary>
    public WaveFormat? WaveFormat
    {
        get
        {
            lock (_syncLock)
            {
                return _capture?.WaveFormat;
            }
        }
    }

    /// <summary>
    /// Indicates whether capture is configured to automatically follow the default Windows playback device.
    /// </summary>
    public bool UseDefaultDevice =>
        string.IsNullOrEmpty(_selectedDeviceId) ||
        _selectedDeviceId.Equals("default", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The currently configured device ID (null or "default" for default Windows playback endpoint).
    /// </summary>
    public string? SelectedDeviceId
    {
        get => _selectedDeviceId;
        set
        {
            SetDevice(value);
        }
    }

    public SystemAudioCapture(string? deviceId = null)
    {
        _selectedDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;

        try
        {
            _deviceEnumerator = new MMDeviceEnumerator();
            _deviceEnumerator.RegisterEndpointNotificationCallback(this);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemAudioCapture] Failed to register MMDevice notification callback: {ex.Message}");
        }
    }

    /// <summary>
    /// Enumerates all active Windows audio output (playback) devices.
    /// </summary>
    public static List<AudioDeviceInfo> GetPlaybackDevices()
    {
        var result = new List<AudioDeviceInfo>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? defaultId = null;

            try
            {
                var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                defaultId = defaultDevice?.ID;
            }
            catch
            {
                // No default endpoint available
            }

            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var ep in endpoints)
            {
                try
                {
                    result.Add(new AudioDeviceInfo
                    {
                        Id = ep.ID,
                        Name = ep.FriendlyName,
                        IsDefault = (defaultId != null && ep.ID.Equals(defaultId, StringComparison.OrdinalIgnoreCase)),
                        IsPlayback = true
                    });
                }
                catch
                {
                    // Skip device if reading properties fails
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemAudioCapture] Error enumerating playback devices: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Configures the playback device to capture from.
    /// If currently capturing, restarts capture on the new device asynchronously.
    /// </summary>
    /// <param name="deviceId">Device ID or null/empty for Windows default playback device.</param>
    public void SetDevice(string? deviceId)
    {
        string? newDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;

        lock (_syncLock)
        {
            if (string.Equals(_selectedDeviceId, newDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedDeviceId = newDeviceId;
        }

        if (IsCapturing)
        {
            Task.Run(RestartCaptureInternal);
        }
    }

    /// <summary>
    /// Starts capturing computer audio from the selected or default Windows playback device.
    /// Avoids blocking the UI thread by executing initialization safely.
    /// </summary>
    public void Start()
    {
        lock (_syncLock)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(SystemAudioCapture));
            }

            if (_isCapturing)
            {
                return;
            }

            StartInternal();
        }
    }

    /// <summary>
    /// Asynchronously starts system audio capture without blocking caller.
    /// </summary>
    public Task StartAsync()
    {
        return Task.Run(Start);
    }

    /// <summary>
    /// Stops capturing system audio and releases active WASAPI capture resources.
    /// </summary>
    public void Stop()
    {
        lock (_syncLock)
        {
            if (!_isCapturing && _capture == null)
            {
                return;
            }

            StopInternal();
        }
    }

    /// <summary>
    /// Asynchronously stops system audio capture.
    /// </summary>
    public Task StopAsync()
    {
        return Task.Run(Stop);
    }

    private void StartInternal()
    {
        try
        {
            if (_deviceEnumerator == null)
            {
                _deviceEnumerator = new MMDeviceEnumerator();
                try
                {
                    _deviceEnumerator.RegisterEndpointNotificationCallback(this);
                }
                catch
                {
                    // Ignore registration errors
                }
            }

            MMDevice? targetDevice = null;

            if (UseDefaultDevice)
            {
                try
                {
                    targetDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                }
                catch (Exception ex)
                {
                    NotifyError(new InvalidOperationException("No default Windows audio playback device found. Please connect speakers or headphones.", ex));
                    return;
                }
            }
            else
            {
                try
                {
                    targetDevice = _deviceEnumerator.GetDevice(_selectedDeviceId!);
                    if (targetDevice.State != DeviceState.Active)
                    {
                        NotifyStatus($"Selected device '{targetDevice.FriendlyName}' is not active ({targetDevice.State}). Falling back to default output.");
                        targetDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    }
                }
                catch (Exception ex)
                {
                    NotifyStatus($"Failed to open selected device: {ex.Message}. Falling back to default output.");
                    try
                    {
                        targetDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    }
                    catch (Exception innerEx)
                    {
                        NotifyError(new InvalidOperationException("No playback device available: " + innerEx.Message, innerEx));
                        return;
                    }
                }
            }

            if (targetDevice == null)
            {
                NotifyError(new InvalidOperationException("No suitable playback device could be resolved."));
                return;
            }

            _currentDevice = targetDevice;
            CurrentDeviceId = targetDevice.ID;
            CurrentDeviceName = targetDevice.FriendlyName;

            // Initialize NAudio WASAPI Loopback Capture
            // This captures directly from the specified render endpoint
            _capture = new WasapiLoopbackCapture(targetDevice);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;

            _capture.StartRecording();
            _isCapturing = true;

            string devDesc = UseDefaultDevice
                ? $"Default Output: {CurrentDeviceName}"
                : CurrentDeviceName ?? "Playback Device";

            NotifyStatus($"Capturing system audio from {devDesc} ({_capture.WaveFormat.SampleRate} Hz, {_capture.WaveFormat.Channels} ch)");
            DeviceChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (COMException comEx)
        {
            _isCapturing = false;
            CurrentDeviceId = null;
            CurrentDeviceName = null;
            NotifyError(new InvalidOperationException($"WASAPI loopback initialization failed (HRESULT 0x{comEx.ErrorCode:X8}): {comEx.Message}", comEx));
        }
        catch (Exception ex)
        {
            _isCapturing = false;
            CurrentDeviceId = null;
            CurrentDeviceName = null;
            NotifyError(new InvalidOperationException($"Failed to start system audio capture: {ex.Message}", ex));
        }
    }

    private void StopInternal()
    {
        _isCapturing = false;

        try
        {
            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;

                try
                {
                    _capture.StopRecording();
                }
                catch
                {
                    // Ignore stop errors if already stopped or invalidated
                }

                _capture.Dispose();
                _capture = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemAudioCapture] Error stopping capture: {ex.Message}");
        }

        CurrentDeviceId = null;
        CurrentDeviceName = null;
        AudioLevelChanged?.Invoke(this, 0.0f);
        NotifyStatus("System audio capture stopped.");
    }

    private void RestartCaptureInternal()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            StopInternal();
            StartInternal();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || _isDisposed)
        {
            return;
        }

        try
        {
            var format = _capture?.WaveFormat;
            float peak = CalculatePeak(e.Buffer, e.BytesRecorded, format);

            AudioLevelChanged?.Invoke(this, peak);

            if (format != null)
            {
                var eventArgs = new AudioDataEventArgs(e.Buffer, e.BytesRecorded, format, peak, AudioSourceType.SystemAudio);
                AudioData?.Invoke(this, eventArgs);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemAudioCapture] Error processing audio buffer: {ex.Message}");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        bool wasCapturing;
        lock (_syncLock)
        {
            wasCapturing = _isCapturing;
            _isCapturing = false;
        }

        AudioLevelChanged?.Invoke(this, 0.0f);

        if (e.Exception != null)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemAudioCapture] Recording stopped with exception: {e.Exception.Message}");

            // Handle device disconnection (e.g. wired earphones unplugged, USB headset disconnected)
            if (wasCapturing && !_isDisposed)
            {
                NotifyStatus($"Audio device disconnected or invalidated: {e.Exception.Message}. Attempting to reconnect...");
                ScheduleAutoReconnect();
            }
            else
            {
                NotifyError(e.Exception);
            }
        }
    }

    private void ScheduleAutoReconnect()
    {
        if (_isReconnecting || _isDisposed) return;
        _isReconnecting = true;

        Task.Run(async () =>
        {
            try
            {
                // Wait briefly for Windows to settle default device assignment
                await Task.Delay(1000);

                lock (_syncLock)
                {
                    if (_isDisposed) return;
                    NotifyStatus("Reconnecting system audio capture to active playback device...");
                    StopInternal();
                    StartInternal();
                }
            }
            catch (Exception ex)
            {
                NotifyError(ex);
            }
            finally
            {
                _isReconnecting = false;
            }
        });
    }

    /// <summary>
    /// Calculates normalized peak audio level [0.0 - 1.0] from raw PCM byte buffer.
    /// Supports IEEE Float 32-bit (standard WASAPI loopback format) as well as 16-bit and 24-bit PCM.
    /// </summary>
    public static float CalculatePeak(byte[] buffer, int bytesRecorded, WaveFormat? format)
    {
        if (bytesRecorded <= 0 || buffer.Length < bytesRecorded)
        {
            return 0.0f;
        }

        float max = 0.0f;

        try
        {
            if (format != null && (format.Encoding == WaveFormatEncoding.IeeeFloat || format.BitsPerSample == 32))
            {
                // 32-bit IEEE Float format (standard WASAPI shared-mode mix format)
                int sampleCount = bytesRecorded / 4;
                for (int i = 0; i < sampleCount; i++)
                {
                    int offset = i * 4;
                    if (offset + 4 > bytesRecorded) break;
                    float sample = BitConverter.ToSingle(buffer, offset);
                    float abs = Math.Abs(sample);
                    if (abs > max) max = abs;
                }
            }
            else if (format != null && format.BitsPerSample == 16)
            {
                // 16-bit signed PCM
                int sampleCount = bytesRecorded / 2;
                for (int i = 0; i < sampleCount; i++)
                {
                    int offset = i * 2;
                    if (offset + 2 > bytesRecorded) break;
                    short sample = BitConverter.ToInt16(buffer, offset);
                    float abs = Math.Abs(sample) / 32768.0f;
                    if (abs > max) max = abs;
                }
            }
            else if (format != null && format.BitsPerSample == 24)
            {
                // 24-bit signed PCM
                int sampleCount = bytesRecorded / 3;
                for (int i = 0; i < sampleCount; i++)
                {
                    int offset = i * 3;
                    if (offset + 3 > bytesRecorded) break;
                    int sample = (buffer[offset] << 8) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 24);
                    float abs = Math.Abs(sample / 2147483648.0f);
                    if (abs > max) max = abs;
                }
            }
            else
            {
                // Fallback default: try 32-bit float
                int sampleCount = bytesRecorded / 4;
                for (int i = 0; i < sampleCount; i++)
                {
                    int offset = i * 4;
                    if (offset + 4 > bytesRecorded) break;
                    float sample = BitConverter.ToSingle(buffer, offset);
                    if (!float.IsNaN(sample) && !float.IsInfinity(sample))
                    {
                        float abs = Math.Abs(sample);
                        if (abs > max) max = abs;
                    }
                }
            }
        }
        catch
        {
            // Return current max if parsing fails
        }

        return Math.Clamp(max, 0.0f, 1.0f);
    }

    private void NotifyStatus(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[SystemAudioCapture] {message}");
        try
        {
            StatusChanged?.Invoke(this, message);
        }
        catch
        {
            // Ignore subscriber errors
        }
    }

    private void NotifyError(Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[SystemAudioCapture ERROR] {ex}");
        try
        {
            CaptureError?.Invoke(this, ex);
        }
        catch
        {
            // Ignore subscriber errors
        }
    }

    #region IMMNotificationClient Implementation (Device Hot-Plugging & Default Switch Detection)

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow != DataFlow.Render || (role != Role.Multimedia && role != Role.Console))
        {
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[SystemAudioCapture] Default playback device changed to: {defaultDeviceId}");

        DeviceChanged?.Invoke(this, EventArgs.Empty);

        // If configured to follow the default Windows playback device (e.g. wired earphones connected/disconnected),
        // seamlessly switch capture to the new default endpoint!
        if (UseDefaultDevice && IsCapturing)
        {
            if (!string.Equals(CurrentDeviceId, defaultDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                NotifyStatus("Windows default playback device changed. Switching capture stream...");
                ScheduleAutoReconnect();
            }
        }
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        System.Diagnostics.Debug.WriteLine($"[SystemAudioCapture] Device state changed: {deviceId} -> {newState}");

        DeviceChanged?.Invoke(this, EventArgs.Empty);

        // If the device currently being captured was unplugged or disabled
        if (string.Equals(CurrentDeviceId, deviceId, StringComparison.OrdinalIgnoreCase) &&
            newState != DeviceState.Active &&
            IsCapturing)
        {
            NotifyStatus($"Active playback device state changed to {newState}. Reconnecting...");
            ScheduleAutoReconnect();
        }
    }

    public void OnDeviceAdded(string pwstrDeviceId)
    {
        System.Diagnostics.Debug.WriteLine($"[SystemAudioCapture] New audio device added: {pwstrDeviceId}");
        DeviceChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OnDeviceRemoved(string pwstrDeviceId)
    {
        System.Diagnostics.Debug.WriteLine($"[SystemAudioCapture] Audio device removed: {pwstrDeviceId}");
        DeviceChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
    }

    #endregion

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            StopInternal();

            try
            {
                if (_deviceEnumerator != null)
                {
                    _deviceEnumerator.UnregisterEndpointNotificationCallback(this);
                    _deviceEnumerator.Dispose();
                    _deviceEnumerator = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SystemAudioCapture] Error disposing enumerator: {ex.Message}");
            }
        }

        GC.SuppressFinalize(this);
    }
}
