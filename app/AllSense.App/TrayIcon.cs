using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AllSense;

/// <summary>
/// Ikon system tray (System.Windows.Forms.NotifyIcon):
/// klik-kiri togel window, klik-kanan menu (Tampilkan, Ganti Mode, Keluar).
/// Ikon menampilkan level baterai (warna: hijau/kuning/merah); tooltip menampilkan detail.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public Action? ToggleWindow;
    public Action? CycleMode;
    public Action? ExitApp;

    public TrayIcon()
    {
        _icon = new NotifyIcon
        {
            Text = "AllSense Control",
            Icon = CreateIcon(null),
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => ToggleWindow?.Invoke();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Tampilkan / Sembunyikan", null, (_, _) => ToggleWindow?.Invoke());
        menu.Items.Add("Ganti Mode Daya", null, (_, _) => CycleMode?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Keluar", null, (_, _) => ExitApp?.Invoke());
        _icon.ContextMenuStrip = menu;
    }

    /// <summary>Perbarui ikon + tooltip berdasar level baterai.</summary>
    public void SetBattery(int? percent, bool onAc)
    {
        _icon.Icon = CreateIcon(percent);
        _icon.Text = percent.HasValue
            ? $"AllSense Control — Baterai {percent}% · {(onAc ? "AC" : "BAT")}"
            : "AllSense Control";
    }

    /// <summary>Bubble notifikasi (untuk peringatan baterai rendah).</summary>
    public void Balloon(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.Warning);

    private static Icon CreateIcon(int? percent)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // latar persegi bulat gelap
            using (var bg = new SolidBrush(Color.FromArgb(16, 18, 26)))
            using (var bgPath = RoundRect(1, 1, 30, 30, 8))
                g.FillPath(bg, bgPath);

            if (percent.HasValue)
                DrawBattery(g, percent.Value);
            else
                DrawBolt(g);
        }
        var handle = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        return icon;
    }

    private static void DrawBolt(Graphics g)
    {
        using var bolt = new SolidBrush(Color.FromArgb(255, 82, 72));
        Point[] pts =
        {
            new(18, 4), new(8, 17), new(15, 17),
            new(14, 28), new(24, 15), new(17, 15),
        };
        g.FillPolygon(bolt, pts);
    }

    private static void DrawBattery(Graphics g, int percent)
    {
        var color = percent switch
        {
            <= 20 => Color.FromArgb(255, 82, 72),   // merah
            <= 40 => Color.FromArgb(255, 176, 32),  // kuning
            _ => Color.FromArgb(53, 201, 115),      // hijau
        };

        // outline bodi (x7..24, tinggi penuh) + nub di kanan
        using (var outline = new Pen(Color.FromArgb(120, 200, 210, 224), 1.4f))
        using (var body = RoundRect(7, 9, 14, 14, 3))
            g.DrawPath(outline, body);

        using (var nub = new SolidBrush(Color.FromArgb(120, 200, 210, 224)))
            g.FillRectangle(nub, 22, 13, 2.5f, 6);

        // isi baterai proporsional
        int fillW = (int)Math.Round(10 * percent / 100.0); // 0..10 px isi
        if (fillW > 0)
        {
            using var fill = new SolidBrush(color);
            g.FillRectangle(fill, 9, 11, fillW, 10);
        }
    }

    private static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
        var d = r * 2;
        var p = new GraphicsPath();
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public void Dispose()
    {
        if (_icon is null) return;
        _icon.Visible = false;
        _icon.Dispose();
    }
}