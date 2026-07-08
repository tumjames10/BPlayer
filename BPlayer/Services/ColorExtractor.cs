using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BPlayer.Models;

namespace BPlayer.Services;

public static class ColorExtractor
{
    public static Palette ExtractFromImage(string? imagePath, Palette fallback)
    {
        if (string.IsNullOrEmpty(imagePath)) return fallback;

        try
        {
            byte[] bytes;
            if (imagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                bytes = client.GetByteArrayAsync(imagePath).GetAwaiter().GetResult();
            }
            else
            {
                if (!File.Exists(imagePath)) return fallback;
                bytes = File.ReadAllBytes(imagePath);
            }

            using var ms = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var h = (int)(frame.PixelHeight * (32.0 / frame.PixelWidth));
            if (h < 1) h = 1;

            var small = new TransformedBitmap(frame, new ScaleTransform(32.0 / frame.PixelWidth, (double)h / frame.PixelHeight));
            var pixels = new byte[small.PixelWidth * small.PixelHeight * 4];
            small.CopyPixels(pixels, small.PixelWidth * 4, 0);

            long rTotal = 0, gTotal = 0, bTotal = 0;
            var count = 0;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                var b = pixels[i];
                var g = pixels[i + 1];
                var r = pixels[i + 2];
                var brightness = r * 0.299 + g * 0.587 + b * 0.114;
                if (brightness > 20 && brightness < 240)
                {
                    rTotal += r; gTotal += g; bTotal += b;
                    count++;
                }
            }

            if (count == 0) return fallback;

            var avgR = (byte)(rTotal / count);
            var avgG = (byte)(gTotal / count);
            var avgB = (byte)(bTotal / count);

            var bg = Color.FromRgb(avgR, avgG, avgB);

            var accentR = (byte)System.Math.Clamp(avgR + 60, 0, 255);
            var accentG = (byte)System.Math.Clamp(avgG + 60, 0, 255);
            var accentB = (byte)System.Math.Clamp(avgB + 60, 0, 255);
            var accent = Color.FromRgb(accentR, accentG, accentB);

            var darkR = (byte)(avgR / 4);
            var darkG = (byte)(avgG / 4);
            var darkB = (byte)(avgB / 4);
            var dark = Color.FromRgb(darkR, darkG, darkB);

            return new Palette { Background = bg, Accent = accent, Dark = dark };
        }
        catch (System.Exception ex)
        {
            Logger.Warn($"Color extraction failed: {ex.Message}");
            return fallback;
        }
    }

    public static Brush CreateGradientBrush(Palette palette)
    {
        var brush = new LinearGradientBrush(
            palette.Dark, palette.Background,
            new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
        brush.Freeze();
        return brush;
    }

    public static Brush CreateGlassBrush()
    {
        var color = Color.FromArgb(40, 255, 255, 255);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
