using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;

namespace NOVORA.UI;

public static class NLUIAndroidQr
{
    public static BitmapSource Create(string invitation)
    {
        var matrix = new ZXing.QrCode.QRCodeWriter().encode(invitation, BarcodeFormat.QR_CODE, 360, 360);
        byte[] pixels = new byte[matrix.Width * matrix.Height * 4];
        for (int y = 0; y < matrix.Height; y++)
        for (int x = 0; x < matrix.Width; x++)
        {
            int offset = (y * matrix.Width + x) * 4;
            byte value = matrix[x, y] ? (byte)0 : (byte)255;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
        var bitmap = BitmapSource.Create(matrix.Width, matrix.Height, 96, 96,
            PixelFormats.Bgra32, null, pixels, matrix.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
