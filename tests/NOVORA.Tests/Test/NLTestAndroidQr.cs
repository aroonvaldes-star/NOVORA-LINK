using NOVORA.Control;
using Xunit;
using ZXing;

namespace NOVORA.Tests;

public sealed class NLTestAndroidQr
{
    [Fact]
    public void DesktopQrImageDecodesToSameInvitation()
    {
        var invitation = new NLControlLanInvitation(1, "192.168.1.25", 27215,
            new string('A', 64), new string('B', 64), DateTimeOffset.UtcNow.AddSeconds(110).ToUnixTimeSeconds());
        var bitmap = NLUIAndroidQr.Create(invitation.Encode());
        byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var reader = new BarcodeReaderGeneric();
        var result = reader.Decode(new RGBLuminanceSource(pixels, bitmap.PixelWidth, bitmap.PixelHeight,
            RGBLuminanceSource.BitmapFormat.BGRA32));
        Assert.NotNull(result);
        Assert.Equal(invitation, NLControlLanInvitation.Parse(result.Text));
    }
}
