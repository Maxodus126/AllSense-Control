using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AllSense;

/// <summary>
/// Ikon system tray (System.Windows.Forms.NotifyIcon):
/// klik-kiri togel window, klik-kanan menu (Tampilkan, Ganti Mode, Keluar).
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
            Icon = CreateIcon(),
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

    /// <summary>Bubble notifikasi (untuk peringatan baterai rendah).</summary>
    public void Balloon(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.Warning);

    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // latar: persegi bulat gelap
            using var bg = new SolidBrush(Color.FromArgb(16, 18, 26));
            using var bgPath = RoundRect(1, 1, 30, 30, 8);
            g.FillPath(bg, bgPath);

            // petir: warna aksen
            using var bolt = new SolidBrush(Color.FromArgb(255, 82, 72));
            Point[] pts =
            {
                new(18, 3), new(8, 18), new(15, 18),
                new(14, 29), new(24, 14), new(17, 14),
            };
            g.FillPolygon(bolt, pts);
        }
        // GetHicon mengalokasikan HICON GDI yang tidak pernah dilepas ikon hasil
        // FromHandle. Salin HICON ke managed Icon lalu hancurkan handle aslinya
        // (kalau tidak: kebocoran GDI, satu handle tiap start/toggle tray).
        var hicon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hicon);
            return (Icon)tmp.Clone();
        }
        finally { DestroyIcon(hicon); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

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

    public void Dispose()
    {
        if (_icon is null) return;
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}