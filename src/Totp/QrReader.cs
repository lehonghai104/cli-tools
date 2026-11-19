using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace Totp;

public static class QrReader
{
    /// <summary>Decodes every QR code found in the image (PNG, JPEG, WebP, GIF, BMP...).</summary>
    public static List<string> ReadAll(string imagePath)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Image not found: {imagePath}");

        using var original = SKBitmap.Decode(imagePath)
            ?? throw new System.FormatException($"Unsupported or corrupt image: {imagePath}");

        // Draw onto an opaque white RGBA canvas: normalizes the pixel format and flattens
        // transparent backgrounds (common for QR PNGs) so they read as white, not black.
        using var bitmap = new SKBitmap(new SKImageInfo(original.Width, original.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(original, 0, 0, SKSamplingOptions.Default, null);
        }

        var source = new PlanarYUVLuminanceSource(ToLuminance(bitmap), bitmap.Width, bitmap.Height,
            0, 0, bitmap.Width, bitmap.Height, false);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = [BarcodeFormat.QR_CODE],
            },
        };

        var results = reader.DecodeMultiple(source);
        return results?.Select(r => r.Text).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList() ?? [];
    }

    /// <summary>Opaque RGBA8888 → 8-bit luminance (what ZXing binarizes).</summary>
    private static byte[] ToLuminance(SKBitmap bitmap)
    {
        var pixels = bitmap.GetPixelSpan();
        int w = bitmap.Width, h = bitmap.Height, stride = bitmap.RowBytes;
        var lum = new byte[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int p = y * stride + x * 4;
            lum[y * w + x] = (byte)((pixels[p] * 299 + pixels[p + 1] * 587 + pixels[p + 2] * 114) / 1000);
        }
        return lum;
    }
}
