using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ReelsVideoEditor.App.Services.Compositor;
using ReelsVideoEditor.App.Services.Text;
using ReelsVideoEditor.App.Services.VideoDecoder;
using ReelsVideoEditor.App.ViewModels.Preview;
using ReelsVideoEditor.App.ViewModels.Timeline;
using SkiaSharp;

namespace ReelsVideoEditor.App.Views.Preview;

public partial class PreviewPanelView
{
    private async Task RenderSeekFrameAsync(TimeSpan position, PreviewViewModel viewModel)
    {
        if (viewModel.IsVideoHidden)
        {
            return;
        }

        var resolveLayers = viewModel.ResolveVideoLayers;
        IReadOnlyList<PreviewVideoLayer>? resolvedLayers = null;
        if (resolveLayers is not null)
        {
            var playbackMilliseconds = (long)position.TotalMilliseconds;
            resolvedLayers = resolveLayers(playbackMilliseconds);
            var hasAnySelection = viewModel.HasSelectedVideoClip?.Invoke() ?? false;
            var hasActiveTransformTarget = viewModel.HasActiveTransformTarget?.Invoke(playbackMilliseconds)
                ?? resolvedLayers.Any(layer => layer.IsSelected);
            viewModel.IsTransformTargetActive = !hasAnySelection || hasActiveTransformTarget;
            UpdateVideoForegroundBoundsForLayers(viewModel, resolvedLayers, playbackMilliseconds);
        }

        var composed = await Task.Run(() =>
        {
            if (resolvedLayers is not null)
            {
                var layeredFrame = ComposeMultipleLayers(viewModel, resolvedLayers);
                return layeredFrame ?? ComposeBlackFrame(viewModel);
            }

            if (!decoder.IsOpen)
            {
                return null;
            }

            lock (decoder)
            {
                var pixels = decoder.SeekAndRead(position);
                if (pixels is null)
                {
                    return null;
                }

                var (targetW, targetH) = GetTargetResolution(viewModel, decoder.FrameWidth, decoder.FrameHeight);
                var renderOffsetX = (float)(viewModel.TransformX * ((double)targetW / currentPreviewFrameWidth));
                var renderOffsetY = (float)(viewModel.TransformY * ((double)targetH / currentPreviewFrameHeight));

                return compositor.ComposeFrame(
                    pixels,
                    decoder.FrameWidth,
                    decoder.FrameHeight,
                    targetW,
                    targetH,
                    renderOffsetX,
                    renderOffsetY,
                    (float)viewModel.TransformScale,
                    (float)viewModel.CropLeft,
                    (float)viewModel.CropTop,
                    (float)viewModel.CropRight,
                    (float)viewModel.CropBottom,
                    viewModel.UseBlurredBackground);
            }
        });

        if (composed is not null)
        {
            ApplyTextOverlaysToBitmap(composed, viewModel, (long)position.TotalMilliseconds);
            CopyToWriteableBitmap(composed, viewModel);
        }
    }

    private void ApplyTextOverlaysToBitmap(SKBitmap bitmap, PreviewViewModel viewModel, long playbackMilliseconds)
    {
        var resolveTextOverlayState = viewModel.ResolveTextOverlayState;
        if (resolveTextOverlayState is null)
        {
            return;
        }

        var state = resolveTextOverlayState(playbackMilliseconds);
        TimelineTextOverlayRenderer.Draw(
            bitmap,
            state,
            bitmap.Height,
            Math.Max(1.0, viewModel.PreviewFrameWidth),
            Math.Max(1.0, viewModel.PreviewFrameHeight));
    }

    private SKBitmap ComposeBlackFrame(PreviewViewModel viewModel)
    {
        var sourceWidth = decoder.FrameWidth > 0 ? decoder.FrameWidth : 720;
        var sourceHeight = decoder.FrameHeight > 0 ? decoder.FrameHeight : 1280;
        var (targetW, targetH) = GetTargetResolution(viewModel, sourceWidth, sourceHeight);
        return compositor.ComposeLayers(Array.Empty<FrameCompositor.FrameLayer>(), targetW, targetH);
    }

    private SKBitmap? ComposeMultipleLayers(PreviewViewModel viewModel, IReadOnlyList<PreviewVideoLayer> layers)
    {
        var frameLayers = new List<FrameCompositor.FrameLayer>(layers.Count);
        var sourceLayers = new List<PreviewVideoLayer>(layers.Count);
        var sourceWidthForTarget = 0;
        var sourceHeightForTarget = 0;

        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            if (string.IsNullOrWhiteSpace(layer.Path))
            {
                continue;
            }

            var layerDecoder = ResolveDecoderForPath(layer.Path);
            if (layerDecoder is null)
            {
                continue;
            }

            byte[]? pixels;
            var layerPosition = TimeSpan.FromMilliseconds(Math.Max(0, layer.PlaybackMilliseconds));
            lock (layerDecoder)
            {
                if (!layerDecoder.IsOpen)
                {
                    continue;
                }

                pixels = layerDecoder.SeekAndRead(layerPosition);
                if (pixels is null)
                {
                    continue;
                }

                if (sourceWidthForTarget == 0 || sourceHeightForTarget == 0)
                {
                    sourceWidthForTarget = layerDecoder.FrameWidth;
                    sourceHeightForTarget = layerDecoder.FrameHeight;
                }

                frameLayers.Add(new FrameCompositor.FrameLayer(
                    pixels,
                    layerDecoder.FrameWidth,
                    layerDecoder.FrameHeight,
                    0f,
                    0f,
                    1f,
                    0f,
                    0f,
                    0f,
                    0f,
                    layer.DrawBlurredBackground));
                sourceLayers.Add(layer);
            }
        }

        if (frameLayers.Count == 0)
        {
            return null;
        }

        if (sourceWidthForTarget <= 0 || sourceHeightForTarget <= 0)
        {
            sourceWidthForTarget = decoder.FrameWidth > 0 ? decoder.FrameWidth : 720;
            sourceHeightForTarget = decoder.FrameHeight > 0 ? decoder.FrameHeight : 1280;
        }

        var (targetW, targetH) = GetTargetResolution(viewModel, sourceWidthForTarget, sourceHeightForTarget);
        for (var i = 0; i < frameLayers.Count; i++)
        {
            var layer = frameLayers[i];
            var sourceLayer = sourceLayers[i];
            var renderOffsetX = (float)(sourceLayer.TransformX * ((double)targetW / currentPreviewFrameWidth));
            var renderOffsetY = (float)(sourceLayer.TransformY * ((double)targetH / currentPreviewFrameHeight));
            frameLayers[i] = layer with
            {
                OffsetX = renderOffsetX,
                OffsetY = renderOffsetY,
                Scale = (float)sourceLayer.TransformScale,
                CropLeft = (float)sourceLayer.CropLeft,
                CropTop = (float)sourceLayer.CropTop,
                CropRight = (float)sourceLayer.CropRight,
                CropBottom = (float)sourceLayer.CropBottom
            };
        }

        return compositor.ComposeLayers(frameLayers, targetW, targetH);
    }

    private void UpdateVideoForegroundBoundsForLayers(PreviewViewModel viewModel, IReadOnlyList<PreviewVideoLayer> layers, long playbackMilliseconds)
    {
        if (previewFrame is null)
        {
            return;
        }

        if (UpdateTextForegroundBoundsIfNeeded(viewModel, playbackMilliseconds))
        {
            return;
        }

        if (layers.Count == 0)
        {
            viewModel.ForegroundWidth = previewFrame.Width;
            viewModel.ForegroundHeight = previewFrame.Height;
            return;
        }

        var transformLayer = layers.LastOrDefault(layer => layer.IsSelected) ?? layers[^1];
        var transformDecoder = ResolveDecoderForPath(transformLayer.Path);
        if (transformDecoder is null || !transformDecoder.IsOpen)
        {
            return;
        }

        var sourceW = transformDecoder.FrameWidth;
        var sourceH = transformDecoder.FrameHeight;
        if (sourceW <= 0 || sourceH <= 0)
        {
            return;
        }

        var targetW = previewFrame.Width;
        var targetH = previewFrame.Height;
        var scaleX = targetW / sourceW;
        var scaleY = targetH / sourceH;
        var scale = Math.Min(scaleX, scaleY);

        viewModel.ForegroundWidth = sourceW * scale;
        viewModel.ForegroundHeight = sourceH * scale;
    }

    private bool UpdateTextForegroundBoundsIfNeeded(PreviewViewModel viewModel, long playbackMilliseconds)
    {
        if (previewFrame is null || previewTextOverlay is null)
        {
            return false;
        }

        var isTextTarget = viewModel.IsTextTransformTarget?.Invoke(playbackMilliseconds) ?? false;
        if (!isTextTarget)
        {
            return false;
        }

        previewTextOverlay.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var measuredWidth = Math.Max(previewTextOverlay.Bounds.Width, previewTextOverlay.DesiredSize.Width);
        var measuredHeight = Math.Max(previewTextOverlay.Bounds.Height, previewTextOverlay.DesiredSize.Height);
        if (measuredWidth <= 1 || measuredHeight <= 1)
        {
            return false;
        }

        const double textHandlePadding = 24;
        viewModel.ForegroundWidth = Math.Clamp(measuredWidth + textHandlePadding, 24, previewFrame.Width);
        viewModel.ForegroundHeight = Math.Clamp(measuredHeight + textHandlePadding, 24, previewFrame.Height);
        return true;
    }

    private VideoFrameDecoder? ResolveDecoderForPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(loadedPath) && string.Equals(path, loadedPath, StringComparison.OrdinalIgnoreCase))
        {
            if (decoder.IsOpen)
            {
                return decoder;
            }

            try
            {
                decoder.Open(path);
                return decoder;
            }
            catch
            {
                return null;
            }
        }

        if (overlayDecoders.TryGetValue(path, out var existingDecoder))
        {
            if (existingDecoder.IsOpen)
            {
                return existingDecoder;
            }

            try
            {
                existingDecoder.Open(path);
                return existingDecoder;
            }
            catch
            {
                return null;
            }
        }

        var newDecoder = new VideoFrameDecoder();
        try
        {
            newDecoder.Open(path);
            overlayDecoders[path] = newDecoder;
            return newDecoder;
        }
        catch
        {
            newDecoder.Dispose();
            return null;
        }
    }

    private async void TriggerRecomposeAsync()
    {
        if (boundViewModel is null)
        {
            return;
        }

        pendingRecompose = true;
        if (isRecomposing)
        {
            return;
        }

        isRecomposing = true;
        try
        {
            while (pendingRecompose)
            {
                pendingRecompose = false;
                await RenderSeekFrameAsync(TimeSpan.FromMilliseconds(boundViewModel.CurrentPlaybackMilliseconds), boundViewModel);
            }
        }
        finally
        {
            isRecomposing = false;
        }
    }

    private void CopyToWriteableBitmap(SKBitmap composed, PreviewViewModel viewModel)
    {
        var width = composed.Width;
        var height = composed.Height;

        if (renderTarget is null || renderTarget.PixelSize.Width != width || renderTarget.PixelSize.Height != height)
        {
            renderTarget = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                AlphaFormat.Premul);
        }

        using (var lockedBitmap = renderTarget.Lock())
        {
            var sourcePtr = composed.GetPixels();
            var destPtr = lockedBitmap.Address;
            var byteCount = width * height * 4;

            if (tempFrameCopyBuffer is null || tempFrameCopyBuffer.Length != byteCount)
            {
                tempFrameCopyBuffer = new byte[byteCount];
            }

            Marshal.Copy(sourcePtr, tempFrameCopyBuffer, 0, byteCount);
            Marshal.Copy(tempFrameCopyBuffer, 0, destPtr, byteCount);
        }

        if (viewModel.CurrentFrame == renderTarget)
        {
            viewModel.CurrentFrame = null;
        }

        viewModel.CurrentFrame = renderTarget;

        previewImage?.InvalidateVisual();

        fpsFrameCount++;
        var currentTick = Environment.TickCount64;
        if (lastFpsTick == 0)
        {
            lastFpsTick = currentTick;
        }
        else if (currentTick - lastFpsTick >= 1000)
        {
            viewModel.Fps = fpsFrameCount;
            viewModel.FpsText = $"{fpsFrameCount} FPS";
            fpsFrameCount = 0;
            lastFpsTick = currentTick;
        }
    }

    private static (int Width, int Height) GetTargetResolution(PreviewViewModel viewModel, int sourceWidth, int sourceHeight)
    {
        return viewModel.SelectedQuality switch
        {
            PreviewQuality.High => (1080, 1920),
            PreviewQuality.Mid => (720, 1280),
            PreviewQuality.Low => (360, 640),
            _ => (720, 1280)
        };
    }
}
