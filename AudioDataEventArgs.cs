using NAudio.Wave;

namespace MyOverlay;

public enum AudioSourceType
{
    SystemAudio,
    Microphone
}

public enum AudioSourceMode
{
    SystemAudioOnly,
    MicrophoneOnly,
    Both
}

public class AudioDataEventArgs : EventArgs
{
    public byte[] Buffer { get; }
    public int BytesRecorded { get; }
    public WaveFormat WaveFormat { get; }
    public DateTime Timestamp { get; }
    public float PeakLevel { get; }
    public AudioSourceType SourceType { get; }

    public AudioDataEventArgs(byte[] buffer, int bytesRecorded, WaveFormat waveFormat, float peakLevel, AudioSourceType sourceType = AudioSourceType.SystemAudio)
    {
        Buffer = buffer;
        BytesRecorded = bytesRecorded;
        WaveFormat = waveFormat;
        Timestamp = DateTime.UtcNow;
        PeakLevel = peakLevel;
        SourceType = sourceType;
    }
}
