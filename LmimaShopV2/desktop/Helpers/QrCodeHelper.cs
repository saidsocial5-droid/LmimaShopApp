using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace LmimaShopV2.Desktop.Helpers;

public static class QrCodeHelper
{
    /// <summary>Renders a QR code as a frozen WPF bitmap (safe to hand to the UI thread).</summary>
    public static BitmapSource? CreateBitmap(string payload, int pixelsPerModule = 8)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);

        using var stream = new MemoryStream(png);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
