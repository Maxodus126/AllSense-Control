using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AllSense;

public sealed record UpdateInfo(string Version, string Url, string Sha256);

/// <summary>
/// Self-update via GitHub Releases. Alur:
/// cek release terbaru → unduh asset AllSense.exe → ganti exe berjalan + restart.
/// </summary>
public static class Updater
{
    public const string CurrentVersion = "2.0.1";
    private const string Repo = "Maxodus126/AllSense-Control";
    private const string LatestUrl = $"https://api.github.com/repos/{Repo}/releases/latest";

    private const long MinExeBytes = 200_000; // ambang minimum exe yang wajar (tolak file korup)

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonDocumentOptions JsonOpts = new() { AllowTrailingCommas = true };

    /// <summary>GET dengan retry/backoff singkat untuk gangguan jaringan sesaat.</summary>
    private static async Task<HttpResponseMessage?> GetWithRetryAsync(string url)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd("AllSense-Control/1.0");
                var resp = await Http.SendAsync(req);
                if (resp.IsSuccessStatusCode) return resp;
                resp.Dispose();
                if ((int)resp.StatusCode < 500) return null; // 4xx: tidak perlu retry
            }
            catch { }
            if (attempt < 2) await Task.Delay(400 * (attempt + 1));
        }
        return null;
    }

    /// <summary>Ambil info rilis terbaru dari GitHub. Tag vX.Y.Z + asset payload AllSense.exe.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var r = await GetWithRetryAsync(LatestUrl);
            if (r is null) return null;
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
                    {
                        var urlValue = ue.GetString();
                        if (string.IsNullOrWhiteSpace(urlValue)) return null;

                        // Integrity: hash SHA-256 harus dipublikasikan di release body
                        // (baris "sha256:<64 hex>"). Tanpa hash → update ditolak.
                        var body = root.TryGetProperty("body", out var be) ? be.GetString() : null;
                        var hash = ExtractSha256(body);
                        if (hash is null) return null; // tolak update tanpa checksum
                        return new UpdateInfo(version, urlValue, hash);
                    }
                }
            }
            return null;
        }
        catch { return null; }
    }

    private static string? ExtractSha256(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var s = line.Trim();
            if (s.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                var hex = s.AsSpan(7).Trim().ToString();
                if (hex.Length == 64 && hex.All(Uri.IsHexDigit)) return hex.ToLowerInvariant();
            }
        }
        // alternatif: hex 64字符 berdiri sendiri dengan prefix "SHA256 " atau di code fence
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var s = line.Trim().Trim('`');
            if (s.Length == 64 && s.All(Uri.IsHexDigit)) return s.ToLowerInvariant();
        }
        return null;
    }

    public static bool IsNewer(string latestVersion)
    {
        try { return Version.Parse(latestVersion) > Version.Parse(CurrentVersion); }
        catch { return false; }
    }

    /// <summary>Unduh exe baru ke folder update. Return path jika berhasil.</summary>
    public static async Task<string?> DownloadAsync(string url, string version, string expectedSha256, Action<int>? progress)
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
            var buf = new byte[81920];
            long read = 0;
            int n;
            await using (var fs = File.Create(dest))
            {
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await fs.WriteAsync(buf.AsMemory(0, n));
                    read += n;
                    if (total > 0) progress?.Invoke((int)Math.Min(100, read * 100 / total));
                }
            }

            // Validasi setelah file ditutup. Tolak & hapus unduhan yang mencurigakan
            // (0 byte / terlalu kecil / bukan PE / checksum salah) agar exe yang sehat
            // tidak pernah digantikan file korup, dan tidak ada sampah basi tertinggal.
            bool Ok()
            {
                try
                {
                    using var check = File.OpenRead(dest);
                    if (check.Length <= MinExeBytes) return false;
                    var hdr = new byte[2];
                    if (check.Read(hdr, 0, 2) != 2 || hdr[0] != (byte)'M' || hdr[1] != (byte)'Z')
                        return false;
                    check.Position = 0;
                    using var sha = SHA256.Create();
                    var actual = Convert.ToHexString(sha.ComputeHash(check)).ToLowerInvariant();
                    return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }

            if (!Ok())
            {
                try { File.Delete(dest); } catch { }
                return null;
            }
            return dest;
        }
        catch { return null; }
    }

    /// <summary>
    /// Smoke-test build baru SEBELUM menimpa: jalankan exe dalam mode
    /// ALLSENSE_SMOKE_TEST (App.xaml.cs membangun window WPF lalu keluar 0).
    /// Tangkap kegagalan boot (mis. CultureNotFoundException font cache
    /// InvariantGlobalization) yang membuat window tak pernah muncul.
    /// </summary>
    public static async Task<bool> SmokeTestAsync(string exePath)
    {
        try
        {
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.Environment[App.SmokeEnv] = "1";
            using var p = Process.Start(psi);
            if (p is null) return false;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch { }
                return false;
            }
            return p.ExitCode == 0;
        }
        catch { return false; }
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
        var backup = Path.Combine(dir, "AllSense.previous.exe");

        // Escape metacharacter cmd: gandakan '%' agar %VAR% di dalam path tidak
        // diekspansi cmd (path berisi '&'/'|'/'<'/'>' aman karena di-quote).
        static string Esc(string s) => s.Replace("%", "%%");
        var src = Esc(downloadedExe);
        var dst = Esc(app);
        var bak = Esc(backup);
        var scr = Esc(script);

        // backup sebelum timpa; tulis ke DST%.new (volume sama dgn DST) lalu
        // rename (atomic). Jika gagal menulis di folder instal, exit tanpa timpa.
        // Rollback: setelah start, tunggu ~6 dtk; bila proses baru mati (gagal boot),
        // pulihkan backup dan jalankan versi lama agar app tidak "mati" pasca-update.
        File.WriteAllText(script,
            "@echo off\r\n" +
            "set \"SRC=" + src + "\"\r\n" +
            "set \"DST=" + dst + "\"\r\n" +
            "set \"BAK=" + bak + "\"\r\n" +
            ":wait\r\n" +
            "tasklist /FI \"IMAGENAME eq AllSense.exe\" | find /I \"AllSense.exe\" >nul && (\r\n" +
            "  ping -n 2 127.0.0.1 >nul\r\n" +
            "  goto wait\r\n" +
            ")\r\n" +
            "copy /y \"%DST%\" \"%BAK%\" >nul\r\n" +
            "copy /y \"%SRC%\" \"%DST%.new\" >nul\r\n" +
            "if errorlevel 1 exit /b 1\r\n" +
            "move /y \"%DST%.new\" \"%DST%\" >nul\r\n" +
            "if errorlevel 1 exit /b 1\r\n" +
            "del /q \"%SRC%\"\r\n" +
            "start \"\" \"%DST%\"\r\n" +
            "ping -n 7 127.0.0.1 >nul\r\n" +
            "tasklist /FI \"IMAGENAME eq AllSense.exe\" | find /I \"AllSense.exe\" >nul || (\r\n" +
            "  copy /y \"%BAK%\" \"%DST%\" >nul\r\n" +
            "  start \"\" \"%DST%\"\r\n" +
            ")\r\n" +
            "del /q \"" + scr + "\"\r\n");
        return script;
    }
}
