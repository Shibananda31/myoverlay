namespace MyOverlay;

public class AudioDeviceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsPlayback { get; set; }

    public override string ToString()
    {
        return IsDefault ? $"{Name} [Default]" : Name;
    }
}
