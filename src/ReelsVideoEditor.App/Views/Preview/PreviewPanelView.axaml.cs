using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ReelsVideoEditor.App.Services.AudioPlayback;
using ReelsVideoEditor.App.Services.Compositor;
using ReelsVideoEditor.App.Services.VideoDecoder;
using ReelsVideoEditor.App.ViewModels.Preview;
using SkiaSharp;

namespace ReelsVideoEditor.App.Views.Preview;

public partial class PreviewPanelView : UserControl
{
    private const double PreviewAspectRatio = 9.0 / 16.0;
    private const double PreviewPadding = 8;

    private readonly VideoFrameDecoder decoder = new();
    private readonly AudioPlaybackService audioService = new();
    private readonly FrameCompositor compositor = new();
    private readonly Border? previewFrame;
    private readonly Control? previewViewport;
    private readonly Image? previewImage;

    private PreviewViewModel? boundViewModel;
    private string? loadedPath;
    private int handledStopRequestVersion;
    private int handledSeekRequestVersion;
    private readonly Stopwatch playbackStopwatch = new();
    private long playbackStartMilliseconds;
    private WriteableBitmap? renderTarget;
    private bool isSeeking;
    private TimeSpan? pendingSeekPosition;
    private bool isRecomposing;
    private bool pendingRecompose;
    private int fpsFrameCount;
    private long lastFpsTick;
    private byte[]? tempFrameCopyBuffer;
    private CancellationTokenSource? playbackCts;

    private double currentZoom = 1.0;
    private double panX = 0.0;
    private double panY = 0.0;
    private bool isPanning;
    private Point lastPanPosition;
    
    private bool isScaling;
    private double scaleStartDistance;
    private double scaleStartValue;
    private Point dragCenter;
    private bool isCropping;
    private CropHandle activeCropHandle = CropHandle.None;

    private const double MinCropVisibleNormalized = 0.05;
    
    private double currentPreviewFrameWidth = 64;
    private double currentPreviewFrameHeight = 112;

    private enum CropHandle
    {
        None,
        TopLeft,
        Top,
        TopRight,
        Left,
        Right,
        BottomLeft,
        Bottom,
        BottomRight
    }

    public PreviewPanelView()
    {
        InitializeComponent();

        VideoFrameDecoder.InitializeFFmpeg();

        var previewCanvas = this.FindControl<PreviewCanvasView>("PreviewCanvas");
        previewFrame = previewCanvas?.FindControl<Border>("PreviewFrame");
        previewViewport = previewCanvas?.FindControl<Control>("PreviewViewport");
        previewImage = previewCanvas?.FindControl<Image>("PreviewImage");

        if (previewViewport is not null)
        {
            previewViewport.SizeChanged += (_, _) => UpdatePreviewFrameSize();
            previewViewport.PointerWheelChanged += OnPreviewPointerWheelChanged;
            previewViewport.PointerPressed += OnPreviewPointerPressed;
            previewViewport.PointerMoved += OnPreviewPointerMoved;
            previewViewport.PointerReleased += OnPreviewPointerReleased;
            previewViewport.PointerCaptureLost += OnPreviewPointerCaptureLost;
        }

        Loaded += (_, _) => UpdatePreviewFrameSize();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => DisposeResources();
    }

    private void OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        if (boundViewModel is not null)
        {
            boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        boundViewModel = DataContext as PreviewViewModel;
        if (boundViewModel is not null)
        {
            boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyVideoSource(boundViewModel);
            ApplyAudioState(boundViewModel);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (boundViewModel is null)
        {
            return;
        }

        switch (eventArgs.PropertyName)
        {
            case nameof(PreviewViewModel.IsPlaying):
            case nameof(PreviewViewModel.SourceVideoPath):
            case nameof(PreviewViewModel.StopRequestVersion):
            case nameof(PreviewViewModel.SeekRequestVersion):
                ApplyPlaybackState(boundViewModel);
                break;
            case nameof(PreviewViewModel.IsAudioMuted):
            case nameof(PreviewViewModel.CurrentAudioVolume):
                ApplyAudioState(boundViewModel);
                break;
            case nameof(PreviewViewModel.SelectedQuality):
            case nameof(PreviewViewModel.TransformX):
            case nameof(PreviewViewModel.TransformY):
            case nameof(PreviewViewModel.TransformScale):
            case nameof(PreviewViewModel.CropLeft):
            case nameof(PreviewViewModel.CropTop):
            case nameof(PreviewViewModel.CropRight):
            case nameof(PreviewViewModel.CropBottom):
                if (!boundViewModel.IsPlaying)
                {
                    TriggerRecomposeAsync();
                }
                break;
            case nameof(PreviewViewModel.IsTransformModeEnabled):
                ConstrainPan();
                ApplyTransform();
                break;
        }
    }

    private void ApplyVideoSource(PreviewViewModel viewModel)
    {
        var path = viewModel.SourceVideoPath;
        if (!string.IsNullOrWhiteSpace(path) && !string.Equals(path, loadedPath, StringComparison.OrdinalIgnoreCase))
        {
            LoadMedia(path);
        }
    }

    private void ApplyPlaybackState(PreviewViewModel viewModel)
    {
        ApplyVideoSource(viewModel);
        ApplySeekRequest(viewModel);

        if (viewModel.IsPlaying)
        {
            if (decoder.IsOpen)
            {
                playbackStartMilliseconds = viewModel.CurrentPlaybackMilliseconds;
                playbackStopwatch.Restart();
                audioService.Seek(TimeSpan.FromMilliseconds(playbackStartMilliseconds));
                audioService.Play();
                StartPlaybackLoop(viewModel);
            }

            return;
        }

        playbackCts?.Cancel();
        if (viewModel.StopRequestVersion > handledStopRequestVersion && decoder.IsOpen)
        {
            handledStopRequestVersion = viewModel.StopRequestVersion;
            audioService.Stop();
            playbackStopwatch.Stop();
            viewModel.UpdatePlaybackTime(0);
            _ = RenderSeekFrameAsync(TimeSpan.Zero, viewModel);
            return;
        }

        audioService.Pause();
        playbackStopwatch.Stop();
    }

    private async void ApplySeekRequest(PreviewViewModel viewModel)
    {
        if (viewModel.SeekRequestVersion <= handledSeekRequestVersion)
        {
            return;
        }

        if (!decoder.IsOpen)
        {
            return;
        }

        handledSeekRequestVersion = viewModel.SeekRequestVersion;

        var targetMilliseconds = Math.Max(0, viewModel.RequestedSeekMilliseconds);
        var totalLength = (long)decoder.Duration.TotalMilliseconds;
        if (totalLength > 0)
        {
            targetMilliseconds = Math.Min(targetMilliseconds, totalLength);
        }

        var targetTime = TimeSpan.FromMilliseconds(targetMilliseconds);
        audioService.Seek(targetTime);
        viewModel.UpdatePlaybackTime(targetMilliseconds);

        playbackStartMilliseconds = targetMilliseconds;
        if (viewModel.IsPlaying)
        {
            playbackStopwatch.Restart();
        }
        else
        {
            playbackStopwatch.Reset();
            audioService.Pause();
        }

        pendingSeekPosition = targetTime;
        if (isSeeking) return;

        isSeeking = true;
        try
        {
            while (pendingSeekPosition.HasValue)
            {
                var timeToSeek = pendingSeekPosition.Value;
                pendingSeekPosition = null;
                await RenderSeekFrameAsync(timeToSeek, viewModel);
            }
        }
        finally
        {
            isSeeking = false;
        }
    }

    private void ApplyAudioState(PreviewViewModel viewModel)
    {
        audioService.Volume = (float)Math.Clamp(viewModel.CurrentAudioVolume, 0.0, 1.0);
        audioService.IsMuted = viewModel.IsAudioMuted;
    }

    private void LoadMedia(string path)
    {
        audioService.Stop();

        try
        {
            decoder.Open(path);
            audioService.Open(path);
        }
        catch
        {
            decoder.Close();
            audioService.Close();
            loadedPath = null;
            return;
        }

        loadedPath = path;
        playbackStartMilliseconds = 0;
        playbackStopwatch.Reset();

        if (boundViewModel is not null)
        {
            UpdateVideoForegroundBounds();
            var totalMs = (long)decoder.Duration.TotalMilliseconds;
            boundViewModel.UpdatePlaybackTime(0);
            boundViewModel.UpdateTotalPlaybackTime(totalMs);
            _ = RenderSeekFrameAsync(TimeSpan.Zero, boundViewModel);
        }
    }

    private void StartPlaybackLoop(PreviewViewModel viewModel)
    {
        playbackCts?.Cancel();
        playbackCts = new CancellationTokenSource();
        var token = playbackCts.Token;

        Task.Run(async () =>
        {
            // 16.666 ms interval for 60 FPS targeting without UI thread quantization!
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16.666));

            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
            {
                var elapsedMs = playbackStopwatch.ElapsedMilliseconds;
                var currentMs = playbackStartMilliseconds + elapsedMs;
                var totalMs = (long)decoder.Duration.TotalMilliseconds;

                if (totalMs > 0 && currentMs >= totalMs)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        viewModel.UpdatePlaybackTime((long)totalMs);
                        viewModel.IsPlaying = false;
                        audioService.Stop();
                    });
                    break;
                }

                var currentTime = TimeSpan.FromMilliseconds(currentMs);

                SKBitmap? composed = null;
                lock (decoder)
                {
                    if (!decoder.IsOpen) break;

                    var pixels = decoder.ReadNextFrame(currentTime);
                    if (pixels != null)
                    {
                        var (targetW, targetH) = GetTargetResolution(viewModel, decoder.FrameWidth, decoder.FrameHeight);
                        var renderOffsetX = (float)(viewModel.TransformX * ((double)targetW / currentPreviewFrameWidth));
                        var renderOffsetY = (float)(viewModel.TransformY * ((double)targetH / currentPreviewFrameHeight));
                        
                        // Background buffer reuse applies here! 0 unmanaged allocations.
                        composed = compositor.ComposeFrame(
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
                            (float)viewModel.CropBottom);
                    }
                }

                if (composed != null && !token.IsCancellationRequested)
                {
                    // Await the push to the UI thread to prevent mutating 'composed' 
                    // in the background before the UI copies it.
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            CopyToWriteableBitmap(composed, viewModel);
                            viewModel.UpdatePlaybackTime((long)currentMs);
                            viewModel.UpdateTotalPlaybackTime(totalMs);
                        }
                    }, DispatcherPriority.Render);
                }
            }
        }, token);
    }

    private async Task RenderSeekFrameAsync(TimeSpan position, PreviewViewModel viewModel)
    {
        if (!decoder.IsOpen || viewModel.IsVideoHidden)
            return;

        var composed = await Task.Run(() =>
        {
            lock (decoder)
            {
                var pixels = decoder.SeekAndRead(position);
                if (pixels is null) return null;

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
                    (float)viewModel.CropBottom);
            }
        });

        if (composed is not null)
        {
            CopyToWriteableBitmap(composed, viewModel);
        }
    }

    private async void TriggerRecomposeAsync()
    {
        if (boundViewModel is null) return;
        
        pendingRecompose = true;
        if (isRecomposing) return;

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

            System.Runtime.InteropServices.Marshal.Copy(sourcePtr, tempFrameCopyBuffer, 0, byteCount);
            System.Runtime.InteropServices.Marshal.Copy(tempFrameCopyBuffer, 0, destPtr, byteCount);
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

    private void DisposeResources()
    {
        if (boundViewModel is not null)
        {
            boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        playbackCts?.Cancel();
        decoder.Dispose();
        audioService.Dispose();
        compositor.Dispose();
        renderTarget = null;
    }

    private void OnPreviewPointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (previewFrame is null || previewViewport is null || boundViewModel is null) return;

        var zoomDelta = e.Delta.Y > 0 ? 0.15 : -0.15;
        var newZoom = Math.Clamp(currentZoom + zoomDelta, 1.0, 5.0);

        if (Math.Abs(newZoom - currentZoom) < 0.001) return;

        var mousePos = e.GetPosition(previewViewport);
        var centerX = previewViewport.Bounds.Width / 2;
        var centerY = previewViewport.Bounds.Height / 2;

        var relativeX = mousePos.X - centerX;
        var relativeY = mousePos.Y - centerY;

        var zoomRatio = newZoom / currentZoom;
        panX = relativeX - (relativeX - panX) * zoomRatio;
        panY = relativeY - (relativeY - panY) * zoomRatio;

        currentZoom = newZoom;
        
        if (boundViewModel is not null)
        {
            boundViewModel.CurrentZoom = newZoom;
            boundViewModel.ZoomText = $"Zoom: {Math.Round(currentZoom * 100)}%";
        }

        ConstrainPan();
        ApplyTransform();
        e.Handled = true;
    }

    private void OnPreviewPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (previewViewport is null || boundViewModel is null) return;

        var pointer = e.GetCurrentPoint(previewViewport);

        if (boundViewModel.IsTransformModeEnabled && e.Source is Avalonia.Controls.Shapes.Ellipse ellipse && ellipse.Classes.Contains("TransformHandle"))
        {
            if (pointer.Properties.IsLeftButtonPressed)
            {
                isScaling = true;
                scaleStartValue = boundViewModel.TransformScale;
                var vpCenter = new Point(previewViewport.Bounds.Width / 2, previewViewport.Bounds.Height / 2);
                dragCenter = new Point(vpCenter.X + boundViewModel.TransformX, vpCenter.Y + boundViewModel.TransformY);
                scaleStartDistance = Math.Sqrt(Math.Pow(pointer.Position.X - dragCenter.X, 2) + Math.Pow(pointer.Position.Y - dragCenter.Y, 2));
                e.Handled = true;
                return;
            }
        }

        if (boundViewModel.IsClipperModeEnabled && e.Source is Avalonia.Controls.Shapes.Ellipse cropEllipse && cropEllipse.Classes.Contains("ClipperHandle"))
        {
            if (pointer.Properties.IsLeftButtonPressed)
            {
                activeCropHandle = ParseCropHandle(cropEllipse.Tag as string);
                if (activeCropHandle != CropHandle.None)
                {
                    isCropping = true;
                    e.Handled = true;
                    return;
                }
            }
        }

        bool canPanZoom = currentZoom > 1.0 && !boundViewModel.IsTransformModeEnabled && !boundViewModel.IsClipperModeEnabled;
        bool canPanTransform = boundViewModel.IsTransformModeEnabled;

        if (!canPanZoom && !canPanTransform) return;

        if (pointer.Properties.IsLeftButtonPressed || pointer.Properties.IsMiddleButtonPressed)
        {
            isPanning = true;
            lastPanPosition = pointer.Position;
            e.Handled = true;
        }
    }

    private void OnPreviewPointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (previewViewport is null || boundViewModel is null) return;

        var pointer = e.GetCurrentPoint(previewViewport);

        if (isScaling)
        {
            var currentDistance = Math.Sqrt(Math.Pow(pointer.Position.X - dragCenter.X, 2) + Math.Pow(pointer.Position.Y - dragCenter.Y, 2));
            if (scaleStartDistance > 0)
            {
                var newScale = scaleStartValue * (currentDistance / scaleStartDistance);
                boundViewModel.TransformScale = Math.Max(0.1, newScale);
            }
            e.Handled = true;
            return;
        }

        if (isCropping)
        {
            ApplyCropDrag(pointer.Position);
            e.Handled = true;
            return;
        }

        if (!isPanning) return;

        var deltaX = pointer.Position.X - lastPanPosition.X;
        var deltaY = pointer.Position.Y - lastPanPosition.Y;

        lastPanPosition = pointer.Position;

        if (boundViewModel.IsTransformModeEnabled)
        {
            boundViewModel.TransformX += deltaX;
            boundViewModel.TransformY += deltaY;
            e.Handled = true;
            return;
        }

        panX += deltaX;
        panY += deltaY;

        ConstrainPan();
        ApplyTransform();
        e.Handled = true;
    }

    private void OnPreviewPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        isPanning = false;
        isScaling = false;
        isCropping = false;
        activeCropHandle = CropHandle.None;
    }

    private void OnPreviewPointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        isPanning = false;
        isScaling = false;
        isCropping = false;
        activeCropHandle = CropHandle.None;
    }

    private void ApplyCropDrag(Point pointerPosition)
    {
        if (boundViewModel is null || activeCropHandle == CropHandle.None)
        {
            return;
        }

        var fgWidth = boundViewModel.ScaledForegroundWidth;
        var fgHeight = boundViewModel.ScaledForegroundHeight;
        if (fgWidth <= 1 || fgHeight <= 1)
        {
            return;
        }

        var centerX = (previewViewport?.Bounds.Width ?? 0) / 2.0;
        var centerY = (previewViewport?.Bounds.Height ?? 0) / 2.0;
        var fgLeft = centerX + boundViewModel.TransformX - (fgWidth / 2.0);
        var fgTop = centerY + boundViewModel.TransformY - (fgHeight / 2.0);

        var xNorm = (pointerPosition.X - fgLeft) / fgWidth;
        var yNorm = (pointerPosition.Y - fgTop) / fgHeight;
        xNorm = Math.Clamp(xNorm, 0.0, 1.0);
        yNorm = Math.Clamp(yNorm, 0.0, 1.0);

        var maxLeft = Math.Max(0.0, 1.0 - boundViewModel.CropRight - MinCropVisibleNormalized);
        var maxTop = Math.Max(0.0, 1.0 - boundViewModel.CropBottom - MinCropVisibleNormalized);
        var maxRight = Math.Max(0.0, 1.0 - boundViewModel.CropLeft - MinCropVisibleNormalized);
        var maxBottom = Math.Max(0.0, 1.0 - boundViewModel.CropTop - MinCropVisibleNormalized);

        if (activeCropHandle is CropHandle.TopLeft or CropHandle.Left or CropHandle.BottomLeft)
        {
            boundViewModel.CropLeft = Math.Clamp(xNorm, 0.0, maxLeft);
        }

        if (activeCropHandle is CropHandle.TopRight or CropHandle.Right or CropHandle.BottomRight)
        {
            var right = 1.0 - xNorm;
            boundViewModel.CropRight = Math.Clamp(right, 0.0, maxRight);
        }

        if (activeCropHandle is CropHandle.TopLeft or CropHandle.Top or CropHandle.TopRight)
        {
            boundViewModel.CropTop = Math.Clamp(yNorm, 0.0, maxTop);
        }

        if (activeCropHandle is CropHandle.BottomLeft or CropHandle.Bottom or CropHandle.BottomRight)
        {
            var bottom = 1.0 - yNorm;
            boundViewModel.CropBottom = Math.Clamp(bottom, 0.0, maxBottom);
        }
    }

    private static CropHandle ParseCropHandle(string? tag) =>
        tag switch
        {
            "TopLeft" => CropHandle.TopLeft,
            "Top" => CropHandle.Top,
            "TopRight" => CropHandle.TopRight,
            "Left" => CropHandle.Left,
            "Right" => CropHandle.Right,
            "BottomLeft" => CropHandle.BottomLeft,
            "Bottom" => CropHandle.Bottom,
            "BottomRight" => CropHandle.BottomRight,
            _ => CropHandle.None
        };

    private (int Width, int Height) GetTargetResolution(PreviewViewModel viewModel, int sourceWidth, int sourceHeight)
    {
        return viewModel.SelectedQuality switch
        {
            PreviewQuality.High => (1080, 1920),
            PreviewQuality.Mid => (720, 1280),
            PreviewQuality.Low => (360, 640),
            _ => (720, 1280)
        };
    }

    private void ConstrainPan()
    {
        if (previewFrame is null) return;

        if (boundViewModel?.IsTransformModeEnabled == true)
        {
            return;
        }

        if (currentZoom <= 1.0)
        {
            panX = 0;
            panY = 0;
            return;
        }

        var scaledWidth = previewFrame.Width * currentZoom;
        var scaledHeight = previewFrame.Height * currentZoom;

        var maxPanX = Math.Max(0, (scaledWidth - previewFrame.Width) / 2);
        var maxPanY = Math.Max(0, (scaledHeight - previewFrame.Height) / 2);

        panX = Math.Clamp(panX, -maxPanX, maxPanX);
        panY = Math.Clamp(panY, -maxPanY, maxPanY);
    }

    private void ApplyTransform()
    {
        if (previewFrame?.RenderTransform is TransformGroup group && group.Children.Count >= 2)
        {
            if (group.Children[0] is ScaleTransform scale)
            {
                scale.ScaleX = currentZoom;
                scale.ScaleY = currentZoom;
            }
            if (group.Children[1] is TranslateTransform translate)
            {
                translate.X = panX;
                translate.Y = panY;
            }
        }
    }

    private void UpdatePreviewFrameSize()
    {
        if (previewFrame is null || previewViewport is null)
        {
            return;
        }

        var availableWidth = Math.Max(0, previewViewport.Bounds.Width - PreviewPadding * 2);
        var availableHeight = Math.Max(0, previewViewport.Bounds.Height - PreviewPadding * 2);

        if (availableWidth <= 0 || availableHeight <= 0)
        {
            return;
        }

        var frameWidthFromHeight = availableHeight * PreviewAspectRatio;
        var frameWidth = Math.Min(availableWidth, frameWidthFromHeight);
        var frameHeight = frameWidth / PreviewAspectRatio;

        previewFrame.Width = Math.Max(64, frameWidth);
        previewFrame.Height = Math.Max(112, frameHeight);
        currentPreviewFrameWidth = previewFrame.Width;
        currentPreviewFrameHeight = previewFrame.Height;
        
        if (boundViewModel != null)
        {
            boundViewModel.PreviewFrameWidth = currentPreviewFrameWidth;
            boundViewModel.PreviewFrameHeight = currentPreviewFrameHeight;
        }
        
        previewFrame.HorizontalAlignment = HorizontalAlignment.Center;
        previewFrame.VerticalAlignment = VerticalAlignment.Center;

        UpdateVideoForegroundBounds();

        ConstrainPan();
        ApplyTransform();
    }

    private void UpdateVideoForegroundBounds()
    {
        if (boundViewModel is null || !decoder.IsOpen || previewFrame is null) return;

        var sourceW = decoder.FrameWidth;
        var sourceH = decoder.FrameHeight;
        if (sourceW <= 0 || sourceH <= 0) return;

        var targetW = previewFrame.Width;
        var targetH = previewFrame.Height;

        var scaleX = targetW / sourceW;
        var scaleY = targetH / sourceH;
        var scale = Math.Min(scaleX, scaleY);

        boundViewModel.ForegroundWidth = sourceW * scale;
        boundViewModel.ForegroundHeight = sourceH * scale;
    }
}
