using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;

namespace AllSense;

/// <summary>
/// Overlay OSD bergaya laptop gaming (ROG/Legion) pada pergantian mode daya:
/// badge ikon ber-glow, judul tebal, gauge energi yang terisi + scanline.
/// </summary>
internal sealed class ModeOsd : Window
{
    private const double TrackWidth = 210;

    private readonly Border _card;
    private readonly Border _badge;
    private readonly Path _icon;
    private readonly TextBlock _title;
    private readonly TextBlock _sub;
    private readonly Border _gaugeFill;
    private readonly Rectangle _scan;
    private readonly ScaleTransform _scale = new(0.9, 0.9);
    private readonly TranslateTransform _slide = new();
    private readonly TranslateTransform _scanMove = new();
    private readonly DispatcherTimer _hideTimer;

    private readonly record struct ModeSpec(Brush Accent, Brush Soft, Geometry Geom, string Title, string Sub, double Gauge);

    private static readonly Geometry[] Geoms =
    {
        Geometry.Parse("M12 3v8M6.4 6.6a8 8 0 1 0 11.2 0"),
        Geometry.Parse("M13 2 4.5 13.5H11L9.5 22 19.5 10H13L15.5 2Z"),
        Geometry.Parse("M12 8.75a3.25 3.25 0 1 0 0 6.5 3.25 3.25 0 0 0 0-6.5ZM19.4 15a1.6 1.6 0 0 0 .32 1.77l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.6 1.6 0 0 0-1.77-.32 1.6 1.6 0 0 0-.97 1.47V21a2 2 0 1 1-4 0v-.09a1.6 1.6 0 0 0-1.05-1.47 1.6 1.6 0 0 0-1.77.32l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.6 1.6 0 0 0 .32-1.77 1.6 1.6 0 0 0-1.47-.97H3a2 2 0 1 1 0-4h.09a1.6 1.6 0 0 0 1.47-1.05 1.6 1.6 0 0 0-.32-1.77l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.6 1.6 0 0 0 1.77.32h.02a1.6 1.6 0 0 0 1.05-1.47V3a2 2 0 1 1 4 0v.09a1.6 1.6 0 0 0 .97 1.47h.02a1.6 1.6 0 0 0 1.77-.32l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.6 1.6 0 0 0-.32 1.77v.02a1.6 1.6 0 0 0 1.47.97H21a2 2 0 1 1 0 4h-.09a1.6 1.6 0 0 0-1.51 1.05Z"),
    };

    private static readonly ModeSpec[] Specs =
    {
        new(BrushFrom("#FF38E08E"), BrushFrom("#2638E08E"), Geoms[0], "HEMAT DAYA", "EFFICIENCY", 0.35),
        new(BrushFrom("#FF4D8DFF"), BrushFrom("#264D8DFF"), Geoms[1], "BALANCED", "STANDARD", 0.62),
        new(BrushFrom("#FFFF4356"), BrushFrom("#26FF4356"), Geoms[2], "TURBO", "PERFORMANCE", 1.00),
    };

    public ModeOsd()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;

        _badge = new Border
        {
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(0, 0, 14, 0),
            BorderThickness = new Thickness(1),
        };
        _icon = new Path
        {
            StrokeThickness = 1.7,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 22,
            Height = 22,
            Stretch = Stretch.Uniform,
        };
        _badge.Child = _icon;

        _title = new TextBlock
        {
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = BrushFrom("#FFEEF0F5"),
        };
        _sub = new TextBlock
        {
            FontSize = 9,
            FontFamily = new FontFamily("Consolas"),
            Foreground = BrushFrom("#FF97A0AE"),
            Margin = new Thickness(0, 2, 0, 0),
        };
        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(_title);
        textStack.Children.Add(_sub);

        var contentRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        contentRow.Children.Add(_badge);
        contentRow.Children.Add(textStack);

        var track = new Border
        {
            Width = TrackWidth,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = BrushFrom("#22000000"),
            BorderBrush = BrushFrom("#30FFFFFF"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 12, 0, 0),
        };
        _gaugeFill = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _scan = new Rectangle { Width = 42, Height = 6, Fill = BrushFrom("#55FFFFFF"), RenderTransform = _scanMove, IsHitTestVisible = false };
        var gaugeLayer = new Grid { ClipToBounds = true };
        gaugeLayer.Children.Add(_gaugeFill);
        gaugeLayer.Children.Add(_scan);
        track.Child = gaugeLayer;

        var stack = new StackPanel();
        stack.Children.Add(contentRow);
        stack.Children.Add(track);

        _card = new Border
        {
            Child = stack,
            Padding = new Thickness(18, 14, 18, 14),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Background = BrushFrom("#E60E1017"),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var tg = new TransformGroup();
        tg.Children.Add(_scale);
        tg.Children.Add(_slide);
        _card.RenderTransform = tg;
        _card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 34, ShadowDepth = 6, Opacity = 0.5, Color = Colors.Black };

        Content = _card;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1650) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); PlayOutro(); };
    }

    public void Show(PowerMode mode)
    {
        var s = Specs[(int)mode];

        _icon.Data = s.Geom;
        _icon.Stroke = s.Accent;
        _badge.Background = s.Soft;
        _badge.BorderBrush = s.Accent;
        _card.BorderBrush = s.Accent;
        _title.Text = s.Title;
        _sub.Text = s.Sub;
        _gaugeFill.Background = s.Accent;
        _gaugeFill.Width = 0;

        PositionTopCenter();

        if (!IsVisible)
        {
            Opacity = 0;
            _scale.ScaleX = _scale.ScaleY = 0.9;
            _slide.Y = 0;
            _scanMove.X = -42;
            _title.Opacity = 0;
            Show();
        }
        PlayIntro(s.Gauge);
    }

    private void PositionTopCenter()
    {
        UpdateLayout();
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - ActualWidth) / 2;
        Top = wa.Top + 42;
    }

    private void PlayIntro(double gauge)
    {
        var sb = new Storyboard();

        var op = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTargetProperty(op, new PropertyPath(OpacityProperty));

        var sx = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTargetProperty(sx, new PropertyPath(ScaleTransform.ScaleXProperty));
        Storyboard.SetTarget(sx, _scale);
        var sy = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTargetProperty(sy, new PropertyPath(ScaleTransform.ScaleYProperty));
        Storyboard.SetTarget(sy, _scale);

        var top = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(230)) { BeginTime = TimeSpan.FromMilliseconds(90), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTargetProperty(top, new PropertyPath(TextBlock.OpacityProperty));
        Storyboard.SetTarget(top, _title);

        var fill = new DoubleAnimation(0, TrackWidth * gauge, TimeSpan.FromMilliseconds(500)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTargetProperty(fill, new PropertyPath(FrameworkElement.WidthProperty));
        Storyboard.SetTarget(fill, _gaugeFill);

        var scan = new DoubleAnimation(-42, TrackWidth + 42, TimeSpan.FromMilliseconds(640)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTargetProperty(scan, new PropertyPath(TranslateTransform.XProperty));
        Storyboard.SetTarget(scan, _scanMove);

        sb.Children.Add(op);
        sb.Children.Add(sx);
        sb.Children.Add(sy);
        sb.Children.Add(top);
        sb.Children.Add(fill);
        sb.Children.Add(scan);
        sb.Begin(this);

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void PlayOutro()
    {
        var sb = new Storyboard();
        var op = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTargetProperty(op, new PropertyPath(OpacityProperty));
        op.Completed += (_, _) => Hide();
        var slide = new DoubleAnimation(0, -14, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTargetProperty(slide, new PropertyPath(TranslateTransform.YProperty));
        Storyboard.SetTarget(slide, _slide);
        sb.Children.Add(op);
        sb.Children.Add(slide);
        sb.Begin(this);
    }

    private static Brush BrushFrom(string hex) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
}
