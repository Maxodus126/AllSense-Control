using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AllSense;

public partial class MainWindow : Window
{
    private readonly UserSettings _settings = UserSettings.Load();
    private readonly DispatcherTimer _batteryTimer;
    private HwndSource? _src;
    private const int HotkeyToggle = 1;
    private const int HotkeyCycle = 2;

    public MainWindow()
    {
        InitializeComponent();
        ApplySettingsToUi();
        DockRight();

        _batteryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _batteryTimer.Tick += (_, _) => RefreshBattery();
        _batteryTimer.Start();
        RefreshBattery();
    }

    private void ApplySettingsToUi()
    {
        BrightSlider.Value = _settings.Brightness;
        VolSlider.Value = _settings.Volume;
        MuteBox.IsChecked = _settings.Mute;

        switch (_settings.PowerMode)
        {
            case "efficiency": ModeE.IsChecked = true; break;
            case "performance": ModeP.IsChecked = true; break;
            default: ModeB.IsChecked = true; break;
        }
    }

    private void DockRight()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 24;
        Top = wa.Top + 24;
    }

    // ── baterai ──
    private void RefreshBattery()
    {
        var (ac, pct) = NativeInterop.GetPowerStatus();
        BatteryText.Text = ac
            ? $"AC · {(pct >= 0 ? $"{pct}%" : "100%")}"
            : $"Baterai · {(pct >= 0 ? $"{pct}%" : "—")}";
        AcText.Text = ac ? "AC" : "DC";
        AcChip.Background ??= System.Windows.Media.Brushes.Transparent;
    }

    // ── power mode ──
    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tb || tb.Tag is not string kind) return;
        ModeE.IsChecked = kind == "efficiency";
        ModeB.IsChecked = kind == "balanced";
        ModeP.IsChecked = kind == "performance";
        _settings.PowerMode = kind;
        PowerControl.Apply(kind switch
        {
            "efficiency" => PowerMode.Efficiency,
            "performance" => PowerMode.Performance,
            _ => PowerMode.Balanced
        });
        _settings.Save();
    }

    // ── slider / mute ──
    private void BrightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)BrightSlider.Value;
        BrightVal.Text = $"{v}%";
        if (_settings is not null) _settings.Brightness = v;
        BrightnessControl.Set(v);
        _settings.Save();
    }

    private void VolSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)VolSlider.Value;
        VolVal.Text = $"{v}%";
        if (_settings is not null) _settings.Volume = v;
        VolumeControl.SetLevel(v / 100f);
        _settings.Save();
    }

    private void Mute_Changed(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;
        bool m = MuteBox.IsChecked == true;
        _settings.Mute = m;
        VolumeControl.SetMute(m);
        _settings.Save();
    }

    // ── settings overlay ──
    private void SettingsBtn_Click(object sender, RoutedEventArgs e) => Overlay.Visibility = Visibility.Visible;
    private void SheetClose_Click(object sender, RoutedEventArgs e) => Overlay.Visibility = Visibility.Collapsed;
    private void Overlay_Click(object sender, MouseButtonEventArgs e) => Overlay.Visibility = Visibility.Collapsed;

    // ── keluar ──
    private void ExitBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.Save();
        Close();
    }

    // ── hotkey ──
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _src?.AddHook(WndProc);
        if (_src is not null)
        {
            NativeInterop.RegisterHotKey(_src.Handle, HotkeyToggle,
                NativeInterop.MOD_CONTROL | NativeInterop.MOD_ALT, NativeInterop.VK_A);
            NativeInterop.RegisterHotKey(_src.Handle, HotkeyCycle,
                NativeInterop.MOD_CONTROL | NativeInterop.MOD_ALT, NativeInterop.VK_M);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeInterop.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HotkeyToggle)
            {
                if (WindowState == WindowState.Minimized || Visibility != Visibility.Visible)
                {
                    Show();
                    WindowState = WindowState.Normal;
                }
                else Hide();
                handled = true;
            }
            else if (id == HotkeyCycle)
            {
                var next = _settings.PowerMode switch
                {
                    "efficiency" => "balanced",
                    "balanced" => "performance",
                    _ => "efficiency"
                };
                _settings.PowerMode = next;
                ApplySettingsToUi();
                PowerControl.Apply(next switch
                {
                    "efficiency" => PowerMode.Efficiency,
                    "performance" => PowerMode.Performance,
                    _ => PowerMode.Balanced
                });
                _settings.Save();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        _batteryTimer.Stop();
        if (_src is not null)
        {
            NativeInterop.UnregisterHotKey(_src.Handle, HotkeyToggle);
            NativeInterop.UnregisterHotKey(_src.Handle, HotkeyCycle);
        }
        base.OnClosed(e);
    }
}
