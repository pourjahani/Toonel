using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;
using MessageBox = System.Windows.MessageBox;

namespace Toonel.App;

/// <summary>
/// Shows the selected profile's share link as a scannable QR code.
/// The QR is generated locally in-process; nothing is uploaded anywhere.
/// </summary>
public partial class QrWindow : Window
{
    private readonly string _link;

    public QrWindow(Toonel.Core.Models.Profile profile)
    {
        InitializeComponent();
        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(icoPath))
                Icon = BitmapFrame.Create(new Uri(icoPath));
        }
        catch { /* default icon is fine */ }

        _link = Toonel.Core.ProfileShareLink.Build(profile);
        TitleText.Text = profile.Name;
        ApplyLocalization();

        if (_link.Length == 0)
        {
            HintText.Text = Loc.T("This profile type cannot be shared as a QR code.");
            QrImage.Source = null;
            BtnCopy.IsEnabled = false;
            return;
        }

        QrImage.Source = RenderQr(_link);
    }

    private static BitmapSource RenderQr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule: 8,
            darkColorRgba: new byte[] { 17, 17, 17, 255 },   // #111111 ink
            lightColorRgba: new byte[] { 255, 255, 255, 255 });
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void ApplyLocalization()
    {
        HintText.Text = Loc.T("Scan with your phone's VPN client to import this server.");
        BtnCopy.Content = Loc.T("Copy link");
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(_link);
            ToastWindow.Show(Loc.T("Link copied to clipboard."));
        }
        catch (Exception ex)
        {
            ToastWindow.Show(Loc.F("Copy failed: {0}", ex.Message));
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
