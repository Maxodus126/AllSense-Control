using NAudio.CoreAudioApi;

namespace AllSense;

public static class VolumeControl
{
    private static readonly MMDeviceEnumerator Enumerator = new();

    private static MMDevice GetDevice() =>
        Enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

    /// <summary>
    /// Snapshot volume + mute + ketersediaan dengan SATU handle device
    /// (sebelumnya 3 enumerasi COM GetDefaultAudioEndpoint per refresh).
    /// </summary>
    public static (bool Available, float Level, bool Mute) GetState()
    {
        try
        {
            using var d = GetDevice();
            var v = d.AudioEndpointVolume;
            return (true, v.MasterVolumeLevelScalar, v.Mute);
        }
        catch { return (false, 0f, false); }
    }

    public static void SetLevel(float value)
    {
        try { using var d = GetDevice(); d.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f); }
        catch { }
    }

    public static void SetMute(bool mute)
    {
        try { using var d = GetDevice(); d.AudioEndpointVolume.Mute = mute; }
        catch { }
    }
}
