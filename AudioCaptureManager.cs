using System;

namespace MyOverlay;

/// <summary>
/// Audio Pipeline Manager coordinating System Audio (WASAPI loopback) and Microphone capture.
/// Keeps system audio and microphone recording as two separate sources while providing
/// a unified PCM audio stream for speech-to-text, transcription, or other audio processing pipelines.
/// </summary>
public sealed class AudioCaptureManager : IDisposable
{
    private readonly object _syncLock = new();
    private bool _isDisposed;

    public SystemAudioCapture SystemAudio { get; }
    public MicrophoneAudioCapture Microphone { get; }

    /// <summary>
    /// Event raised whenever PCM audio data arrives from any active source (tagged with AudioSourceType).
    /// </summary>
    public event EventHandler<AudioDataEventArgs>? AudioDataReceived;

    /// <summary>
    /// Event raised when status changes on either audio capture source.
    /// </summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>
    /// Event raised when an error occurs on either audio capture source.
    /// </summary>
    public event EventHandler<Exception>? ErrorOccurred;

    public AudioCaptureManager(AppSettings? settings = null)
    {
        SystemAudio = new SystemAudioCapture();
        Microphone = new MicrophoneAudioCapture();

        SystemAudio.AudioData += (s, e) => AudioDataReceived?.Invoke(this, e);
        SystemAudio.StatusChanged += (s, msg) => StatusChanged?.Invoke(this, $"[System Audio] {msg}");
        SystemAudio.CaptureError += (s, ex) => ErrorOccurred?.Invoke(this, ex);

        Microphone.AudioData += (s, e) => AudioDataReceived?.Invoke(this, e);
        Microphone.StatusChanged += (s, msg) => StatusChanged?.Invoke(this, $"[Microphone] {msg}");
        Microphone.CaptureError += (s, ex) => ErrorOccurred?.Invoke(this, ex);

        if (settings != null)
        {
            ApplySettings(settings);
        }
    }

    /// <summary>
    /// Applies updated configuration to system audio and microphone capture sources.
    /// </summary>
    public void ApplySettings(AppSettings settings)
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;

            // 1. Configure System Audio Capture
            SystemAudio.SetDevice(settings.SystemAudioDeviceId);

            if (settings.CaptureSystemAudio)
            {
                if (!SystemAudio.IsCapturing)
                {
                    SystemAudio.Start();
                }
            }
            else
            {
                if (SystemAudio.IsCapturing)
                {
                    SystemAudio.Stop();
                }
            }

            // 2. Configure Microphone Capture (kept completely separate)
            bool enableMicrophone = (settings.AudioSource == AudioSourceMode.MicrophoneOnly ||
                                     settings.AudioSource == AudioSourceMode.Both);

            if (enableMicrophone)
            {
                Microphone.SetDevice(settings.MicrophoneDeviceId);
                if (!Microphone.IsCapturing)
                {
                    Microphone.Start();
                }
            }
            else
            {
                if (Microphone.IsCapturing)
                {
                    Microphone.Stop();
                }
            }
        }
    }

    /// <summary>
    /// Stops all audio capture streams.
    /// </summary>
    public void StopAll()
    {
        lock (_syncLock)
        {
            SystemAudio.Stop();
            Microphone.Stop();
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            SystemAudio.Dispose();
            Microphone.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
