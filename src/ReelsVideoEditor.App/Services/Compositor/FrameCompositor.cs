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
    private SKBitmap? blurredTinyBitmap;
    private SKCanvas? tinyCanvas;
    private int lastTargetWidth;
    private int lastTargetHeight;
    private bool disposed;

    public SKBitmap ComposeFrame(byte[] sourcePixels, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, float offsetX = 0f, float offsetY = 0f, float scale = 1f)
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

        DrawCenteredForeground(canvas, sourceBitmap, sourceWidth, sourceHeight, targetWidth, targetHeight, offsetX, offsetY, scale);

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

        // Downscale to a tiny resolution (1/20) for lightning fast blur
        int tinyWidth = Math.Max(1, source.Width / 20);
        int tinyHeight = Math.Max(1, source.Height / 20);

        if (tinyBitmap is null || blurredTinyBitmap is null || tinyCanvas is null || tinyBitmap.Width != tinyWidth || tinyBitmap.Height != tinyHeight)
        {
            tinyBitmap?.Dispose();
            tinyBitmap = new SKBitmap(tinyWidth, tinyHeight, source.ColorType, source.AlphaType);
            
            blurredTinyBitmap?.Dispose();
            blurredTinyBitmap = new SKBitmap(tinyWidth, tinyHeight, source.ColorType, source.AlphaType);
            
            tinyCanvas?.Dispose();
            tinyCanvas = new SKCanvas(blurredTinyBitmap);
        }

        // Fast bilinear downscale
        source.ScalePixels(tinyBitmap, SKFilterQuality.Low);

        tinyCanvas!.Clear(SKColors.Black);
        
        using var darkenFilter = SKColorFilter.CreateBlendMode(new SKColor(0, 0, 0, 100), SKBlendMode.SrcOver);
        // Apply blur on the tiny bitmap, making it instantaneous
        using var blurFilter = SKImageFilter.CreateBlur(BlurSigma / 15f, BlurSigma / 15f);
        using var combinedFilter = SKImageFilter.CreateColorFilter(darkenFilter, blurFilter);

        using var tinyPaint = new SKPaint
        {
            ImageFilter = combinedFilter,
            IsAntialias = false
        };
        
        tinyCanvas.DrawBitmap(tinyBitmap, 0, 0, tinyPaint);
        tinyCanvas.Flush();

        using var mainPaint = new SKPaint
        {
            IsAntialias = false,
            // Bilinear upscale of the already-blurred tiny image gives a perfect smooth gradient
            FilterQuality = SKFilterQuality.Low 
        };

        canvas.DrawBitmap(blurredTinyBitmap!, bgRect, mainPaint);
    }

    private static void DrawCenteredForeground(SKCanvas canvas, SKBitmap source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, float offsetX, float offsetY, float scale)
    {
        var scaleX = (float)targetWidth / sourceWidth;
        var scaleY = (float)targetHeight / sourceHeight;
        var baseFgScale = Math.Min(scaleX, scaleY);
        var fgScale = baseFgScale * scale;

        var fgWidth = sourceWidth * fgScale;
        var fgHeight = sourceHeight * fgScale;
        var fgX = (targetWidth - fgWidth) / 2f + offsetX;
        var fgY = (targetHeight - fgHeight) / 2f + offsetY;

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
        if (disposed) return;
        
        disposed = true;
        composedBitmap?.Dispose();
        sourceBitmap?.Dispose();
        tinyBitmap?.Dispose();
        blurredTinyBitmap?.Dispose();
        tinyCanvas?.Dispose();
        
        composedBitmap = null;
        sourceBitmap = null;
        tinyBitmap = null;
        blurredTinyBitmap = null;
        tinyCanvas = null;
    }
}
