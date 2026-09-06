using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MyOverlay;

/// <summary>
/// Dedicated Microphone Audio capture component.
/// Kept completely separate from SystemAudioCapture (WASAPI loopback).
/// Only starts and captures when microphone audio input is explicitly enabled by the user.
/// </summary>
public sealed class MicrophoneAudioCapture : IDisposable
{
    private readonly object _syncLock = new();
    private MMDeviceEnumerator? _deviceEnumerator;
    private WasapiCapture? _capture;
    private string? _selectedDeviceId; // null or "default" = Windows default microphone
    private bool _isCapturing;
    private bool _isDisposed;

    /// <summary>
    /// Event raised whenever microphone PCM audio data is recorded.
    /// </summary>
    public event EventHandler<AudioDataEventArgs>? AudioData;

    /// <summary>
    /// Event raised with normalized peak level [0.0 - 1.0] for real-time microphone VU meter.
    /// </summary>
    public event EventHandler<float>? AudioLevelChanged;

    /// <summary>
    /// Event raised when microphone capture status changes.
    /// </summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>
    /// Event raised when an error occurs during microphone capture.
    /// </summary>
    public event EventHandler<Exception>? CaptureError;

    /// <summary>
    /// Indicates whether microphone audio capture is currently running.
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
    /// Returns the ID of the microphone device currently being captured.
    /// </summary>
    public string? CurrentDeviceId { get; private set; }

    /// <summary>
    /// Returns the friendly name of the microphone device currently being captured.
    /// </summary>
    public string? CurrentDeviceName { get; private set; }

    /// <summary>
    /// Gets the PCM WaveFormat of the active microphone capture stream.
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

    public bool UseDefaultDevice =>
        string.IsNullOrEmpty(_selectedDeviceId) ||
        _selectedDeviceId.Equals("default", StringComparison.OrdinalIgnoreCase);

    public string? SelectedDeviceId
    {
        get => _selectedDeviceId;
        set => SetDevice(value);
    }

    public MicrophoneAudioCapture(string? deviceId = null)
    {
        _selectedDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        _deviceEnumerator = new MMDeviceEnumerator();
    }

    /// <summary>
    /// Enumerates all active Windows audio recording/microphone (capture) endpoints.
    /// </summary>
    public static List<AudioDeviceInfo> GetCaptureDevices()
    {
        var result = new List<AudioDeviceInfo>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? defaultId = null;

            try
            {
                var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                defaultId = defaultDevice?.ID;
            }
            catch
            {
                // No default microphone available
            }

            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var ep in endpoints)
            {
                try
                {
                    result.Add(new AudioDeviceInfo
                    {
                        Id = ep.ID,
                        Name = ep.FriendlyName,
                        IsDefault = (defaultId != null && ep.ID.Equals(defaultId, StringComparison.OrdinalIgnoreCase)),
                        IsPlayback = false
                    });
                }
                catch
                {
                    // Skip device on property read error
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MicrophoneAudioCapture] Error enumerating capture devices: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Resets the device enumerator to ensure fresh device detection.
    /// Should be called when audio devices change to avoid stale enumerator state.
    /// </summary>
    internal void RefreshDeviceEnumerator()
    {
        lock (_syncLock)
        {
            if (!_isCapturing)
            {
                try
                {
                    _deviceEnumerator?.Dispose();
                    _deviceEnumerator = new MMDeviceEnumerator();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MicrophoneAudioCapture] Error refreshing device enumerator: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Configures the microphone device to capture from.
    /// If currently capturing, immediately restarts capture with the new device.
    /// </summary>
    public void SetDevice(string? deviceId)
    {
        string? newDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;

        bool needsRestart = false;
        lock (_syncLock)
        {
            // Check if device actually changed
            if (string.Equals(_selectedDeviceId, newDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                return; // No change needed
            }

            _selectedDeviceId = newDeviceId;
            needsRestart = _isCapturing;
        }

        // If currently capturing, restart immediately with the new device
        if (needsRestart)
        {
            NotifyStatus($"Switching to selected microphone (device ID: {newDeviceId ?? "default"})...");
            Task.Run(RestartCaptureInternal);
        }
    }

    /// <summary>
    /// Starts recording from the configured microphone.
    /// </summary>
    public void Start()
    {
        lock (_syncLock)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(MicrophoneAudioCapture));
            }

            if (_isCapturing)
            {
                return;
            }

            StartInternal();
        }
    }

    public Task StartAsync() => Task.Run(Start);

    /// <summary>
    /// Stops recording from the microphone.
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

    public Task StopAsync() => Task.Run(Stop);

    private void StartInternal()
    {
        try
        {
            if (_deviceEnumerator == null)
            {
                _deviceEnumerator = new MMDeviceEnumerator();
            }

            MMDevice? targetDevice = null;
            bool isUsingSelectedDevice = false;

            if (UseDefaultDevice)
            {
                try
                {
                    targetDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                }
                catch (Exception ex)
                {
                    NotifyError(new InvalidOperationException("No default microphone found: " + ex.Message, ex));
                    return;
                }
            }
            else
            {
                // Attempt to open the SELECTED device
                try
                {
                    targetDevice = _deviceEnumerator.GetDevice(_selectedDeviceId!);
                    
                    // Verify the device is actually active
                    if (targetDevice.State != DeviceState.Active)
                    {
                        NotifyStatus($"Selected microphone '{targetDevice.FriendlyName}' is not currently active. Falling back to default.");
                        targetDevice = null; // Force fallback
                    }
                    else
                    {
                        isUsingSelectedDevice = true;
                    }
                }
                catch (Exception ex)
                {
                    NotifyStatus($"Cannot find or access selected microphone (ID: {_selectedDeviceId}). Error: {ex.Message}. Falling back to default.");
                    targetDevice = null; // Force fallback
                }

                // Only fall back to default if the selected device couldn't be used
                if (targetDevice == null)
                {
                    try
                    {
                        targetDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                    }
                    catch (Exception innerEx)
                    {
                        NotifyError(new InvalidOperationException("No microphone available: " + innerEx.Message, innerEx));
                        return;
                    }
                }
            }

            if (targetDevice == null)
            {
                NotifyError(new InvalidOperationException("No microphone device could be resolved."));
                return;
            }

            CurrentDeviceId = targetDevice.ID;
            CurrentDeviceName = targetDevice.FriendlyName;

            _capture = new WasapiCapture(targetDevice);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;

            _capture.StartRecording();
            _isCapturing = true;

            // Provide clear status message indicating which device is being used
            if (isUsingSelectedDevice)
            {
                NotifyStatus($"Capturing from selected microphone: {CurrentDeviceName} ({_capture.WaveFormat.SampleRate} Hz, {_capture.WaveFormat.Channels} ch)");
            }
            else
            {
                NotifyStatus($"Capturing microphone audio from {CurrentDeviceName} ({_capture.WaveFormat.SampleRate} Hz, {_capture.WaveFormat.Channels} ch)");
            }
        }
        catch (Exception ex)
        {
            _isCapturing = false;
            CurrentDeviceId = null;
            CurrentDeviceName = null;
            NotifyError(new InvalidOperationException($"Failed to start microphone capture: {ex.Message}", ex));
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
                    // Ignore stop error
                }

                _capture.Dispose();
                _capture = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MicrophoneAudioCapture] Error stopping capture: {ex.Message}");
        }

        CurrentDeviceId = null;
        CurrentDeviceName = null;
        AudioLevelChanged?.Invoke(this, 0.0f);
        NotifyStatus("Microphone capture stopped.");
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
        if (e.BytesRecorded <= 0 || _isDisposed) return;

        try
        {
            var format = _capture?.WaveFormat;
            float peak = SystemAudioCapture.CalculatePeak(e.Buffer, e.BytesRecorded, format);

            AudioLevelChanged?.Invoke(this, peak);

            if (format != null)
            {
                var eventArgs = new AudioDataEventArgs(e.Buffer, e.BytesRecorded, format, peak, AudioSourceType.Microphone);
                AudioData?.Invoke(this, eventArgs);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MicrophoneAudioCapture] Error processing buffer: {ex.Message}");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_syncLock)
        {
            _isCapturing = false;
        }

        AudioLevelChanged?.Invoke(this, 0.0f);

        if (e.Exception != null)
        {
            NotifyError(e.Exception);
        }
    }

    private void NotifyStatus(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[MicrophoneAudioCapture] {message}");
        try
        {
            StatusChanged?.Invoke(this, message);
        }
        catch
        {
        }
    }

    private void NotifyError(Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[MicrophoneAudioCapture ERROR] {ex}");
        try
        {
            CaptureError?.Invoke(this, ex);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            StopInternal();

            try
            {
                _deviceEnumerator?.Dispose();
                _deviceEnumerator = null;
            }
            catch
            {
            }
        }

        GC.SuppressFinalize(this);
    }
}
