# DESAIN — Universal Power Control Center (ALL-SENSE style)

Proyek: kontrol power mode universal, jalan di sembarang laptop Windows.
Filosofi: **hanya API Windows resmi** — tanpa hook ACPI vendor, tanpa admin.
Tidak ada fan curve / GPU mux / undervolt / RGB (itu vendor-specific, bukan target).

---

## 1. Ringkasan visi

- Universal: nggak peduli merk laptop (ASUS, Lenovo, HP, Acer, Dell, ...).
- Tanpa admin (`asInvoker`).
- Satu EXE self-contained, kecil (~puluhan MB), trampil.
- Fitur inti: **Power Mode** (Best Efficiency / Balanced / Best Performance) + **monitoring suhu** CPU/GPU/RAM + status AC/DC.

---

## 2. Fitur v1 (cakupan)

| Fitur | Mekanisme |
|---|---|
| Power mode slider | `PowerSetUserConfigured{AC,DC}PowerMode` (Win11) → fallback `PowerSetActiveScheme` (legacy) |
| Deteksi AC/DC | `GetSystemPowerStatus` (kernel32) |
| Suhu CPU | LibreHardwareMonitor (inti CPU) → fallback zona termal ACPI (WMI) |
| Suhu GPU | LibreHardwareMonitor — sensor "core", BUKAN hot spot / memory junction |
| Suhu RAM / load / RPM fan | LibreHardwareMonitor |
| Status storage | WMI `Win32_LogicalDisk` |
| Tray icon + global hotkey | `RegisterHotKey` (Ctrl+Alt+A) |
| Update via GitHub | `api.github.com/repos/{owner/repo}/releases/latest` |
| Settings (simpan) | `%APPDATA%\AllSense\config.json` |

**Dengan sengaja TIDAK dibuat (v1):** kontrol fan, GPU mux, undervolt, RGB, battery charge limit, Wi-Fi/bluetooth toggle.

---

## 3. Stack

- .NET 8, WPF (XAML), x64, WinForms (buat tray icon).
- `PublishSingleFile` + `SelfContained`.
- `app.manifest` → `requestedExecutionLevel=asInvoker` (tanpa UAC).
- Dep: `LibreHardwareMonitorLib` (satu-satunya eksternal berat), `System.Management` (WMI), `powrprof.dll` via P/Invoke.

---

## 4. Arsitektur (proyek tunggal, folder rapi)

```
AllSense.App/
├── App.xaml(.cs)        # entry, crash log %TEMP%\AllSense-selftest\crash.log, dark title bar
├── MainWindow.xaml(.cs) # UI utama: dashboard + halaman power + halaman monitoring
├── RingGauge.xaml(.cs)  # gauge indikator bool
├── PowerControl.cs       # P/Invoke powrprof (Get/Set AC/DC mode, aktifkan skema)
├── HardwareMonitor.cs    # bungkus LibreHardwareMonitor; Poll() di thread belakang
├── SystemInfo.cs         # WMI: battery, storage, CPU name dll.
├── NativeInterop.cs      # RegisterHotKey, SetForegroundWindow, GetSystemPowerStatus
├── AppUpdate.cs          # cek/unduh/terapkan update (updater.bat)
├── Selftest.cs           # --selftest exit 0/1
└── app.manifest
```

### Aliran data (pola AllSense, wajib)
- **UI tak pernah blokir**: `Tick()` cuma jadwalin; `Poll()` + WMI jalan di `Task.Run`, update UI lewat `Dispatcher.BeginInvoke`. Guard `_polling` cegah tumpang-tindih.
- Sensor dibuka di thread background saat startup; kalau gagal → `Available=false`, UI tampil tanpa suhu.
- Power mode: 2 keluarga GUID, JANGAN ditukar:
  - **Mode slider**: `PowerSetUserConfigured{AC,DC}PowerMode` → Efficiency `961cc777-2547-4f9d-8174-7d86181b8a7a`, Balanced `00000000-0000-0000-0000-000000000000`, Performance `ded574b5-45a0-4f42-8737-46345c09c238`.
  - **Skema**: `PowerSetActiveScheme` → `a4f06079…`, `381b4222…`, `8c5e7fda…`. (GUID skema di API mode → error 87. Guard.)
- Suhu = **sensor "core"** (mis. NVIDIA `GPU Core`). JANGAN ambil max semua sensor (hot spot ~61/72°C bukan pembanding). Guard `!float.IsNaN(tg)` buat GPU kedua (iGPU tanpa sensor).

---

## 5. UI (gambaran)

- Dark theme native WPF (tanpa WebView2 — memori ~200 MB, bukan ~975 MB).
- Tab/panel:
  1. **Dashboard**: RingGauge suhu CPU/GPU/RAM + load.
  2. **Power**: pilih mode (3 tombol/radio), status AC/DC, info battery.
  3. **Sistem**: storage, versi, tombol cek update, set repo GitHub, hotkey.
- Tray: menu cepat ganti mode + buka window + keluar.
- Global hotkey Ctrl+Alt+A → bring to front.

---

## 6. Non-fungsional / aturan

- Tanpa admin, tanpa service, tanpa install OS.
- Crash → tulis crash.log, terus jalan (kalau bisa).
- Hotkey gagal daftar → rollback + warning, jangan crash.
- Update: parse semver, abaikan prerelease, pilih asset `.zip` x64; `updater.bat` tunggu PID mati → copy → restart → hapus diri.

---

## 7. Rencana implementasi (urutan)

1. Scaffold sln + WPF app + manifest asInvoker + selftest kosong.
2. `PowerControl.cs` — mode slider + skema + fallback, self-test `power.ok`, `power_modern.ok`, `perf_roundtrip.ok`, `power_read.ok`.
3. `HardwareMonitor.cs` — sensor CPU/GPU/RAM + WMI thermal fallback; `cpu_sensor.ok`, `gpu_sensor.ok`, `memory.ok`, `storage.ok`, `wmi.ok`.
4. `NativeInterop.cs` — hotkey + tray + bring-to-front; `hotkey_format.ok`.
5. `AppUpdate.cs` — `version.ok`, `update_tagparse.ok`.
6. UI: MainWindow + RingGauge + dark theme + polling thread belakang.
7. Publish single-file x64, uji selftest exit 0.

---

## 8. Risiko & keputusan

- **Win11 vs Win10**: mode slider API hanya Win11. Mesin Win10 → otomatis fallback skema. Deteksi via `Environment.OSVersion` / coba API + error.
- **Suhu CPU di beberapa laptop**: nggak ada inti CPU terbaca (Secure Boot) → fallback zona ACPI (label "SUHU SISTEM"). Tampil transparan di UI.
- **GPU tanpa sensor**: jalan di laptop iGPU/some GPU — guard NaN.
- **Scope creep**: fitur vendor (fan/RGB/GPU mux) **TIDAK** masuk v1. Kalau suatu saat mau: tambah antarmuka *provider* per-vendor, tapi inti universal tetap berdiri sendiri.

---

## 9. Nama proyek (saran)

- "AllSense Control" (dipakai ulang) atau lebih netral: **"PowerSense"**, **"LaptopControl"**, **"UniPower"**.
