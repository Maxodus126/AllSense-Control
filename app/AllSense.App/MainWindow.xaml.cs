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
    private readonly ModeOsd _osd = new();
    private string? _hkToggleLabel, _hkCycleLabel;
    private bool _suppressApply;
    private IntPtr _hWnd;
    private int _awaitingHotKey;
    private TrayIcon? _tray;
    private EventWaitHandle? _wake;
    private bool _wasOnAc = NativeInterop.GetPowerStatus().onAc;
    private PowerMode? _restoreOnAc;
    private bool _batteryWarned;

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

    private static readonly Brush Panel2 = BrushFrom("#FF1A1E28");
    private static readonly Brush Raised = BrushFrom("#FF1C212D");
    private static readonly Brush Line = BrushFrom("#12FFFFFF");
    private static readonly Brush Fg = BrushFrom("#FFEEF0F5");
    private static readonly Brush Muted = BrushFrom("#FF97A0AE");
    private static readonly Brush Dim = BrushFrom("#FF596272");
    private static readonly Brush Amber = BrushFrom("#FFFFB020");
    private static readonly Brush Blue = BrushFrom("#FF4D8DFF");
    private static readonly Brush Red = BrushFrom("#FFFF4356");
    private static readonly Brush Green = BrushFrom("#FF35C973");

    private static Brush BrushFrom(string hex) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    public MainWindow()
    {
        // Harus SEBELUM InitializeComponent: nilai default XAML (Value="75"/"65")
        // memicu ValueChanged yang menulis hardware bila guard belum aktif.
        _suppressApply = true;
        InitializeComponent();
        VersionChip.Text = "v" + Updater.CurrentVersion;
        AboutVersion.Text = Updater.CurrentVersion;
        InstalledVersion.Text = Updater.CurrentVersion;
        DockRight();

        _mode = ParseMode(_settings.PowerMode);
        var cur = PowerControl.Current();
        if (cur.HasValue) _mode = cur.Value;
        ApplyModeVisual();

        // Refleksikan kondisi aktual hardware; jangan paksa. Terapkan hanya saat user menggeser.
        var bright = BrightnessControl.Current() ?? _settings.Brightness;
        BrightSlider.Value = Math.Clamp(bright, BrightSlider.Minimum, BrightSlider.Maximum);
        if (VolumeControl.Available)
        {
            VolSlider.Value = Math.Clamp(VolumeControl.GetLevel() * 100d, VolSlider.Minimum, VolSlider.Maximum);
            MuteTog.IsChecked = VolumeControl.GetMute();
        }
        else
        {
            VolSlider.Value = Math.Clamp(_settings.Volume, VolSlider.Minimum, VolSlider.Maximum);
            MuteTog.IsChecked = _settings.Mute;
        }
        _suppressApply = false;

        InitRadios();
        UpdateScreenInfo();

        DragBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) =>
        {
            // Baterai wajib selalu dicek (peringatan rendah tetap aktif walau window di-tray).
            RefreshBattery();
            // Idle ringan: polling hardware (WMI) hanya saat window terlihat.
            if (Visibility != Visibility.Visible) return;
            RefreshHardwareState();
        };
        _timer.Start();
        RefreshBattery();
        RefreshHardwareState();
        InitSystemFeatures();

        Loaded += (_, _) =>
        {
            _hWnd = new WindowInteropHelper(this).Handle;
            ApplyHotkeyBindings();
            var src = (HwndSource)PresentationSource.FromVisual(this)!;
            src.AddHook(WndProc);
            UpdateBehaviorUI();
            _ = AutoCheckUpdate();

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
        _mode = m;
        _settings.PowerMode = m.ToString();
        _settings.Save();
        ApplyModeVisual();
        PowerControl.Apply(m);
        _osd.Show(m);
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
            CycleMode = () => Dispatcher.Invoke(CycleMode),
            ExitApp = () => Dispatcher.Invoke(Close),
        };
    }

    private void ToggleWindow()
    {
        if (Visibility == Visibility.Visible) Hide();
        else { Show(); DockRight(); RefreshBattery(); RefreshHardwareState(); }
    }

    private void CycleMode() => SetMode((PowerMode)(((int)_mode + 1) % 3), manual: true);

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // Transisi AC <-> baterai. Hanya bertindak bila opsi aktif.
        var (ac, _) = NativeInterop.GetPowerStatus();
        if (ac == _wasOnAc) return;
        _wasOnAc = ac;
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
        bd.Background = on ? Soft(accent) : Raised;
        bd.BorderBrush = on ? accent : Line;
        bd.BorderThickness = new Thickness(1);
        icon.Stroke = on ? accent : Dim;
        cap.Foreground = on ? Fg : Muted;
        sub.Foreground = on ? accent : Dim;
    }

    private static Brush Soft(Brush accent)
    {
        var c = ((SolidColorBrush)accent).Color;
        return new SolidColorBrush(Color.FromArgb(30, c.R, c.G, c.B));
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
        StyleRadioOne(wifiOn, WifiT, WifiIcWrap, WifiIc, WifiLbl, WifiDot, WifiSt);
        StyleRadioOne(btOn, BtT, BtIcWrap, BtIc, BtLbl, BtDot, BtSt);
    }

    private static void StyleRadioOne(bool? on, Border card, Border wrap,
        System.Windows.Shapes.Path icon, TextBlock lbl, System.Windows.Shapes.Ellipse dot, TextBlock st)
    {
        var active = on == true;
        var unknown = on is null;
        card.Background = active ? Soft(Green) : Raised;
        card.BorderBrush = active ? LineGreen() : Line;
        card.BorderThickness = new Thickness(1);
        wrap.Background = active ? Soft(Green) : BrushFrom("#FF20242F");
        icon.Stroke = active ? Green : Dim;
        lbl.Foreground = active ? Fg : Muted;
        dot.Fill = active ? Green : Dim;
        st.Foreground = active ? Green : Dim;
        st.Text = unknown ? "?" : (active ? "ON" : "OFF");
    }

    private static Brush LineGreen() => BrushFrom("#9935C973");

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
        AcChip.Background = ac ? Soft(Green) : Panel2;
        AcChip.BorderBrush = ac ? LineGreen() : Line;
        AcText.Foreground = ac ? Green : Muted;
        if (pct >= 0) BatteryText.Text = $"{pct}% · {(ac ? "AC" : "BAT")}";
        AcText.Text = $"{(ac ? "AC" : "BAT")} · {(pct >= 0 ? pct + "%" : "—")}";

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
        _tray?.Balloon("Baterai rendah", $"{pct}% tersisa. Colokkan charger.");
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
            var doBright = ++_hwCounter % 3 == 0; // throttle WMI brightness tiap ~6 detik
            var snap = await Task.Run(() =>
            {
                int? b = null;
                if (doBright) b = BrightnessControl.Current();
                float vol = -1f;
                bool mute = false;
                if (VolumeControl.Available) { vol = VolumeControl.GetLevel(); mute = VolumeControl.GetMute(); }
                return (b, vol, mute);
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
        catch { }
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
        _settings.Save();
    }

    private void VolSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolVal != null)
            VolVal.Text = ((int)e.NewValue).ToString();
        if (_suppressApply) return;
        VolumeControl.SetLevel((float)(e.NewValue / 100d));
        _settings.Volume = (int)e.NewValue;
        _settings.Save();
    }

    private void Mute_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressApply) return;
        var m = MuteTog.IsChecked == true;
        VolumeControl.SetMute(m);
        _settings.Mute = m;
        _settings.Save();
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
    private void ExitBtn_Click(object sender, MouseButtonEventArgs e) => Close();

    // ---------- update ----------
    // Cek otomatis saat startup; hening, hanya muncul bila ada versi baru.
    private async Task AutoCheckUpdate()
    {
        var up = await Task.Run(() => Updater.CheckAsync());
        if (up is not null && Updater.IsNewer(up.Version) && _pending is null)
        {
            _pending = up;
            UpStatus.Text = $"Versi {up.Version} tersedia";
            DlBtn.Visibility = Visibility.Visible;
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckBtn.IsEnabled = false;
        UpStatus.Text = "Memeriksa…";
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
        CheckBtn.IsEnabled = true;
    }

    private async void DlBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_pending is null) return;
        DlBtn.IsEnabled = false;
        UpProgress.Visibility = Visibility.Visible;
        UpProgress.Value = 0;

        var dest = await Task.Run(() => Updater.DownloadAsync(_pending.Url, _pending.Version,
            p => UpProgress.Dispatcher.Invoke(() => UpProgress.Value = p)));

        UpProgress.Visibility = Visibility.Collapsed;
        if (dest is null)
        {
            UpStatus.Text = "Unduhan gagal";
            DlBtn.IsEnabled = true;
            return;
        }

        var apply = MessageBox.Show("Update siap. Terapkan sekarang? Aplikasi akan ditutup dan restart otomatis.",
            "AllSense Control", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        if (!apply)
        {
            UpStatus.Text = "Unduhan selesai. Terapkan nanti lewat tombol berversi.";
            DlBtn.IsEnabled = true;
            return;
        }

        if (!Updater.CanSelfReplace())
        {
            UpStatus.Text = "Lokasi instal butuh admin — jalankan sekali sebagai admin";
            DlBtn.IsEnabled = true;
            return;
        }

        var script = Updater.StageAndRestart(dest);
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $" /c start /b \"\" \"{script}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch { }
        Close();
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
        var h = new WindowInteropHelper(this).Handle;
        NativeInterop.UnregisterHotKey(h, 1);
        NativeInterop.UnregisterHotKey(h, 2);
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _wake?.Dispose();
        if (_tray is not null) { _tray.Dispose(); _tray = null; }
        _osd.Close();
        base.OnClosed(e);
    }
}
