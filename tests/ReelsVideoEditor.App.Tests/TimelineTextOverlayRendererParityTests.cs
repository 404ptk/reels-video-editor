using System;
using System.Runtime.InteropServices;
using ReelsVideoEditor.App.Services.Text;
using ReelsVideoEditor.App.ViewModels.Timeline;
using SkiaSharp;

namespace ReelsVideoEditor.App.Tests;

public class TimelineTextOverlayRendererParityTests
{
    [Fact]
    public void Draw_SameStateForPlaybackAndExport_ProducesIdenticalPixels()
    {
        using var playbackBitmap = CreateBlankBitmap(1080, 1920);
        using var exportBitmap = CreateBlankBitmap(1080, 1920);

        var overlayState = new TimelineTextOverlayState(
        [
            new TimelineTextOverlayLayer(
                Text: "To jest dluzszy subtitle testowy, ktory powinien zawinac sie identycznie w playback i exporcie.",
                FontFamily: "Inter",
                FontSize: 56,
                LineHeightMultiplier: 1.15,
                LetterSpacing: 1.0,
                ColorHex: "#FFFFFF",
                OutlineColorHex: "#000000",
                OutlineThickness: 4,
                TransformX: 32,
                TransformY: 140,
                TransformScale: 1,
                AnimationScale: 1,
                CropLeft: 0.08,
                CropTop: 0.05,
                CropRight: 0.08,
                CropBottom: 0.05)
        ]);

        // Playback path call shape.
        TimelineTextOverlayRenderer.Draw(
            playbackBitmap,
            overlayState,
            targetHeight: playbackBitmap.Height,
            previewFrameWidth: 720,
            previewFrameHeight: 1280);

        // Export path call shape.
        TimelineTextOverlayRenderer.Draw(
            exportBitmap,
            overlayState,
            targetHeight: 1920,
            previewFrameWidth: 720,
            previewFrameHeight: 1280);

        var playbackPixels = CopyPixels(playbackBitmap);
        var exportPixels = CopyPixels(exportBitmap);

        Assert.Equal(playbackPixels.Length, exportPixels.Length);
        Assert.Equal(playbackPixels, exportPixels);
    }

    [Fact]
    public void Draw_WhenAnimationScaleChanges_ChangesRenderedPixels()
    {
        using var baseBitmap = CreateBlankBitmap(1080, 1920);
        using var animatedBitmap = CreateBlankBitmap(1080, 1920);

        var baseState = new TimelineTextOverlayState(
        [
            new TimelineTextOverlayLayer(
                Text: "POP",
                FontFamily: "Inter",
                FontSize: 92,
                LineHeightMultiplier: 1.0,
                LetterSpacing: 0,
                ColorHex: "#FFFFFF",
                OutlineColorHex: "#000000",
                OutlineThickness: 4,
                TransformX: 0,
                TransformY: 0,
                TransformScale: 1,
                AnimationScale: 1,
                CropLeft: 0,
                CropTop: 0,
                CropRight: 0,
                CropBottom: 0)
        ]);

        var animatedState = new TimelineTextOverlayState(
        [
            new TimelineTextOverlayLayer(
                Text: "POP",
                FontFamily: "Inter",
                FontSize: 92,
                LineHeightMultiplier: 1.0,
                LetterSpacing: 0,
                ColorHex: "#FFFFFF",
                OutlineColorHex: "#000000",
                OutlineThickness: 4,
                TransformX: 0,
                TransformY: 0,
                TransformScale: 1,
                AnimationScale: 1.14,
                CropLeft: 0,
                CropTop: 0,
                CropRight: 0,
                CropBottom: 0)
        ]);

        TimelineTextOverlayRenderer.Draw(baseBitmap, baseState, baseBitmap.Height, 720, 1280);
        TimelineTextOverlayRenderer.Draw(animatedBitmap, animatedState, animatedBitmap.Height, 720, 1280);

        var basePixels = CopyPixels(baseBitmap);
        var animatedPixels = CopyPixels(animatedBitmap);

        Assert.NotEqual(basePixels, animatedPixels);
    }

    private static SKBitmap CreateBlankBitmap(int width, int height)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        return bitmap;
    }

    private static byte[] CopyPixels(SKBitmap bitmap)
    {
        var bytes = new byte[bitmap.Width * bitmap.Height * 4];
        Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);
        return bytes;
    }
}
