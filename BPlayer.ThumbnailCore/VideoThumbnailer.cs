using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;
using Windows.Storage;
using SkiaSharp;

namespace BPlayer.ThumbnailCore;

public class ThumbnailResult
{
    public bool Success { get; set; }
    public string? CachedPath { get; set; }
    public string? Error { get; set; }
    public bool UsedFaceDetection { get; set; }
}

public class VideoThumbnailer
{
    private const int OutputW = 340;
    private const int OutputH = 510;
    private const int SnapshotW = 640;
    private const int SnapshotH = 360;

    public string CacheDir { get; }
    public Action<string>? LogInfo { get; set; }
    public Action<string>? LogWarn { get; set; }

    public VideoThumbnailer(string cacheDir)
    {
        CacheDir = cacheDir;
        Directory.CreateDirectory(cacheDir);
    }

    public ThumbnailResult Generate(string videoPath)
    {
        if (!File.Exists(videoPath))
            return new ThumbnailResult { Error = "File not found" };

        var cacheKey = Path.GetFileNameWithoutExtension(videoPath);
        var cachePath = Path.Combine(CacheDir, cacheKey + ".jpg");

        if (File.Exists(cachePath))
        {
            try
            {
                using var cached = SKBitmap.Decode(cachePath);
                if (cached != null && cached.Width >= OutputW && cached.Height >= OutputH)
                    return new ThumbnailResult { Success = true, CachedPath = cachePath };
                File.Delete(cachePath);
            }
            catch
            {
                try { File.Delete(cachePath); } catch { }
            }
        }

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var faceResult = ScanWithFaceDetection(videoPath, cachePath);
            if (faceResult != null)
            {
                LogInfo?.Invoke($"Thumbnail OK (face) for {Path.GetFileName(videoPath)}");
                return faceResult;
            }

            var fallbackResult = CaptureFrame(videoPath, cachePath, 0.5f);
            if (fallbackResult != null)
            {
                LogInfo?.Invoke($"Thumbnail OK (fallback) for {Path.GetFileName(videoPath)}");
                return fallbackResult;
            }

            if (attempt < 3)
                LogWarn?.Invoke($"Attempt {attempt}/3 failed for {videoPath}, retrying...");
        }

        LogWarn?.Invoke($"Thumbnail FAILED after 3 attempts for {videoPath}");
        return new ThumbnailResult { Error = "Failed after 3 attempts" };
    }

    private ThumbnailResult? ScanWithFaceDetection(string videoPath, string cachePath)
    {
        IntPtr hwnd = IntPtr.Zero;
        IntPtr libvlc = IntPtr.Zero;
        IntPtr mp = IntPtr.Zero;

        try
        {
            hwnd = NativeMethods.CreateWindowEx(
                NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW,
                "static", "", NativeMethods.WS_POPUP,
                0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var pluginPath = Path.Combine(baseDir, "libvlc", "win-x64", "plugins");

            var args = new[] {
                "--no-audio", "--intf", "dummy",
                $"--plugin-path={pluginPath}",
                "--no-video-title-show", "--network-caching=200"
            };

            libvlc = NativeMethods.libvlc_new(args.Length, args);
            if (libvlc == IntPtr.Zero) return null;

            var media = NativeMethods.libvlc_media_new_path(libvlc, videoPath);
            if (media == IntPtr.Zero) return null;

            mp = NativeMethods.libvlc_media_player_new_from_media(media);
            NativeMethods.libvlc_media_release(media);
            if (mp == IntPtr.Zero) return null;

            NativeMethods.libvlc_media_player_set_hwnd(mp, hwnd);
            if (NativeMethods.libvlc_media_player_play(mp) == -1) return null;

            var started = false;
            for (int i = 0; i < 75; i++)
            {
                if (NativeMethods.libvlc_media_player_is_playing(mp) == 1)
                { started = true; break; }
                Thread.Sleep(200);
            }
            if (!started) return null;

            float[] positions = { 0.10f, 0.30f, 0.50f, 0.70f, 0.90f };

            foreach (var pos in positions)
            {
                var tempName = $"__tmp_{Path.GetFileNameWithoutExtension(cachePath)}_{pos:F2}.jpg";
                var tempPath = Path.Combine(CacheDir, tempName);

                NativeMethods.libvlc_media_player_set_position(mp, pos);
                Thread.Sleep(1500);

                var hr = NativeMethods.libvlc_video_take_snapshot(mp, 0, tempPath, SnapshotW, SnapshotH);
                Thread.Sleep(1000);

                if (hr == 0 && File.Exists(tempPath))
                {
                    var fi = new FileInfo(tempPath);
                    if (fi.Length > 500)
                    {
                        var faceBounds = GetFaceBounds(tempPath);
                        if (faceBounds.HasValue)
                        {
                            CropWithFaceAnchor(tempPath, faceBounds.Value, OutputW, OutputH);
                            try
                            {
                                if (File.Exists(cachePath)) File.Delete(cachePath);
                                File.Move(tempPath, cachePath);
                            }
                            catch { File.Copy(tempPath, cachePath, true); }
                            LogInfo?.Invoke($"Face at {pos:P0} for {Path.GetFileName(videoPath)}");
                            return new ThumbnailResult
                            {
                                Success = true,
                                CachedPath = cachePath,
                                UsedFaceDetection = true
                            };
                        }
                    }
                }

                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }

            return null;
        }
        catch (Exception ex)
        {
            LogWarn?.Invoke($"Face scan failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (mp != IntPtr.Zero)
            { NativeMethods.libvlc_media_player_stop(mp); NativeMethods.libvlc_media_player_release(mp); }
            if (libvlc != IntPtr.Zero) NativeMethods.libvlc_release(libvlc);
            if (hwnd != IntPtr.Zero) NativeMethods.DestroyWindow(hwnd);
        }
    }

    private static (int X, int Y, int Width, int Height)? GetFaceBounds(string imagePath)
    {
        try
        {
            var tcs = new TaskCompletionSource<(int, int, int, int)?>();
            var thread = new Thread(async () =>
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(imagePath);
                    using var stream = await file.OpenReadAsync();
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    using var bgraBitmap = await decoder.GetSoftwareBitmapAsync(
                        BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
                    using var grayBitmap = SoftwareBitmap.Convert(bgraBitmap, BitmapPixelFormat.Gray8);
                    var detector = await FaceDetector.CreateAsync();
                    var faces = await detector.DetectFacesAsync(grayBitmap);

                    if (faces.Count == 0) { tcs.TrySetResult(null); return; }

                    DetectedFace? largest = null;
                    double largestArea = 0;
                    foreach (var face in faces)
                    {
                        var area = face.FaceBox.Width * face.FaceBox.Height;
                        if (area > largestArea) { largestArea = area; largest = face; }
                    }

                    if (largest != null)
                    {
                        var fb = largest.FaceBox;
                        tcs.TrySetResult(((int)fb.X, (int)fb.Y, (int)fb.Width, (int)fb.Height));
                    }
                    else
                    {
                        tcs.TrySetResult(null);
                    }
                }
                catch
                {
                    tcs.TrySetResult(null);
                }
            });
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            return tcs.Task.Result;
        }
        catch
        {
            return null;
        }
    }

    private ThumbnailResult? CaptureFrame(string videoPath, string cachePath, float position)
    {
        IntPtr hwnd = IntPtr.Zero;
        IntPtr libvlc = IntPtr.Zero;
        IntPtr mp = IntPtr.Zero;

        try
        {
            hwnd = NativeMethods.CreateWindowEx(
                NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW,
                "static", "", NativeMethods.WS_POPUP,
                0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var pluginPath = Path.Combine(baseDir, "libvlc", "win-x64", "plugins");

            var args = new[] {
                "--no-audio", "--intf", "dummy",
                $"--plugin-path={pluginPath}",
                "--no-video-title-show", "--network-caching=200"
            };

            libvlc = NativeMethods.libvlc_new(args.Length, args);
            if (libvlc == IntPtr.Zero) return null;

            var media = NativeMethods.libvlc_media_new_path(libvlc, videoPath);
            if (media == IntPtr.Zero) return null;

            mp = NativeMethods.libvlc_media_player_new_from_media(media);
            NativeMethods.libvlc_media_release(media);
            if (mp == IntPtr.Zero) return null;

            NativeMethods.libvlc_media_player_set_hwnd(mp, hwnd);
            if (NativeMethods.libvlc_media_player_play(mp) == -1) return null;

            for (int i = 0; i < 75; i++)
            {
                if (NativeMethods.libvlc_media_player_is_playing(mp) == 1) break;
                Thread.Sleep(200);
            }

            NativeMethods.libvlc_media_player_set_position(mp, position);
            Thread.Sleep(2000);

            var tempPath = cachePath + ".tmp";
            var hr = NativeMethods.libvlc_video_take_snapshot(mp, 0, tempPath, SnapshotW, SnapshotH);
            Thread.Sleep(1000);
            NativeMethods.libvlc_media_player_stop(mp);

            if (hr == 0 && File.Exists(tempPath))
            {
                var fi = new FileInfo(tempPath);
                if (fi.Length > 500)
                {
                    if (File.Exists(cachePath)) File.Delete(cachePath);
                    File.Move(tempPath, cachePath);
                    CenterCropToJpeg(cachePath, OutputW, OutputH);
                    return new ThumbnailResult { Success = true, CachedPath = cachePath };
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            LogWarn?.Invoke($"CaptureFrame failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (mp != IntPtr.Zero)
            { NativeMethods.libvlc_media_player_stop(mp); NativeMethods.libvlc_media_player_release(mp); }
            if (libvlc != IntPtr.Zero) NativeMethods.libvlc_release(libvlc);
            if (hwnd != IntPtr.Zero) NativeMethods.DestroyWindow(hwnd);
        }
    }

    private static void CenterCropToJpeg(string imagePath, int targetW, int targetH)
    {
        try
        {
            using var orig = SKBitmap.Decode(imagePath);
            if (orig == null) return;

            var origW = orig.Width;
            var origH = orig.Height;
            var targetRatio = (double)targetW / targetH;
            var origRatio = (double)origW / origH;

            int cropW, cropH, cropX, cropY;
            if (origRatio > targetRatio)
            {
                cropH = origH;
                cropW = (int)(origH * targetRatio);
                cropX = (origW - cropW) / 2;
                cropY = 0;
            }
            else
            {
                cropW = origW;
                cropH = (int)(origW / targetRatio);
                cropX = 0;
                cropY = (origH - cropH) / 2;
            }

            using var cropped = new SKBitmap(targetW, targetH);
            using var canvas = new SKCanvas(cropped);
            canvas.DrawBitmap(orig,
                new SKRect(cropX, cropY, cropX + cropW, cropY + cropH),
                new SKRect(0, 0, targetW, targetH),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

            SaveJpeg(cropped, imagePath, 92);
        }
        catch { }
    }

    private static void CropWithFaceAnchor(string imagePath, (int X, int Y, int Width, int Height) faceRect, int targetW, int targetH)
    {
        try
        {
            using var orig = SKBitmap.Decode(imagePath);
            if (orig == null) return;

            var imgW = orig.Width;
            var imgH = orig.Height;
            var targetRatio = (double)targetW / targetH;

            double fcX = faceRect.X + faceRect.Width / 2.0;
            double fcY = faceRect.Y + faceRect.Height / 2.0;

            double padW = faceRect.Width * 3.0;
            double padH = faceRect.Height * 3.0;

            double cropW, cropH;
            if (padW / padH > targetRatio)
            {
                cropH = padH;
                cropW = cropH * targetRatio;
            }
            else
            {
                cropW = padW;
                cropH = cropW / targetRatio;
            }

            double minW = imgW * 0.40;
            double minH = imgH * 0.40;
            if (cropW < minW) { cropW = minW; cropH = cropW / targetRatio; }
            if (cropH < minH) { cropH = minH; cropW = cropH * targetRatio; }
            if (cropW > imgW) { cropW = imgW; cropH = cropW / targetRatio; }
            if (cropH > imgH) { cropH = imgH; cropW = cropH * targetRatio; }

            double cropX = fcX - cropW / 2.0;
            double cropY = fcY - cropH / 2.0;

            if (cropX < 0) cropX = 0;
            if (cropY < 0) cropY = 0;
            if (cropX + cropW > imgW) cropX = imgW - cropW;
            if (cropY + cropH > imgH) cropY = imgH - cropH;

            using var cropped = new SKBitmap(targetW, targetH);
            using var canvas = new SKCanvas(cropped);
            canvas.DrawBitmap(orig,
                new SKRect((float)cropX, (float)cropY, (float)(cropX + cropW), (float)(cropY + cropH)),
                new SKRect(0, 0, targetW, targetH),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

            SaveJpeg(cropped, imagePath, 92);
        }
        catch { }
    }

    private static void SaveJpeg(SKBitmap bitmap, string path, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        using var fs = File.OpenWrite(path);
        data.SaveTo(fs);
    }
}

public class PreviewFrameGenerator
{
    private const int CaptureW = 320;
    private const int CaptureH = 180;

    public static List<string> GenerateFrames(string videoPath, string outputDir)
    {
        if (!File.Exists(videoPath))
            return new List<string>();

        Directory.CreateDirectory(outputDir);

        var results = new List<string>();
        float[] positions = { 0.10f, 0.30f, 0.50f, 0.70f, 0.90f };

        IntPtr hwnd = IntPtr.Zero;
        IntPtr libvlc = IntPtr.Zero;
        IntPtr mp = IntPtr.Zero;

        try
        {
            hwnd = NativeMethods.CreateWindowEx(
                NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW,
                "static", "", NativeMethods.WS_POPUP,
                0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var pluginPath = Path.Combine(baseDir, "libvlc", "win-x64", "plugins");

            var args = new[] {
                "--no-audio", "--intf", "dummy",
                $"--plugin-path={pluginPath}",
                "--no-video-title-show", "--network-caching=200"
            };

            libvlc = NativeMethods.libvlc_new(args.Length, args);
            if (libvlc == IntPtr.Zero) return results;

            var media = NativeMethods.libvlc_media_new_path(libvlc, videoPath);
            if (media == IntPtr.Zero) return results;

            mp = NativeMethods.libvlc_media_player_new_from_media(media);
            NativeMethods.libvlc_media_release(media);
            if (mp == IntPtr.Zero) return results;

            NativeMethods.libvlc_media_player_set_hwnd(mp, hwnd);
            if (NativeMethods.libvlc_media_player_play(mp) == -1) return results;

            var started = false;
            for (int i = 0; i < 75; i++)
            {
                if (NativeMethods.libvlc_media_player_is_playing(mp) == 1)
                { started = true; break; }
                Thread.Sleep(200);
            }
            if (!started) return results;

            for (int i = 0; i < positions.Length; i++)
            {
                NativeMethods.libvlc_media_player_set_position(mp, positions[i]);
                Thread.Sleep(1500);

                var tempPath = Path.Combine(outputDir, $"__tmp_{i}.jpg");
                var hr = NativeMethods.libvlc_video_take_snapshot(mp, 0, tempPath, CaptureW, CaptureH);
                Thread.Sleep(1000);

                if (hr == 0 && File.Exists(tempPath))
                {
                    var fi = new FileInfo(tempPath);
                    if (fi.Length > 500)
                    {
                        var finalPath = Path.Combine(outputDir, $"preview_{i + 1}.jpg");
                        try
                        {
                            if (File.Exists(finalPath)) File.Delete(finalPath);
                            File.Move(tempPath, finalPath);
                        }
                        catch
                        {
                            File.Copy(tempPath, finalPath, true);
                            try { File.Delete(tempPath); } catch { }
                        }
                        results.Add(finalPath);
                    }
                    else
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }
            }

            return results;
        }
        catch
        {
            return results;
        }
        finally
        {
            if (mp != IntPtr.Zero)
            {
                NativeMethods.libvlc_media_player_stop(mp);
                NativeMethods.libvlc_media_player_release(mp);
            }
            if (libvlc != IntPtr.Zero)
                NativeMethods.libvlc_release(libvlc);
            if (hwnd != IntPtr.Zero)
                NativeMethods.DestroyWindow(hwnd);
        }
    }
}
