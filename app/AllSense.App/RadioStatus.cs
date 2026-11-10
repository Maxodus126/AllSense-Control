using System;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace AllSense;

/// <summary>Baca status nyala/mati radio Wi-Fi &amp; Bluetooth (indikator, bukan kontrol).</summary>
public static class RadioStatus
{
    public static async Task<(bool? wifi, bool? bt)> GetAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            bool? wifi = null, bt = null;
            foreach (var r in radios)
            {
                var on = r.State == RadioState.On;
                switch (r.Kind)
                {
                    case RadioKind.WiFi: wifi = on; break;
                    case RadioKind.Bluetooth: bt = on; break;
                }
            }
            return (wifi, bt);
        }
        catch { return (null, null); }
    }
}
