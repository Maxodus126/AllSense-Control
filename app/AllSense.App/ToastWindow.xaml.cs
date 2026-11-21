using System.Windows;

namespace AllSense;

/// <summary>
/// Toast selip kanan-bawah (pengganti NotifyIcon bila TrayMode mati).
/// Muncul 4 detik lalu tutup sendiri. Tidak mengganggu fokus app lain.
/// </summary>
public partial class ToastWindow : Window
{
    public ToastWindow() => InitializeComponent();

    public string NoticeTitle
    {
        get => TitleBlock.Text;
        set => TitleBlock.Text = value;
    }

    public string NoticeBody
    {
        get => BodyBlock.Text;
        set => BodyBlock.Text = value;
    }
}