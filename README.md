<div align="center">

# ⚡ AllSense Control

**Panel kontrol daya universal untuk laptop Windows — tanpa admin, tanpa driver vendor.**

[![CI](https://img.shields.io/github/actions/workflow/status/Maxodus126/AllSense-Control/build.yml?branch=main&label=CI&logo=github&style=flat-square)](https://github.com/Maxodus126/AllSense-Control/actions)
[![Rilis](https://img.shields.io/github/v/release/Maxodus126/AllSense-Control?style=flat-square&logo=github&label=versi)](https://github.com/Maxodus126/AllSense-Control/releases/latest)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Windows-10%2F11-0078D6?style=flat-square&logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![Situs](https://img.shields.io/badge/situs-live-4D8DFF?style=flat-square&logo=githubpages&logoColor=white)](https://maxodus126.github.io/AllSense-Control/)

**Unduh:** [AllSense.setup.exe](https://github.com/Maxodus126/AllSense-Control/releases/latest/download/AllSense.setup.exe) · [Lihat situs](https://maxodus126.github.io/AllSense-Control/)

</div>

---

AllSense Control adalah utility ringkas bergaya *gaming laptop* yang menyatukan kontrol **mode daya**, **kecerahan**, **volume**, dan **hotkey global** dalam satu panel kecil yang ter-dock di kanan layar. Berjalan di **Windows 10/11**, **tanpa hak admin**, dan **tanpa driver vendor** — kompatibel lintas merek (ASUS, Lenovo, HP, Acer, Dell, MSI, …).

## ✨ Fitur

| | Fitur | Cara kerja |
|---|---|---|
| 🔋 | **Mode daya** (Hemat / Seimbang / Turbo) | `PowerSetUserConfigured{AC,DC}PowerMode` (Win11), fallback `PowerSetActiveScheme` (legacy) |
| 🌞 | **Kecerahan** | WMI `WmiSetBrightness`, fallback DDC/CI (`dxva2`) bila WMI tak tersedia |
| 🔊 | **Volume & Mute** | CoreAudio `IAudioEndpointVolume` (NAudio) |
| 📡 | **Status Wi-Fi / Bluetooth** (indikator) | WinRT `Windows.Devices.Radios` |
| 🗔 | **Global hotkey** | `RegisterHotKey` — toggle window + ganti mode, preset & kustom |
| 🧊 | **System tray** | toggle window, ganti mode, keluar |
| 🔄 | **Self-update otomatis** | GitHub Releases → unduh → ganti exe → restart |
| 🔌 | **Auto power mode** | beralih ke Hemat saat baterai, pulihkan saat AC |
| 🚨 | **Peringatan baterai rendah** | ambang bisa diatur (5–100%) |
| 👁 | **OSD animasi** | overlay ala ROG/Legion pada pergantian mode |

## 📸 Demo & Screenshot

> Screenshot statis menyusul — sementara itu, coba interaktif:

- 🖥️ **Demo interaktif**: [app-demo.html](docs/app-demo.html)
- 🌐 **Situs produk**: [maxodus126.github.io/AllSense-Control/](https://maxodus126.github.io/AllSense-Control/)
- 🎥 OSD `TURBO` / `BALANCED` / `HEMAT DAYA` muncul tiap kali mode diganti.

## 📦 Instalasi

**Cara termudah** — unduh satu file, jalankan, selesai:

```
https://github.com/Maxodus126/AllSense-Control/releases/latest/download/AllSense.setup.exe
```

- `AllSense.setup.exe` = installer **self-contained** (payload tertanam, tanpa admin).
- Terpasang ke `%LocalAppData%\Programs\AllSense`, shortcut di Desktop & Start Menu.
- Ikon ditanam langsung di executable (Explorer / taskbar / alt+Tab).

> 💡 Jika Windows menampilkan *Unknown publisher* (file tidak ditandatangani), klik **More info → Run anyway**. *Code signing* ada di roadmap.

### Update

Aplikasi memeriksa rilis terbaru saat mulai; ada versi baru → tombol unduh muncul → terapkan → restart otomatis. Tanpa uninstall/reinstall.

## 🔬 Cara kerja (hanya API Windows resmi)

| Kemampuan | API | Tanpa admin |
|---|---|---|
| Mode daya | `powrprof.dll` (user-configured AC/DC) | ✅ |
| Kecerahan | WMI `root\wmi` / `dxva2.dll` | ✅ |
| Volume | CoreAudio `IAudioEndpointVolume` | ✅ |
| Hotkey global | `user32::RegisterHotKey` | ✅ |
| Status AC/baterai | `kernel32::GetSystemPowerStatus` | ✅ |
| Self-update | GitHub Releases API | ✅ |

Filosofi: **tanpa hook ACPI vendor, tanpa driver, tanpa UAC**. Yang vendor-specific (fan curve, GPU mux, undervolt, RGB) sengaja tidak disentuh.

## 🛠️ Membangun dari sumber

```bash
# prasyarat: .NET 10 SDK
dotnet publish app/AllSense.App/AllSense.App.csproj -c Release   # -> AllSense.exe
dotnet publish app/Installer/Installer.csproj -c Release -o publish/installer  # -> AllSenseSetup.exe
```

Struktur:

```
app/
├─ AllSense.App/        # aplikasi utama (WPF + WinForms tray)
└─ Installer/           # installer WPF, payload AllSense.exe tertanam
.github/workflows/
├─ build.yml            # CI: build + publish single-file
├─ release.yml          # rilis otomatis saat tag v* (setup.exe + AllSense.exe)
└─ pages.yml            # deploy situs (docs/) ke GitHub Pages
docs/                   # situs produk (html)
```

### Rilis otomatis

Tag `v*` → GitHub Actions membangun, memuat **`AllSense.setup.exe`** + **`AllSense.exe`** ke Release. Situs otomatis menunjuk ke `releases/latest`.

## 🗺️ Roadmap

- [x] Mode daya AC/DC
- [x] Self-update
- [x] Ikons tertanam & installer self-contained
- [x] Fallback kecerahan DDC/CI
- [ ] Code signing (hapus peringatan SmartScreen)
- [ ] Opsi mulai-minimized
- [ ] Bahasa UI (EN/ID)

## 🤝 Kontribusi

PR & issue disambut. Pastikan `dotnet build` lolos dan uji pada laptop berbasis GPU berbeda.

## ⚖️ Lisensi & Disclaimer

Didesain oleh **Maxodus**. Perangkat lunak disediakan *"sebagaimana adanya"* tanpa jaminan; pemakaian menanggung risiko sendiri. Tidak mengumpulkan data pribadi; hanya memanfaatkan API Windows standar.
