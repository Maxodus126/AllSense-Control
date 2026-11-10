using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AllSense;

public sealed record UpdateInfo(string Version, string Url);

public static class Updater
{
    public const string CurrentVersion = "1.0.0";
    private static readonly HttpClient Http = new();
    private static readonly JsonDocumentOptions JsonOpts = new() { AllowTrailingCommas = true };

    /// <summary>Format manifes: {"version":"1.1.0","url":"https://…/AllSense.exe"}</summary>
    public static async Task<UpdateInfo?> CheckAsync(string manifestUrl)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl)) return null;
        try
        {
            using var r = await Http.GetAsync(manifestUrl);
            if (!r.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(), JsonOpts);
            var root = doc.RootElement;
            var v = root.TryGetProperty("version", out var ve) ? ve.GetString() : null;
            var u = root.TryGetProperty("url", out var ue) ? ue.GetString() : null;
            if (string.IsNullOrWhiteSpace(v) || string.IsNullOrWhiteSpace(u)) return null;
            return new UpdateInfo(v, u);
        }
        catch { return null; }
    }

    public static bool IsNewer(string latestVersion)
    {
        try { return Version.Parse(latestVersion) > Version.Parse(CurrentVersion); }
        catch { return false; }
    }

    /// <summary>Unduh exe versi baru ke folder update; return null jika gagal.</summary>
    public static async Task<string?> DownloadAsync(string url, string version, Action<int>? progress)
    {
        try
        {
            var dir = Path.Combine(App.LocalDir, "update");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, $"AllSense-{version}.exe");
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return null;
            var total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var fs = File.Create(dest);
            var buf = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, n));
                read += n;
                if (total > 0) progress?.Invoke((int)Math.Min(100, read * 100 / total));
            }
            return fs.Length > 0 ? dest : null;
        }
        catch { return null; }
    }
}
