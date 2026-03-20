using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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
    private const int CompositorTargetWidth = 540;
    private const int CompositorTargetHeight = 960;

    private readonly VideoFrameDecoder decoder = new();
    private readonly AudioPlaybackService audioService = new();
    private readonly FrameCompositor compositor = new();
    private readonly Border? previewFrame;
    private readonly Control? previewViewport;

    private PreviewViewModel? boundViewModel;
    private string? loadedPath;
    private int handledStopRequestVersion;
    private int handledSeekRequestVersion;
    private readonly Stopwatch playbackStopwatch = new();
    private long playbackStartMilliseconds;
    private WriteableBitmap? renderTarget;
    private bool isSeeking;
    private TimeSpan? pendingSeekPosition;
    private int fpsFrameCount;
    private long lastFpsTick;
    private byte[]? tempFrameCopyBuffer;
    private CancellationTokenSource? playbackCts;

    public PreviewPanelView()
    {
        InitializeComponent();

        VideoFrameDecoder.InitializeFFmpeg();

        previewFrame = this.FindControl<Border>("PreviewFrame");
        previewViewport = this.FindControl<Control>("PreviewViewport");

        if (previewViewport is not null)
        {
            previewViewport.SizeChanged += (_, _) => UpdatePreviewFrameSize();
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
                        // Background buffer reuse applies here! 0 unmanaged allocations.
                        composed = compositor.ComposeFrame(
                            pixels,
                            decoder.FrameWidth,
                            decoder.FrameHeight,
                            CompositorTargetWidth,
                            CompositorTargetHeight);
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

                return compositor.ComposeFrame(
                    pixels,
                    decoder.FrameWidth,
                    decoder.FrameHeight,
                    CompositorTargetWidth,
                    CompositorTargetHeight);
            }
        });

        if (composed is not null)
        {
            CopyToWriteableBitmap(composed, viewModel);
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

        var previewImage = this.FindControl<Image>("PreviewImage");
        previewImage?.InvalidateVisual();

        fpsFrameCount++;
        var currentTick = Environment.TickCount64;
        if (lastFpsTick == 0)
        {
            lastFpsTick = currentTick;
        }
        else if (currentTick - lastFpsTick >= 1000)
        {
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
        previewFrame.HorizontalAlignment = HorizontalAlignment.Center;
        previewFrame.VerticalAlignment = VerticalAlignment.Center;
    }
}
