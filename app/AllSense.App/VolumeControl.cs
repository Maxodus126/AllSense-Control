using NAudio.CoreAudioApi;

namespace AllSense;

/// <summary>Volume sistem via CoreAudio (IAudioEndpointVolume).</summary>
public static class VolumeControl
{
    private static readonly MMDeviceEnumerator Enumerator = new();

    private static MMDevice GetDevice() =>
        Enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

    public static bool Available
    {
        get { try { using var d = GetDevice(); return true; } catch { return false; } }
    }

    public static float GetLevel()
    {
        try { using var d = GetDevice(); return d.AudioEndpointVolume.MasterVolumeLevelScalar; }
        catch { return 0; }
    }

    public static void SetLevel(float value)
    {
        try { using var d = GetDevice(); d.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f); }
        catch { }
    }

    public static bool GetMute()
    {
        try { using var d = GetDevice(); return d.AudioEndpointVolume.Mute; }
        catch { return false; }
    }

    public static void SetMute(bool mute)
    {
        try { using var d = GetDevice(); d.AudioEndpointVolume.Mute = mute; }
        catch { }
    }
}
