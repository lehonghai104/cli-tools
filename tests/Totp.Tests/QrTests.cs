using SkiaSharp;
using Totp;
using ZXing;
using ZXing.QrCode;

namespace Totp.Tests;

public class QrTests
{
    internal static string WriteQrImage(string text, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions { Width = 300, Height = 300, Margin = 2 },
        };
        var pixels = writer.Write(text); // BGRA32
        var info = new SKImageInfo(pixels.Width, pixels.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        System.Runtime.InteropServices.Marshal.Copy(pixels.Pixels, 0, bitmap.GetPixels(), pixels.Pixels.Length);

        var path = Path.Combine(Path.GetTempPath(), $"totp-test-{Guid.NewGuid():N}.{format.ToString().ToLowerInvariant()}");
        using var data = bitmap.Encode(format, 95);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    public void ReadsOtpAuthQrFromImage(SKEncodedImageFormat format)
    {
        const string uri = "otpauth://totp/GitHub:alice%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub";
        var path = WriteQrImage(uri, format);
        try
        {
            Assert.Equal([uri], QrReader.ReadAll(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ImageWithoutQrReturnsEmpty()
    {
        using var bitmap = new SKBitmap(100, 100);
        bitmap.Erase(SKColors.White);
        var path = Path.Combine(Path.GetTempPath(), $"totp-test-{Guid.NewGuid():N}.png");
        using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(path, data.ToArray());
        try { Assert.Empty(QrReader.ReadAll(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ParsesOtpAuthUri()
    {
        var acc = Assert.Single(OtpAuthParser.Parse(
            "otpauth://totp/ACME%20Co:john.doe@email.com?secret=hxdmvjecjjwsrb3hwizr6ifugfmxuoou&issuer=ACME%20Co&algorithm=SHA256&digits=8&period=60"));
        Assert.Equal("ACME Co", acc.Issuer);
        Assert.Equal("john.doe@email.com", acc.Account);
        Assert.Equal("HXDMVJECJJWSRB3HWIZR6IFUGFMXUOOU", acc.Secret);
        Assert.Equal((8, 60, "SHA256"), (acc.Digits, acc.Period, acc.Algorithm));
    }

    [Fact]
    public void LabelIssuerUsedWhenNoIssuerParam()
    {
        var acc = Assert.Single(OtpAuthParser.Parse("otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP"));
        Assert.Equal(("Example", "alice"), (acc.Issuer, acc.Account));
        Assert.Equal((6, 30, "SHA1"), (acc.Digits, acc.Period, acc.Algorithm));
    }

    [Theory]
    [InlineData("otpauth://hotp/X?secret=JBSWY3DPEHPK3PXP&counter=0")]
    [InlineData("otpauth://totp/X?issuer=X")]
    [InlineData("https://example.com")]
    public void RejectsUnsupported(string text) =>
        Assert.Throws<System.FormatException>(() => OtpAuthParser.Parse(text));

    [Fact]
    public void ParsesGoogleAuthenticatorMigration()
    {
        byte[] secret1 = "12345678901234567890"u8.ToArray();
        byte[] secret2 = [0xde, 0xad, 0xbe, 0xef, 0x01];
        var payload = Concat(
            Field(1, Concat(Field(1, secret1), Field(2, "GitHub:alice"u8.ToArray()), Field(3, "GitHub"u8.ToArray()), Varint(4, 1), Varint(5, 1), Varint(6, 2))),
            Field(1, Concat(Field(1, secret2), Field(2, "bob@x.com"u8.ToArray()), Varint(4, 2), Varint(5, 2), Varint(6, 2))),
            Field(1, Concat(Field(1, secret2), Field(2, "hotp"u8.ToArray()), Varint(6, 1))), // HOTP: skipped
            Varint(2, 1));
        var uri = "otpauth-migration://offline?data=" + Uri.EscapeDataString(Convert.ToBase64String(payload));

        var accounts = OtpAuthParser.Parse(uri);
        Assert.Equal(2, accounts.Count);
        Assert.Equal(new ParsedAccount("GitHub", "alice", "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", 6, 30, "SHA1"), accounts[0]);
        Assert.Equal(new ParsedAccount(null, "bob@x.com", Base32.Encode(secret2), 8, 30, "SHA256"), accounts[1]);
        Assert.Equal(secret2, Base32.Decode(accounts[1].Secret));
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static byte[] Field(int n, byte[] value) => [(byte)(n << 3 | 2), (byte)value.Length, .. value];
    private static byte[] Varint(int n, byte value) => [(byte)(n << 3), value];
}
