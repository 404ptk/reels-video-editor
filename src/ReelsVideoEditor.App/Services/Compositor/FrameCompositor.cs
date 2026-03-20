using System;
using SkiaSharp;

namespace ReelsVideoEditor.App.Services.Compositor;

public sealed class FrameCompositor : IDisposable
{
    private const float BlurSigma = 30f;
    private const float BackgroundScale = 1.3f;

    private SKBitmap? composedBitmap;
    private SKBitmap? sourceBitmap;
    private SKBitmap? tinyBitmap;
    private int lastTargetWidth;
    private int lastTargetHeight;
    private bool disposed;

    public SKBitmap ComposeFrame(byte[] sourcePixels, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        if (composedBitmap is null || lastTargetWidth != targetWidth || lastTargetHeight != targetHeight)
        {
            composedBitmap?.Dispose();
            composedBitmap = new SKBitmap(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            lastTargetWidth = targetWidth;
            lastTargetHeight = targetHeight;
        }

        if (sourceBitmap is null || sourceBitmap.Width != sourceWidth || sourceBitmap.Height != sourceHeight)
        {
            sourceBitmap?.Dispose();
            sourceBitmap = new SKBitmap(sourceWidth, sourceHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        }

        var sourceSpan = sourcePixels.AsSpan();
        var expectedSize = sourceWidth * sourceHeight * 4;
        if (sourceSpan.Length < expectedSize)
        {
            return composedBitmap;
        }

        System.Runtime.InteropServices.Marshal.Copy(sourcePixels, 0, sourceBitmap.GetPixels(), expectedSize);

        using var canvas = new SKCanvas(composedBitmap);
        canvas.Clear(SKColors.Black);

        var sourceAspect = (double)sourceWidth / sourceHeight;
        var targetAspect = (double)targetWidth / targetHeight;
        var needsBlurFill = Math.Abs(sourceAspect - targetAspect) > 0.05;

        if (needsBlurFill)
        {
            DrawBlurredBackground(canvas, sourceBitmap, targetWidth, targetHeight);
        }

        DrawCenteredForeground(canvas, sourceBitmap, sourceWidth, sourceHeight, targetWidth, targetHeight);

        canvas.Flush();
        return composedBitmap;
    }

    private void DrawBlurredBackground(SKCanvas canvas, SKBitmap source, int targetWidth, int targetHeight)
    {
        var scaleX = (float)targetWidth / source.Width * BackgroundScale;
        var scaleY = (float)targetHeight / source.Height * BackgroundScale;
        var bgScale = Math.Max(scaleX, scaleY);

        var bgWidth = source.Width * bgScale;
        var bgHeight = source.Height * bgScale;
        var bgX = (targetWidth - bgWidth) / 2f;
        var bgY = (targetHeight - bgHeight) / 2f;

        var bgRect = new SKRect(bgX, bgY, bgX + bgWidth, bgY + bgHeight);

        int tinyWidth = Math.Max(1, source.Width / 40);
        int tinyHeight = Math.Max(1, source.Height / 40);

        if (tinyBitmap is null || tinyBitmap.Width != tinyWidth || tinyBitmap.Height != tinyHeight)
        {
            tinyBitmap?.Dispose();
            tinyBitmap = new SKBitmap(tinyWidth, tinyHeight, source.ColorType, source.AlphaType);
        }

        source.ScalePixels(tinyBitmap, SKFilterQuality.None);

        using var darkenFilter = SKColorFilter.CreateBlendMode(new SKColor(0, 0, 0, 100), SKBlendMode.SrcOver);

        using var paint = new SKPaint
        {
            ColorFilter = darkenFilter,
            IsAntialias = false,
            FilterQuality = SKFilterQuality.Low
        };

        canvas.DrawBitmap(tinyBitmap, bgRect, paint);
    }

    private static void DrawCenteredForeground(SKCanvas canvas, SKBitmap source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var scaleX = (float)targetWidth / sourceWidth;
        var scaleY = (float)targetHeight / sourceHeight;
        var fgScale = Math.Min(scaleX, scaleY);

        var fgWidth = sourceWidth * fgScale;
        var fgHeight = sourceHeight * fgScale;
        var fgX = (targetWidth - fgWidth) / 2f;
        var fgY = (targetHeight - fgHeight) / 2f;

        var fgRect = new SKRect(fgX, fgY, fgX + fgWidth, fgY + fgHeight);

        using var paint = new SKPaint
        {
            IsAntialias = false,
            FilterQuality = SKFilterQuality.Low
        };

        canvas.DrawBitmap(source, fgRect, paint);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        composedBitmap?.Dispose();
        composedBitmap = null;
        sourceBitmap?.Dispose();
        sourceBitmap = null;
        tinyBitmap?.Dispose();
        tinyBitmap = null;
    }
}
