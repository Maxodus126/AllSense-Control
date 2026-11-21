using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace AllSense;

public partial class MainWindow : Window
{
    private readonly UserSettings _settings = UserSettings.Load();
    private PowerMode _mode = PowerMode.Balanced;
    private readonly DispatcherTimer _timer;
    private UpdateInfo? _pending;
    private ModeOsd? _osd;
    private string? _hkToggleLabel, _hkCycleLabel;
    private bool _suppressApply;
    private IntPtr _hWnd;
    private int _awaitingHotKey;
    private TrayIcon? _tray;
    private EventWaitHandle? _wake;
    private volatile bool _wasOnAc = NativeInterop.GetPowerStatus().onAc;
    private PowerMode? _restoreOnAc;
    private bool _batteryWarned;
    private bool _quitRequested;
    private int _tickCounter;
    private bool _settingsDirty;
    private DispatcherTimer? _saveTimer;
    private bool _checking;
    private (bool ac, int pct) _lastBatt = (false, -2); // -2 = belum pernah

    // Konstanta interval di-tick (ms / tick units) — jangan angka ajaib tersebar.
    private const int PollIntervalMs = 2000;          // timer utama
    private const int RadioEveryTicks = 15;           // tiap 15×2 dtk = 30 dtk, saat visible
    private const int HardwareThrottleTicks = 3;      // brightness WMI tiap ~6 dtk
    private const int BatteryToastMs = 4000;          // durasi toast baterai rendah
    private const int SaveDebounceMs = 400;           // debounce simpan config

    private static readonly (int Mods, int Vk)[] ToggleFallback =
    {
        (NativeInterop.MOD_CONTROL | NativeInterop.MOD_ALT, (int)NativeInterop.VK_A),
        (NativeInterop.MOD_CONTROL | NativeInterop.MOD_ALT | NativeInterop.MOD_SHIFT, (int)NativeInterop.VK_A),
    };

    private static readonly (int Mods, int Vk)[] CycleFallback =
    {
        (NativeInterop.MOD_CONTROL | NativeInterop.MOD_ALT, (int)NativeInterop.VK_M),
        (NativeInterop.MOD_CONTROL | NativeInterop.MOD_ALT | NativeInterop.MOD_SHIFT, (int)NativeInterop.VK_M),
        (NativeInterop.MOD_CONTROL | NativeInterop.MOD_ALT, (int)NativeInterop.VK_N),
    };

    private static readonly Brush Panel2 = BrushFrom("#FF151821");
    private static readonly Brush Raised = BrushFrom("#00151821");
    private static readonly Brush Line = BrushFrom("#11FFFFFF");
    private static readonly Brush Label = BrushFrom("#FFC9D0DB");
    private static readonly Brush Muted = BrushFrom("#FF8D96A6");
    private static readonly Brush Dim = BrushFrom("#FF6E7686");
    private static readonly Brush Amber = BrushFrom("#FFFFB020");
    private static readonly Brush Blue = BrushFrom("#FF4D8DFF");
    private static readonly Brush Red = BrushFrom("#FFFF4356");
    private static readonly Brush Green = BrushFrom("#FF35C973");
    private static readonly Brush LineGreen = BrushFrom("#9935C973");
    private static readonly Brush PillIdle = BrushFrom("#08FFFFFF");

    // Brush lembut di-cache: dulu dibikin baru tiap tick timer (2 dtk) → sampah GC terus.
    private static readonly Brush SoftGreen = Soft(Green);
    private static readonly Brush SoftBlue = Soft(Blue);
    private static readonly Brush SoftRed = Soft(Red);
    private static readonly Brush SoftAmber = Soft(Amber);

    private static Brush BrushFrom(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze(); // brush statis read-only → hemat memori & aman lintas thread
        return b;
    }

    private static Brush Soft(Brush accent)
    {
        var c = ((SolidColorBrush)accent).Color;
        var b = new SolidColorBrush(Color.FromArgb(30, c.R, c.G, c.B));
        b.Freeze(); // read-only → dipakai lintas thread & hemat
        return b;
    }

    private static Brush SoftOf(Brush accent) =>
        ReferenceEquals(accent, Green) ? SoftGreen
        : ReferenceEquals(accent, Blue) ? SoftBlue
        : ReferenceEquals(accent, Red) ? SoftRed
        : ReferenceEquals(accent, Amber) ? SoftAmber
        : Soft(accent);

    public MainWindow()
    {
        // Harus SEBELUM InitializeComponent: nilai default XAML (Value="75"/"65")
        // memicu ValueChanged yang menulis hardware bila guard belum aktif.
        _suppressApply = true;
        InitializeComponent();
        VersionChip.Text = "v" + Updater.CurrentVersion;
        AboutVersion.Text = Updater.CurrentVersion;
        InstalledVersion.Text = Updater.CurrentVersion;
        BuildText.Text = Environment.OSVersion.Version.Build.ToString();
        DockRight();

        _mode = ParseMode(_settings.PowerMode);
        var cur = PowerControl.Current();
        if (cur.HasValue) _mode = cur.Value;
        ApplyModeVisual();

        // Muat nilai default dari config dulu (instan), lalu sinkronkan ke hardware
        // secara async setelah window tampil — supaya panel tidak terasa "menunggu".
        BrightSlider.Value = Math.Clamp(_settings.Brightness, BrightSlider.Minimum, BrightSlider.Maximum);
        VolSlider.Value = Math.Clamp(_settings.Volume, VolSlider.Minimum, VolSlider.Maximum);
        MuteTog.IsChecked = _settings.Mute;
        _suppressApply = false;

        DragBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollIntervalMs) };
        _timer.Tick += (_, _) =>
        {
            // Baterai wajib selalu dicek (peringatan rendah tetap aktif walau window di-tray).
            RefreshBattery();
            // Idle ringan: polling hardware (WMI/NAudio/WinRT) HANYA saat window terlihat.
            // Bila disembunyikan, tidak ada polling radio/hardware sama sekali → RAM & CPU hemat.
            if (Visibility != Visibility.Visible) return;
            // Radio (Wi-Fi/BT) di-update tiap 30 dtk: user/OS bisa mengubah status tanpa panel restart.
            if (++_tickCounter % RadioEveryTicks == 0) _ = LoadRadiosAsync();
            RefreshHardwareState();
        };
        _timer.Start();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SaveDebounceMs) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); if (_settingsDirty) { _settingsDirty = false; _settings.Save(); } };
        RefreshBattery(); // murah (GetSystemPowerStatus); wajib langsung utk peringatan
        InitSystemFeatures();

        // Logoff/shutdown: jangan tahan (izinkan app keluar), beda dengan X biasa.
        System.Windows.Application.Current.SessionEnding += (_, _) => _quitRequested = true;

        Loaded += (_, _) =>
        {
            // SizeToContent: Height belum terukur di constructor (NaN).
            // Ukur ulang di sini supaya panel benar-benar nempel kanan-bawah saat start.
            DockRight();

            _hWnd = new WindowInteropHelper(this).Handle;
            ApplyHotkeyBindings();
            var src = (HwndSource)PresentationSource.FromVisual(this)!;
            src.AddHook(WndProc);
            UpdateBehaviorUI();
            _ = AutoCheckUpdate();

            // Setelah frame pertama tampil, baru probe hardware (WMI/NAudio/WinRT):
            // window muncul instan, slider menyusul sinkron dengan kondisi asli.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateScreenInfo();
                InitRadios();
                RefreshHardwareState();
            }), DispatcherPriority.Background);

            // Bangunkan dari instance kedua (double-click exe/launcher) walau window di-tray.
            _wake = new EventWaitHandle(false, EventResetMode.AutoReset, "AllSense.ShowWindow");
            new Thread(() =>
            {
                try { while (_wake.WaitOne()) Dispatcher.Invoke(WakeShow); }
                catch { }
            }) { IsBackground = true }.Start();
        };
    }

    private void WakeShow()
    {
        if (Visibility == Visibility.Visible)
        {
            Activate();
            Topmost = true;
            Topmost = false;
        }
        else
        {
            Show();
            DockRight();
            RefreshBattery();
            RefreshHardwareState();
        }
    }

    // ---------- mode ----------
    private static PowerMode ParseMode(string s) => s.Trim().ToLowerInvariant() switch
    {
        "efficiency" => PowerMode.Efficiency,
        "performance" => PowerMode.Performance,
        _ => PowerMode.Balanced
    };

    private void Mode_Click(object sender, MouseButtonEventArgs e)
    {
        var next = ParseMode((string)((Border)sender).Tag);
        if (next == _mode) return;
        SetMode(next, manual: true);
    }

    /// <summary>Satu pintu keluar untuk ganti mode + simpan + visual + OSD.</summary>
    private void SetMode(PowerMode m, bool manual = false)
    {
        if (_mode == m && !manual) return;
        _mode = m;
        _settings.PowerMode = m.ToString();
        _settings.Save();
        ApplyModeVisual();
        var ok = PowerControl.Apply(m);
        App.LogOp($"Mode daya -> {m} ({(ok ? "ok" : "gagal")})");
        if (!ok)
            UpStatus.Text = "Peringatan: mode daya gagal diterapkan di perangkat ini.";
        (_osd ??= new ModeOsd()).Show(m);
        if (manual && !NativeInterop.GetPowerStatus().onAc)
            _restoreOnAc = null; // pilihan manual di baterai mengalahkan auto-restore
    }

    // ---------- tray / sistem ----------
    private void InitSystemFeatures()
    {
        if (_settings.TrayMode) EnsureTray();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        ApplyStartupRun();
    }

    private void EnsureTray()
    {
        if (_tray is not null) return;
        _tray = new TrayIcon
        {
            ToggleWindow = () => Dispatcher.Invoke(ToggleWindow),
            ExitApp = () => Dispatcher.Invoke(ConfirmExit),
            CycleMode = () => Dispatcher.Invoke(CycleMode),
        };
    }

    /// <summary>Keluar dari tray dengan konfirmasi (aplikasi selalu hidup, cegah salah klik).</summary>
    private void ConfirmExit()
    {
        var r = MessageBox.Show("Keluar dari AllSense Control?\nHotkey dan panel akan berhenti aktif.",
            "AllSense Control", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        _quitRequested = true;
        Close();
    }

    private void ToggleWindow()
    {
        if (Visibility == Visibility.Visible) Hide();
        else { Show(); DockRight(); RefreshBattery(); RefreshHardwareState(); }
    }

    private void CycleMode() => SetMode((PowerMode)(((int)_mode + 1) % 3), manual: true);

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // SystemEvents.PowerModeChanged diangkat di thread ThreadPool →
        // jangan sentuh UI di sini. Baca status murah, lalu delegasikan
        // ke Dispatcher agar ApplyModeVisual dan _osd.Show jalan di thread UI.
        var (ac, _) = NativeInterop.GetPowerStatus();
        if (ac == _wasOnAc) return;
        _wasOnAc = ac;
        // BeginInvoke (bukan Invoke sinkron): thread ThreadPool tidak boleh
        // diblokir menunggu UI, khususnya saat shutdown/teardown bisa menggantung.
        try { Dispatcher.BeginInvoke(() => OnPowerModeChangedCore(ac)); }
        catch { /* dispatcher sedang shutdown */ }
    }

    private void OnPowerModeChangedCore(bool ac)
    {
        if (!_settings.AutoPowerOnBattery) return;
        if (!ac)
        {
            if (_mode != PowerMode.Efficiency)
            {
                _restoreOnAc = _mode;
                SetMode(PowerMode.Efficiency);
            }
        }
        else if (_restoreOnAc.HasValue)
        {
            var restore = _restoreOnAc.Value;
            _restoreOnAc = null;
            SetMode(restore);
        }
    }

    private void ApplyStartupRun()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            const string name = "AllSenseControl";
            if (_settings.RunAtStartup && Environment.ProcessPath is { } exe)
                key?.SetValue(name, "\"" + exe + "\"");
            else
                key?.DeleteValue(name, throwOnMissingValue: false);
        }
        catch { }
    }

    private void ApplyModeVisual()
    {
        StyleMode(bdE, EIcon, ECap, ESub, _mode == PowerMode.Efficiency, Amber);
        StyleMode(bdB, BIcon, BCap, BSub, _mode == PowerMode.Balanced, Blue);
        StyleMode(bdP, PIcon, PCap, PSub, _mode == PowerMode.Performance, Red);
    }

    private static void StyleMode(Border bd, System.Windows.Shapes.Path icon,
        TextBlock cap, TextBlock sub, bool on, Brush accent)
    {
        bd.Background = on ? SoftOf(accent) : Raised;
        bd.BorderBrush = on ? accent : Brushes.Transparent;
        bd.BorderThickness = new Thickness(1);
        icon.Stroke = on ? accent : Dim;
        cap.Foreground = on ? Label : Muted;
        sub.Foreground = on ? accent : Dim;
    }

    // ---------- wifi / bt (indikator status saja) ----------
    private void InitRadios() => _ = LoadRadiosAsync();

    private async Task LoadRadiosAsync()
    {
        var (wifi, bt) = await RadioStatus.GetAsync();
        StyleRadio(wifi, bt);
    }

    private void StyleRadio(bool? wifiOn, bool? btOn)
    {
        StyleRadioOne(wifiOn, WifiT, WifiDot, WifiSt);
        StyleRadioOne(btOn, BtT, BtDot, BtSt);
    }

    private static void StyleRadioOne(bool? on, Border card, System.Windows.Shapes.Ellipse dot, TextBlock st)
    {
        var active = on == true;
        var unknown = on is null;
        card.Background = active ? SoftGreen : PillIdle;
        card.BorderBrush = active ? LineGreen : Line;
        card.BorderThickness = new Thickness(1);
        dot.Fill = active ? Green : Dim;
        st.Foreground = active ? Green : Dim;
        st.Text = unknown ? "?" : (active ? "ON" : "OFF");
        card.ToolTip = unknown
            ? "Radio tidak tersedia / status tidak dapat dibaca di perangkat ini."
            : (active ? "Aktif" : "Mati");
    }

    // ---------- screen ----------
    private void UpdateScreenInfo()
    {
        var (w, h, hz) = NativeInterop.GetDisplayMode();
        if (hz > 0) RefreshText.Text = $"{hz} Hz";
        if (w > 0) ResText.Text = $"{w} × {h}";
    }

    // ---------- battery ----------
    private void RefreshBattery()
    {
        var (ac, pct) = NativeInterop.GetPowerStatus();
        // Skip penulisan UI bila tidak ada perubahan → nol alokasi string saat idle
        // (sebelumnya $"" dibuat ulang tiap 2 dtk walau nilainya sama).
        if (ac == _lastBatt.ac && pct == _lastBatt.pct) return;
        _lastBatt = (ac, pct);

        AcChip.Background = ac ? SoftGreen : Panel2;
        AcChip.BorderBrush = ac ? LineGreen : Line;
        AcText.Foreground = ac ? Green : Muted;
        if (pct >= 0)
        {
            BatteryText.Text = $"{pct}% · {(ac ? "AC" : "BAT")}";
            AcText.Text = $"{(ac ? "AC" : "BAT")} · {(pct >= 0 ? pct + "%" : "—")}";
            BatBar.Width = pct / 100d * 46;
        }
        else
        {
            BatteryText.Text = "—";
            AcText.Text = $"{(ac ? "AC" : "BAT")} · —";
            BatBar.Width = 0;
        }

        CheckLowBattery(ac, pct);
    }

    private void CheckLowBattery(bool ac, int pct)
    {
        if (ac || pct < 0 || pct > _settings.LowBatteryThreshold)
        {
            _batteryWarned = false;
            return;
        }
        if (_batteryWarned) return;
        _batteryWarned = true;
        if (_tray is not null)
            _tray.Balloon("Baterai rendah", $"{pct}% tersisa. Colokkan charger.");
        else
            ShowBatteryToast(pct);
    }

    private void ShowBatteryToast(int pct)
    {
        // Tanpa tray (TrayMode mati) → peringatan muncul sebagai toast
        // window selip di kanan-bawah, lalu menghilang sendiri.
        var t = new ToastWindow
        {
            Left = SystemParameters.WorkArea.Right - 260,
            Top = SystemParameters.WorkArea.Bottom - 80,
            NoticeTitle = "Baterai rendah",
            NoticeBody = $"{pct}% tersisa. Colokkan charger.",
        };
        t.Show();
        _ = Task.Run(async () =>
        {
            await Task.Delay(BatteryToastMs);
            try
            {
                if (System.Windows.Application.Current is not null)
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (t.IsLoaded) t.Close();
                    });
            }
            catch { }
        });
    }

    // ---------- sinkronkan slider dengan perubahan hardware eksternal (Fn/OS) ----------
    private bool _syncBusy;
    private int _hwCounter;

    private async void RefreshHardwareState()
    {
        if (_syncBusy) return; // cegah tumpang-tindih bila WMI lambat
        _syncBusy = true;
        try
        {
            var doBright = ++_hwCounter % HardwareThrottleTicks == 0; // throttle WMI brightness tiap ~6 detik
            var snap = await Task.Run(() =>
            {
                int? b = null;
                if (doBright) b = BrightnessControl.Current();
                var vs = VolumeControl.GetState();
                return (b, vol: vs.Available ? vs.Level : -1f, mute: vs.Mute);
            });

            if (!BrightSlider.IsMouseCaptured && snap.b.HasValue)
            {
                var b = Math.Clamp(snap.b.Value, BrightSlider.Minimum, BrightSlider.Maximum);
                if ((int)BrightSlider.Value != b)
                {
                    _suppressApply = true;
                    BrightSlider.Value = b;
                    _suppressApply = false;
                }
            }

            if (snap.vol >= 0 && !VolSlider.IsMouseCaptured)
            {
                var v = (int)Math.Round(snap.vol * 100d);
                if ((int)VolSlider.Value != v)
                {
                    _suppressApply = true;
                    VolSlider.Value = Math.Clamp(v, VolSlider.Minimum, VolSlider.Maximum);
                    _suppressApply = false;
                }
                if (snap.mute != MuteTog.IsChecked)
                {
                    _suppressApply = true;
                    MuteTog.IsChecked = snap.mute;
                    _suppressApply = false;
                }
            }
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(App.LocalDir, "crash.log"), $"[{DateTime.Now:u}] RefreshHardware: {ex}\n\n"); } catch { }
        }
        finally { _syncBusy = false; }
    }

    // ---------- sliders ----------
    private void BrightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BrightVal != null)
            BrightVal.Text = ((int)e.NewValue).ToString();
        if (_suppressApply) return;
        if (BrightnessControl.CanSet)
            BrightnessControl.Set((int)e.NewValue);
        _settings.Brightness = (int)e.NewValue;
        ScheduleSave();
    }

    private void VolSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolVal != null)
            VolVal.Text = ((int)e.NewValue).ToString();
        if (_suppressApply) return;
        VolumeControl.SetLevel((float)(e.NewValue / 100d));
        _settings.Volume = (int)e.NewValue;
        ScheduleSave();
    }

    private void Mute_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressApply) return;
        var m = MuteTog.IsChecked == true;
        VolumeControl.SetMute(m);
        _settings.Mute = m;
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _settingsDirty = true;
        if (_saveTimer != null && !_saveTimer.IsEnabled) _saveTimer.Start();
    }

    // ---------- overlay / header / exit ----------
    private void SettingsBtn_Click(object sender, RoutedEventArgs e) => Overlay.Visibility = Visibility.Visible;
    private void SheetClose_Click(object sender, RoutedEventArgs e) => Overlay.Visibility = Visibility.Collapsed;
    private void Overlay_Click(object sender, MouseButtonEventArgs e) => Overlay.Visibility = Visibility.Collapsed;

    // ---------- pengaturan perilaku sistem ----------
    private void UpdateBehaviorUI()
    {
        ChkAutoBat.IsChecked = _settings.AutoPowerOnBattery;
        ChkStartup.IsChecked = _settings.RunAtStartup;
        ChkTray.IsChecked = _settings.TrayMode;
        BatWarn.Text = _settings.LowBatteryThreshold.ToString();
    }

    private void BatWarn_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var ok = int.TryParse(BatWarn.Text.Trim(), out var v) && v >= 5 && v <= 100;
        if (ok)
        {
            _settings.LowBatteryThreshold = v;
            _settings.Save();
            UpStatus.Text = $"Ambang baterai: {v}%";
        }
        else
        {
            BatWarn.Text = _settings.LowBatteryThreshold.ToString();
        }
    }

    private void ChkAutoBat_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoPowerOnBattery = ChkAutoBat.IsChecked == true;
        _settings.Save();
    }

    private void ChkStartup_Click(object sender, RoutedEventArgs e)
    {
        _settings.RunAtStartup = ChkStartup.IsChecked == true;
        _settings.Save();
        ApplyStartupRun();
    }

    private void ChkTray_Click(object sender, RoutedEventArgs e)
    {
        _settings.TrayMode = ChkTray.IsChecked == true;
        _settings.Save();
        if (_settings.TrayMode)
        {
            EnsureTray();
        }
        else
        {
            if (_tray is not null) { _tray.Dispose(); _tray = null; }
            if (Visibility != Visibility.Visible) { Show(); DockRight(); }
        }
    }
    private void MinBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_tray is not null) Hide();
        else WindowState = WindowState.Minimized;
    }
    private void HeaderClose_Click(object sender, RoutedEventArgs e) => Close();

    // Tombol "Keluar" di panel = exit asli (bukan sembunyi ke tray).
    private void ExitBtn_Click(object sender, MouseButtonEventArgs e)
    {
        _quitRequested = true;
        Close();
    }

    // X / Alt+F4 / Close() apa pun → sembunyikan ke tray, APLIKASI TETAP HIDUP.
    // Exit sejati hanya lewat tray "Keluar", tombol Keluar, atau update-restart.
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_quitRequested)
        {
            e.Cancel = true;
            if (_tray is not null) Hide();
            else WindowState = WindowState.Minimized;
            return;
        }
        base.OnClosing(e);
    }

    // ---------- update ----------
    // Cek otomatis saat startup; hening, hanya muncul bila ada versi baru.
    private async Task AutoCheckUpdate()
    {
        if (Interlocked.Exchange(ref _checking, true)) return;
        try
        {
            var up = await Task.Run(() => Updater.CheckAsync());
            App.LogOp(up is null ? "Cek update: tidak ada respons" : $"Cek update: rilis {up.Version}");
            if (up is not null && Updater.IsNewer(up.Version) && _pending is null)
            {
                _pending = up;
                UpStatus.Text = $"Versi {up.Version} tersedia";
                DlBtn.Visibility = Visibility.Visible;
            }
        }
        finally { _checking = false; }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (Interlocked.Exchange(ref _checking, true)) return;
        CheckBtn.IsEnabled = false;
        UpStatus.Text = "Memeriksa…";
        try
        {
            var up = await Task.Run(() => Updater.CheckAsync());
            if (up is null)
                UpStatus.Text = "Gagal / tidak ada pembaruan";
            else if (!Updater.IsNewer(up.Version))
                UpStatus.Text = "Sudah versi terbaru";
            else
            {
                _pending = up;
                UpStatus.Text = $"Versi {up.Version} tersedia";
                DlBtn.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            CheckBtn.IsEnabled = true;
            _checking = false;
        }
    }

    private async void DlBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_pending is null) return;
        DlBtn.IsEnabled = false;
        UpProgress.Visibility = Visibility.Visible;
        UpProgress.Value = 0;

        var dest = await Task.Run(() => Updater.DownloadAsync(_pending.Url, _pending.Version, _pending.Sha256,
            p => UpProgress.Dispatcher.Invoke(() => UpProgress.Value = p)));

        UpProgress.Visibility = Visibility.Collapsed;
        if (dest is null)
        {
            UpStatus.Text = "Unduhan gagal";
            DlBtn.IsEnabled = true;
            return;
        }

        try
        {
            var apply = MessageBox.Show("Update siap. Terapkan sekarang? Aplikasi akan ditutup dan restart otomatis.",
                "AllSense Control", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            if (!apply)
            {
                UpStatus.Text = "Unduhan dibatalkan.";
                DlBtn.IsEnabled = true;
                try { File.Delete(dest); } catch { }
                return;
            }

            if (!Updater.CanSelfReplace())
            {
                UpStatus.Text = "Lokasi instal butuh admin — jalankan sekali sebagai admin";
                DlBtn.IsEnabled = true;
                return;
            }

            // Smoke-test build baru SEBELUM menutup app: kalau gagal boot
            // (window tak muncul), tolak update & pertahankan versi lama.
            var smokeOk = await Task.Run(() => Updater.SmokeTestAsync(dest));
            if (!smokeOk)
            {
                UpStatus.Text = "Update ditolak: build baru gagal smoke-test (versi sekarang tetap dipakai).";
                DlBtn.IsEnabled = true;
                try { File.Delete(dest); } catch { }
                return;
            }

            var script = Updater.StageAndRestart(dest);
            Process.Start(new ProcessStartInfo("cmd.exe", $" /c start /b \"\" \"{script}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            _quitRequested = true;
            Close();
        }
        catch (Exception ex)
        {
            // Jangan tutup app di tengah update yang gagal; log & pulihkan UI.
            try { File.AppendAllText(Path.Combine(App.LocalDir, "crash.log"), $"[{DateTime.Now:u}] ApplyUpdate: {ex}\n\n"); } catch { }
            UpStatus.Text = "Update gagal diterapkan.";
            DlBtn.IsEnabled = true;
            try { File.Delete(dest); } catch { }
        }
    }

    // ---------- docking ----------
    private void DockRight()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 12;
        Top = wa.Bottom - Height - 12;
    }

    // ---------- hotkeys ----------
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeInterop.WM_HOTKEY)
        {
            if (wParam.ToInt32() == 1)
            {
                ToggleWindow();
                handled = true;
            }
            else if (wParam.ToInt32() == 2)
            {
                CycleMode();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void ApplyHotkeyBindings()
    {
        (_hkToggleLabel, _settings.HotkeyToggle) = BindPreferred(ToggleFallback, _settings.HotkeyToggle, 1);
        (_hkCycleLabel, _settings.HotkeyCycle) = BindPreferred(CycleFallback, _settings.HotkeyCycle, 2);
        RefreshLabels();
    }

    /// <summary>Coba daftar preferensi user, lalu fallback bila bentrok. Return (label terikat, nilai tersimpan).</summary>
    private (string? Label, string Saved) BindPreferred((int Mods, int Vk)[] fallback, string pref, int id)
    {
        if (_hWnd != IntPtr.Zero) NativeInterop.UnregisterHotKey(_hWnd, id);
        var p = ParseCombo(pref);
        if (p.HasValue &&
            NativeInterop.RegisterHotKey(_hWnd, id, (uint)p.Value.Mods, (uint)p.Value.Vk))
            return (HotKeyLabel(p.Value.Mods, (uint)p.Value.Vk), pref);
        foreach (var (m, v) in fallback)
        {
            if (NativeInterop.RegisterHotKey(_hWnd, id, (uint)m, (uint)v))
                return (HotKeyLabel(m, (uint)v), HotKeyLabel(m, (uint)v));
        }
        return (null, pref);
    }

    private static (int Mods, int Vk)? ParseCombo(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        int mods = 0, vk = -1;
        foreach (var raw in s.Split('+'))
        {
            var tok = raw.Trim();
            switch (tok.ToLowerInvariant())
            {
                case "ctrl": mods |= NativeInterop.MOD_CONTROL; break;
                case "alt": mods |= NativeInterop.MOD_ALT; break;
                case "shift": mods |= NativeInterop.MOD_SHIFT; break;
                case "win": mods |= 0x0008; break;
                default:
                    if (tok.Length == 1 && char.IsLetter(tok[0])) vk = char.ToUpper(tok[0]);
                    else if (tok.Equals("end", StringComparison.OrdinalIgnoreCase)) vk = 0x23;
                    else if (tok.Equals("home", StringComparison.OrdinalIgnoreCase)) vk = 0x24;
                    else if (tok.Length >= 2 && (tok[0] == 'f' || tok[0] == 'F')
                             && int.TryParse(tok.AsSpan(1), out var fn) && fn >= 1 && fn <= 24)
                        vk = 0x6F + fn;
                    break;
            }
        }
        return mods != 0 && vk >= 0 ? (mods, vk) : null;
    }

    private static string HotKeyLabel(int mods, uint vk)
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((mods & 0x0001) != 0) parts.Add("Alt");
        if ((mods & 0x0002) != 0) parts.Add("Ctrl");
        if ((mods & 0x0004) != 0) parts.Add("Shift");
        if ((mods & 0x0008) != 0) parts.Add("Win");
        parts.Add(vk >= 0x70 && vk <= 0x87
            ? "F" + (vk - 0x6F)
            : vk == 0x23 ? "End" : vk == 0x24 ? "Home" : ((char)vk).ToString());
        return string.Join("+", parts);
    }

    // ---------- rekam hotkey (klik label di Settings) ----------
    private void HKRecord_Click(object sender, MouseButtonEventArgs e)
    {
        _awaitingHotKey = (int)((FrameworkElement)sender).Tag;
        var lbl = _awaitingHotKey == 1 ? HKLabelA : HKLabelM;
        lbl.Text = "Tekan kombinasi… Esc=batal";
        lbl.Foreground = Amber;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_awaitingHotKey == 0) { base.OnPreviewKeyDown(e); return; }
        var id = _awaitingHotKey;
        _awaitingHotKey = 0;
        if (e.Key == Key.Escape) { RefreshLabels(); e.Handled = true; return; }
        if (IsModifierKey(e.Key)) { _awaitingHotKey = id; e.Handled = true; return; }
        var mods = 0;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= NativeInterop.MOD_CONTROL;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods |= NativeInterop.MOD_ALT;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods |= NativeInterop.MOD_SHIFT;
        TryBindNew(id, mods, KeyInterop.VirtualKeyFromKey(e.Key));
        e.Handled = true;
    }

    private static bool IsModifierKey(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.System;

    private void TryBindNew(int id, int mods, int vk)
    {
        if (mods == 0) { _awaitingHotKey = id; UpStatus.Text = "Perlu Ctrl/Alt/Shift + tombol."; return; }
        if (vk <= 0 || vk >= 0xE0) { _awaitingHotKey = id; UpStatus.Text = "Kombinasi tidak valid."; return; }
        if (_hWnd != IntPtr.Zero) NativeInterop.UnregisterHotKey(_hWnd, id);
        if (NativeInterop.RegisterHotKey(_hWnd, id, (uint)mods, (uint)vk))
        {
            var label = HotKeyLabel(mods, (uint)vk);
            if (id == 1) { _hkToggleLabel = label; _settings.HotkeyToggle = label; }
            else { _hkCycleLabel = label; _settings.HotkeyCycle = label; }
            _settings.Save();
            UpStatus.Text = $"Hotkey disimpan: {label}";
            RefreshLabels();
        }
        else
        {
            ApplyHotkeyBindings();
            UpStatus.Text = "Kombinasi sudah dipakai app lain; dikembalikan ke sebelumnya.";
        }
    }

    private void RefreshLabels()
    {
        HKLabelA.Text = _hkToggleLabel ?? "— bentrok —";
        HKLabelM.Text = _hkCycleLabel ?? "— bentrok —";
        HKLabelA.Foreground = HKLabelM.Foreground = Muted;
        HKFootA.Text = _hkToggleLabel ?? "—";
        HKFootM.Text = _hkCycleLabel ?? "—";
        if (!HKInputA.IsKeyboardFocused) HKInputA.Text = _hkToggleLabel ?? "";
        if (!HKInputM.IsKeyboardFocused) HKInputM.Text = _hkCycleLabel ?? "";
    }

    private void CyclePreset_Click(object sender, MouseButtonEventArgs e)
    {
        var combo = (string)((FrameworkElement)sender).Tag;
        var p = ParseCombo(combo);
        if (p.HasValue) TryBindNew(2, p.Value.Mods, p.Value.Vk);
    }

    private void TogglePreset_Click(object sender, MouseButtonEventArgs e)
    {
        var combo = (string)((FrameworkElement)sender).Tag;
        var p = ParseCombo(combo);
        if (p.HasValue) TryBindNew(1, p.Value.Mods, p.Value.Vk);
    }

    // ---------- input teks bebas (ketik kombinasi) ----------
    private void ApplyInputHotkey(int id)
    {
        var box = id == 1 ? HKInputA : HKInputM;
        var combo = box.Text.Trim();
        var p = ParseCombo(combo);
        if (!p.HasValue)
        {
            UpStatus.Text = "Format: Ctrl+Alt+X, Win+Shift+F5, Alt+F12, Ctrl+F9, …";
            return;
        }
        TryBindNew(id, p.Value.Mods, p.Value.Vk);
    }

    private void HKInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyInputHotkey((int)((FrameworkElement)sender).Tag);
            e.Handled = true;
        }
    }

    private void HKInput_Set(object sender, RoutedEventArgs e) =>
        ApplyInputHotkey((int)((Button)sender).Tag);

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _saveTimer?.Stop();
        if (_settingsDirty) { _settingsDirty = false; _settings.Save(); }
        var h = new WindowInteropHelper(this).Handle;
        NativeInterop.UnregisterHotKey(h, 1);
        NativeInterop.UnregisterHotKey(h, 2);
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _wake?.Dispose();
        if (_tray is not null) { _tray.Dispose(); _tray = null; }
        _osd?.Close();
        base.OnClosed(e);
    }
}
