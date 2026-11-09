# RANCANGAN IMPLEMENTASI — AllSense Control

Dokumen ini memetakan produk (per kisi UI yang sudah disepakati) ke arsitektur & langkah build konkret.

---

## 1. Ringkasan riset (fakta terverifikasi)

| Fitur | Mekanisme | Hasil riset |
|---|---|---|
| Power Mode | `PowerSetUserConfigured{AC,DC}PowerMode` | ✅ Ada di powrprof.dll (diuji di mesin, Win11 26100). Win10 → fallback `PowerSetActiveScheme`. Tanpa admin |
| Brightness | WMI `WmiSetBrightness` | ✅ Class & method ada di `root\wmi` (diuji). Tanpa admin. Panel eksternal sebagian read-only |
| Volume / Mute | CoreAudio `IAudioEndpointVolume` | ✅ NAudio 3 (MIT) expose master volume. Tanpa admin |
| Bluetooth toggle | WinRT `Windows.Devices.Radios.Radio` | ✅ Win10+, `RequestAccessAsync` = konsen user, bukan admin |
| Wi-Fi toggle | WMI `MSFT_NetAdapter` | ⚠️ Tidak ada API publik non-admin → butuh UAC sekali |
| Refresh Rate | `EnumDisplaySettings` + `ChangeDisplaySettingsEx` | ✅ (docs). `CDS_TEST` dulu, lalu apply. Tanpa admin |
| Global hotkey | `RegisterHotKey` | ✅ user32 |
| Tray | WinForms `NotifyIcon` | ✅ |
| Update | GitHub API *releases/latest* | ✅ semver |

**Keputusan stack**
- **.NET 10 (LTS, rilis Nov-2025, dukungan s/d Nov-2028)** — .NET 8 habis hari ini (10-Nov-2026).
- Target `net10.0-windows`, WPF + WinForms (tray).
- Dep:
  - `Microsoft.Windows.SDK.NET.Ref` → interop WinRT (Bluetooth Radio).
  - `System.Management` 10.0.14 → WMI (brightness, wifi, battery).
  - `NAudio` 3 → volume.
- Publish `SingleFile` + `SelfContained` → 1 EXE (x64), manifest `asInvoker` (tanpa UAC default).
- `PublishTrimmed=false` — WMI pakai reflection; trim berisiko.

**Yang TIDAK dibuat (sengaja):** HDR, Auto Brightness (tidak universal), monitoring CPU/GPU (di luar scope), kontrol vendor TDP/fan.

---

## 2. Proyek tunggal (folder rapi)

```
AllSense.App/
├─ AllSense.App.csproj        # net10.0-windows; UseWPF+UseWindowsForms; PublishSingleFile
├─ app.manifest               # requestedExecutionLevel=asInvoker; per-monitor DPI
├─ App.xaml / App.xaml.cs     # bumper crash → %TEMP%\AllSense\crash.log; dark title bar
├─ MainWindow.xaml(.cs)       # UI (sesuai mockup): dock kanan, non-resizable
├─ TrayIcon.cs                # NotifyIcon + menu: buka, ganti mode, keluar
├─ NativeInterop.cs           # P/Invoke umum: GetSystemPowerStatus, RegisterHotKey, SetForegroundWindow, EnumDisplaySettings/ChangeDisplaySettingsEx
├─ PowerControl.cs            # mode slider + skema (GUID, fallback)
├─ VolumeControl.cs           # NAudio IAudioEndpointVolume: get/set %, mute
├─ BrightnessControl.cs       # WMI WmiSetBrightness + read
├─ RadioControl.cs            # WinRT Radio: enumerate, toggle Bluetooth
├─ WifiControl.cs             # WMI MSFT_NetAdapter (elevated sekali)
├─ SystemInfo.cs              # WMI: battery, monitor (resolusi/refresh)
├─ Settings.cs                # config JSON (%APPDATA%\AllSense\config.json)
├─ AppUpdate.cs               # GitHub release: semver, unduh, updater.bat
├─ Selftest.cs                # --selftest exit 0/1
└─ Resources/                 # ikon tray, ikon UI (SVG→Path/XAML)
```

---

## 3. Alur runtime

1. **Startup**: baca config → terapkan power mode tersimpan → mulai poll baterai (interval 2 dtk, thread background) → daftar hotkey `Ctrl+Alt+A` → mount tray.
2. **Window**: dock kanan — `Left = WorkingArea.Right − Lebar − 20`, `Top = 20`. Non-resizable, `SizeToContent=Height`, selalu di atas opsi.
3. **Update UI**: `Dispatcher.BeginInvoke` dari background (jangan blokir UI). Guard `_polling` cegah tumpang-tindih.
4. **Hotkey**: bring-to-front / toggle visibility.
5. **Keluar**: simpan config, lepas hotkey, exit.

---

## 4. Per-fitur detail

### Power Mode (PowerControl.cs)
- GUID mode slider (jangan tertukar dgn skema):
  - Efficiency  `961cc777-2547-4f9d-8174-7d86181b8a7a`
  - Balanced   `00000000-0000-0000-0000-000000000000`
  - Performance `ded574b5-45a0-4f42-8737-46345c09c238`
- Alur set: coba `PowerSetUserConfigured{AC,DC}PowerMode` (untuk AC & DC).
  - Gagal (`E_NOTIMPL` / Win10) → fallback `PowerSetActiveScheme`:
    - Efficiency `a4f06079…`, Balanced `381b4222…`, Performance `8c5e7fda…`
- Baca mode aktif: `PowerGetActiveScheme` + bandingkan GUID → sinkronkan UI.

### Brightness (BrightnessControl.cs)
- Read: `WmiMonitorBrightness.CurrentBrightness` (`root\wmi`).
- Set: `WmiMonitorBrightnessMethods.WmiSetBrightness(timeout, brightness)` per-instance monitor.
- Fallback: kalau `Levels ≤ 1` atau method gagal → disable slider (tampilkan "tidak didukung").

### Volume (VolumeControl.cs)
- `NAudio.CoreAudioApi.MMDeviceEnumerator` → default endpoint → `IAudioEndpointVolume`.
- `GetMasterVolumeLevelScalar()` / `SetMasterVolumeLevelScalar(v)` (0–1) + `Mute`/`Unmute`.

### Bluetooth (RadioControl.cs)
- `Radio.GetRadiosAsync()` → filter `Kind == RadioKind.Bluetooth`.
- `Radio.RequestAccessAsync()` → kalau `Allowed`, `SetStateAsync(On/Off)`.
- Tanpa perangkat BT → toggle tampil "off / tidak tersedia".

### Wi-Fi (WifiControl.cs)
- Tanpa admin, tidak ada API. Alur:
  1. Deteksi status: `MSFT_NetAdapter` (MediaType = Native802.11 / Wireless).
  2. Saat toggle: relaunch diri `verb=runas` dengan argumen `--wifi on|off` (UAC sekali). Child proses set `Set-NetAdapter` lalu keluar.
  3. Kalau UAC ditolak → UI kembali & beri catatan.

### Refresh Rate (NativeInterop.cs)
- `EnumDisplaySettings` enumerate mode panel utama (dari `EnumDisplayDevices` primary).
- Set: isi `DEVMODE.dmDisplayFrequency` + `dmFields=DM_DISPLAYFREQUENCY`, `CDS_TEST` → sukses, `ChangeDisplaySettingsEx` (dynamic).

### Update (AppUpdate.cs)
- `GET https://api.github.com/repos/{owner}/{repo}/releases/latest` (Header `User-Agent` wajib).
- Parse `tag_name` semver; wajib prerelease; bandingkan dgn versi lokal.
- Asset `.zip` x64 → ekstrak → `updater.bat`: tunggu PID mati → salin EXE baru → restart → hapus diri.

### Config (Settings.cs)
```json
{
  "powerMode": "balanced",
  "brightness": 78,
  "volume": 65,
  "mute": false,
  "hotkey": "Ctrl+Alt+A"
}
```

---

## 5. Selftest (`--selftest`)
Exit 0/1, tulis `%TEMP%\AllSense\selftest.log`. Cek:
- `power_modern.ok` — export ada + set/get mode.
- `power_legacy.ok` — `PowerSetActiveScheme` tersedia.
- `brightness.ok` — class WMI kebuka (+ `Levels>1`).
- `volume.ok` — endpoint audio terbuka.
- `display_enum.ok` — mode refresh ter-identifikasi.
- `battery.ok` — `GetSystemPowerStatus` benar.
- `config_io.ok` — tulis/baca config.

---

## 6. Langkah implementasi (urutan)

1. Scaffold solution + proyek WPF + manifest `asInvoker` + selftest kosong.
2. `Settings.cs` (config) + `SystemInfo.cs` (baterai) → verifikasi.
3. `PowerControl.cs` (mode + fallback) + selftest power.
4. `BrightnessControl.cs`, `VolumeControl.cs`, `RadioControl.cs`, `WifiControl.cs`.
5. `NativeInterop.cs` (hotkey, display, systempower).
6. `AppUpdate.cs` + `updater.bat`.
7. UI: `MainWindow` (XAML dari mockup) + tray + polling.
8. Publish `win-x64` single-file + uji selftest exit 0.

---

## 7. Risiko & mitigasi

| Risiko | Mitigasi |
|---|---|
| Win10: mode slider gagal | Auto-fallback skema; UI tetap 3 mode |
| Wi-Fi butuh UAC | Relaunch elevated sekali; timeout/tolak → status tetap |
| WMI lambat | Poll di background; guard `_polling` |
| Trim/reflection WMI | `PublishTrimmed=false` |
| WinRT butuh SDK projection | `Microsoft.Windows.SDK.NET.Ref` (net9+) |
| Sebagian laptop brightness read-only | Slider disable + label "tidak didukung" |
| GPIO hotkey bentrok | `RegisterHotKey` gagal → warning, tanpa crash |

---

## 8. Optimasi: ringan, optimal, hemat RAM (target)

**Target terukur (Windows 11, x64):**
- Working set idle ≈ **35–55 MB** (WPF dasar ~30–45 MB).
- CPU idle ≈ **≈0–1%** (tanpa polling aktif).
- Disk: **1 EXE** self-contained ≈ 60–90 MB (dengan kompresi single-file).

**Strategi wajib:**
1. **Lazy load modul** — jangan inisialisasi semuanya di startup. `PowerControl`, `VolumeControl`, `RadioControl`, WMI dibuka **hanya saat panel itu dipakai/pertama kali**. Startup hanya: config, window, tray, hotkey, poll baterai.
2. **Polling cerdas & hemat** — baca baterai tiap **2 dtk** saat jendela terlihat; **berhenti saat window tersembunyi/minimized** (fallback: poll ulang saat dibuka). JANGAN gunakan timer rapat (~10ms) karena bangun CPU.
3. **Bebas GC churn di loop** — reuse objek/struct buffer (jangan `new` ribuan per tick); hindari string interp di jalur panas; panggil `GC.Collect` tidak pernah manual.
4. **Satu instance, satu window** — `Mutex` cegah multi-instance; window non-resizable `SizeToContent`.
5. **WPF resource discipline** — hapus asset tak terpakai; cache `BitmapImage`; pakai `Freeze()` pada brush/geometry statis (kurangi alokasi & render cost); reuse style resource dictionary tunggal.
6. **Tidak ada WebView / browser engine** — murni XAML. (Salah satu alasan pilih WPF: memori ~45 MB vs WebView2 ~200+ MB.)
7. **Framework pilihan** — .NET 10 self-contained; kalau ukuran disk lebih penting → `EnableCompressionInSingleFile=true` + `IncludeNativeLibrariesForSelfExtract=true`. 
   - Opsi ekstrem (bila perlu): **Framework-dependent** (EXE ~1 MB, tapi butuh .NET runtime di sistem) — putuskan saat publish.
8. **Trim**: tetap `PublishTrimmed=false` (WMI/reflection), tapi limpahkan `IlcTrimMetadata`? Tidak — hindari AOT. Peroleh ent lewat disiplin kode, bukan tool.
9. **Bersih saat keluar** — lepas `RegisterHotKey`, dispose tray + event handler, tutup WMI scope, supaya tidak ada handle bocor (memory leak).
10. **Prioritas proses** — tidak perlu override; default sudah cukup.

**Kompromi jujur:** .NET runtime sendiri membawa baseline ~30–45 MB (tidak bisa di bawah itu tanpa AOT). Angka di atas adalah target realistis untuk WPF; untuk sub-20 MB hanya via NativeAOT → tapi itu mempersulit WMI/WinRT, tidak direkomendasikan untuk proyek ini.
