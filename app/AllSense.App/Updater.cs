using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AllSense;

public sealed record UpdateInfo(string Version, string Url);

/// <summary>
/// Self-update via GitHub Releases. Alur:
/// cek release terbaru → unduh asset AllSense.exe → ganti exe berjalan + restart.
/// </summary>
public static class Updater
{
    public const string CurrentVersion = "1.0.5";
    private const string Repo = "Maxodus126/AllSense-Control";
    private const string LatestUrl = $"https://api.github.com/repos/{Repo}/releases/latest";

    private static readonly HttpClient Http = new();
    private static readonly JsonDocumentOptions JsonOpts = new() { AllowTrailingCommas = true };

    /// <summary>Ambil info rilis terbaru dari GitHub. Tag vX.Y.Z + asset payload AllSense.exe.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
            req.Headers.UserAgent.ParseAdd("AllSense-Control/1.0");
            using var r = await Http.SendAsync(req);
            if (!r.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(), JsonOpts);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var te) ? te.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag)) return null;
            var version = tag.TrimStart('v', 'V');

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var ne) ? ne.GetString() : null;
                    // Payload update = AllSense.exe (bukan installer AllSense.setup.exe).
                    if (name is not null &&
                        name.Equals("AllSense.exe", StringComparison.OrdinalIgnoreCase) &&
                        a.TryGetProperty("browser_download_url", out var ue))
                        return new UpdateInfo(version, ue.GetString()!);
                }
            }
            return null;
        }
        catch { return null; }
    }

    public static bool IsNewer(string latestVersion)
    {
        try { return Version.Parse(latestVersion) > Version.Parse(CurrentVersion); }
        catch { return false; }
    }

    /// <summary>Unduh exe baru ke folder update. Return path jika berhasil.</summary>
    public static async Task<string?> DownloadAsync(string url, string version, Action<int>? progress)
    {
        try
        {
            var dir = Path.Combine(App.LocalDir, "update");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, "AllSense.exe");

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
            // Tolak unduhan yang mencurigakan (0 byte / terlalu kecil) agar
            // exe yang sehat tidak pernah digantikan file korup.
            return fs.Length > 200_000 ? dest : null;
        }
        catch { return null; }
    }

    /// <summary>Cek bisa menulis di lokasi exe berjalan (dibutuhkan tuk ganti diri sendiri).</summary>
    public static bool CanSelfReplace()
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath);
            if (string.IsNullOrEmpty(dir)) return false;
            var probe = Path.Combine(dir, ".allsense-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Tulis skrip penerap: tunggu app keluar, ganti exe, restart, hapus diri.
    /// Skrip jalan terpisah sehingga bisa menimpa exe yang sedang berjalan.
    /// </summary>
    public static string StageAndRestart(string downloadedExe)
    {
        var app = Environment.ProcessPath
            ?? throw new InvalidOperationException("Tidak dapat menentukan lokasi exe.");
        var dir = Path.Combine(App.LocalDir, "update");
        var script = Path.Combine(dir, "apply-update.cmd");

        File.WriteAllText(script,
            "@echo off\r\n" +
            "set \"SRC=" + downloadedExe + "\"\r\n" +
            "set \"DST=" + app + "\"\r\n" +
            ":wait\r\n" +
            "tasklist /FI \"IMAGENAME eq AllSense.exe\" | find /I \"AllSense.exe\" >nul && (\r\n" +
            "  ping -n 2 127.0.0.1 >nul\r\n" +
            "  goto wait\r\n" +
            ")\r\n" +
            "move /y \"%SRC%\" \"%DST%\"\r\n" +
            "if errorlevel 1 exit /b 1\r\n" +
            "start \"\" \"%DST%\"\r\n" +
            "del /q \"" + script + "\"\r\n");
        return script;
    }
}
