using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReelsVideoEditor.App.Services.AudioPlayback;
using ReelsVideoEditor.App.ViewModels.Preview;
using SkiaSharp;

namespace ReelsVideoEditor.App.Views.Preview;

public partial class PreviewPanelView
{
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
            case nameof(PreviewViewModel.CurrentZoom):
                if (Math.Abs(currentZoom - boundViewModel.CurrentZoom) > 0.001)
                {
                    currentZoom = boundViewModel.CurrentZoom;
                    ConstrainPan();
                    ApplyTransform();
                }
                break;
            case nameof(PreviewViewModel.UseBlurredBackground):
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
            playbackStartMilliseconds = viewModel.CurrentPlaybackMilliseconds;
            playbackStopwatch.Restart();
            SyncAudioToTimeline(viewModel, playbackStartMilliseconds, forceSeek: true);
            StartPlaybackLoop(viewModel);

            return;
        }

        playbackCts?.Cancel();
        if (viewModel.StopRequestVersion > handledStopRequestVersion)
        {
            handledStopRequestVersion = viewModel.StopRequestVersion;
            audioService.Stop();
            StopAllActiveAudioTracks();
            playbackStopwatch.Stop();
            viewModel.UpdatePlaybackTime(0);
            _ = RenderSeekFrameAsync(TimeSpan.Zero, viewModel);
            return;
        }

        audioService.Pause();
        PauseAllActiveAudioTracks();
        playbackStopwatch.Stop();
    }

    private async void ApplySeekRequest(PreviewViewModel viewModel)
    {
        if (viewModel.SeekRequestVersion <= handledSeekRequestVersion)
        {
            return;
        }

        handledSeekRequestVersion = viewModel.SeekRequestVersion;

        var targetMilliseconds = Math.Max(0, viewModel.RequestedSeekMilliseconds);
        var totalLength = viewModel.ResolvePlaybackMaxMilliseconds?.Invoke() ?? (long)decoder.Duration.TotalMilliseconds;
        if (totalLength > 0)
        {
            targetMilliseconds = Math.Min(targetMilliseconds, totalLength);
        }

        var targetTime = TimeSpan.FromMilliseconds(targetMilliseconds);
        SyncAudioToTimeline(viewModel, targetMilliseconds, forceSeek: true);
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
            PauseAllActiveAudioTracks();
        }

        pendingSeekPosition = targetTime;
        if (isSeeking)
        {
            return;
        }

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
        foreach (var track in activeAudioTracks.Values)
        {
            track.Service.IsMuted = viewModel.IsAudioMuted;
        }
    }

    private void SyncAudioToTimeline(PreviewViewModel viewModel, long timelineMilliseconds, bool forceSeek = false)
    {
        var resolvedAudioState = viewModel.ResolveAudioState?.Invoke(timelineMilliseconds);
        if (resolvedAudioState is null)
        {
            DisposeAllActiveAudioTracks();
            var fallbackMilliseconds = Math.Clamp(timelineMilliseconds, 0, (long)decoder.Duration.TotalMilliseconds);
            var currentAudioMilliseconds = (long)audioService.CurrentPosition.TotalMilliseconds;
            var shouldResyncFallback = forceSeek
                || !viewModel.IsPlaying
                || Math.Abs(fallbackMilliseconds - currentAudioMilliseconds) > AudioPlaybackResyncToleranceMs;
            if (shouldResyncFallback)
            {
                audioService.Seek(TimeSpan.FromMilliseconds(fallbackMilliseconds));
            }

            if (viewModel.IsPlaying)
            {
                audioService.Play();
            }
            else
            {
                audioService.Pause();
            }

            return;
        }

        audioService.Pause();

        if (!resolvedAudioState.ShouldPlay || resolvedAudioState.Tracks.Count == 0)
        {
            PauseAllActiveAudioTracks();
            return;
        }

        var activeTrackKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var trackState in resolvedAudioState.Tracks)
        {
            if (string.IsNullOrWhiteSpace(trackState.TrackKey)
                || string.IsNullOrWhiteSpace(trackState.Path))
            {
                continue;
            }

            activeTrackKeys.Add(trackState.TrackKey);

            if (!activeAudioTracks.TryGetValue(trackState.TrackKey, out var trackContext)
                || !string.Equals(trackContext.Path, trackState.Path, StringComparison.OrdinalIgnoreCase))
            {
                if (trackContext is not null)
                {
                    trackContext.Service.Dispose();
                }

                var service = new AudioPlaybackService();
                service.Open(trackState.Path);
                trackContext = new ActiveAudioTrackContext
                {
                    Path = trackState.Path,
                    Service = service
                };
                activeAudioTracks[trackState.TrackKey] = trackContext;
                forceSeek = true;
            }

            trackContext.Service.Volume = (float)Math.Clamp(trackState.VolumeLevel, 0.0, 1.0);
            trackContext.Service.IsMuted = viewModel.IsAudioMuted;

            var localMilliseconds = Math.Max(0, trackState.PlaybackMilliseconds);
            var currentMilliseconds = (long)trackContext.Service.CurrentPosition.TotalMilliseconds;
            var shouldResync = forceSeek
                || !viewModel.IsPlaying
                || Math.Abs(localMilliseconds - currentMilliseconds) > AudioPlaybackResyncToleranceMs;
            if (shouldResync)
            {
                trackContext.Service.Seek(TimeSpan.FromMilliseconds(localMilliseconds));
            }

            if (viewModel.IsPlaying)
            {
                trackContext.Service.Play();
            }
            else
            {
                trackContext.Service.Pause();
            }
        }

        var keysToRemove = activeAudioTracks.Keys
            .Where(existingKey => !activeTrackKeys.Contains(existingKey))
            .ToList();
        foreach (var key in keysToRemove)
        {
            activeAudioTracks[key].Service.Dispose();
            activeAudioTracks.Remove(key);
        }
    }

    private void LoadMedia(string path)
    {
        audioService.Stop();
        DisposeAllActiveAudioTracks();

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
            var totalMs = boundViewModel.ResolvePlaybackMaxMilliseconds?.Invoke() ?? (long)decoder.Duration.TotalMilliseconds;
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
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16.666));

            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
            {
                var elapsedMs = playbackStopwatch.ElapsedMilliseconds;
                var currentMs = playbackStartMilliseconds + elapsedMs;
                SyncAudioToTimeline(viewModel, currentMs);
                var totalMs = viewModel.ResolvePlaybackMaxMilliseconds?.Invoke() ?? (long)decoder.Duration.TotalMilliseconds;
                if (totalMs <= 0)
                {
                    totalMs = (long)decoder.Duration.TotalMilliseconds;
                }

                if (totalMs > 0 && currentMs >= totalMs)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        viewModel.UpdatePlaybackTime(totalMs);
                        viewModel.IsPlaying = false;
                        audioService.Stop();
                    });
                    break;
                }

                var currentTime = TimeSpan.FromMilliseconds(currentMs);

                SKBitmap? composed = null;
                var resolveLayers = viewModel.ResolveVideoLayers;
                if (resolveLayers is not null)
                {
                    var layers = resolveLayers(currentMs);
                    var hasAnySelection = viewModel.HasSelectedVideoClip?.Invoke() ?? false;
                    var hasActiveTransformTarget = viewModel.HasActiveTransformTarget?.Invoke(currentMs)
                        ?? layers.Any(layer => layer.IsSelected);
                    viewModel.IsTransformTargetActive = !hasAnySelection || hasActiveTransformTarget;
                    composed = ComposeMultipleLayers(viewModel, layers);
                    if (composed is null)
                    {
                        composed = ComposeBlackFrame(viewModel);
                    }
                }
                else
                {
                    lock (decoder)
                    {
                        if (!decoder.IsOpen)
                        {
                            break;
                        }

                        var pixels = decoder.ReadNextFrame(currentTime);
                        if (pixels != null)
                        {
                            var (targetW, targetH) = GetTargetResolution(viewModel, decoder.FrameWidth, decoder.FrameHeight);
                            var renderOffsetX = (float)(viewModel.TransformX * ((double)targetW / currentPreviewFrameWidth));
                            var renderOffsetY = (float)(viewModel.TransformY * ((double)targetH / currentPreviewFrameHeight));

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
                                (float)viewModel.CropBottom,
                                viewModel.UseBlurredBackground);
                        }
                    }
                }

                if (composed != null && !token.IsCancellationRequested)
                {
                    ApplyTextOverlaysToBitmap(composed, viewModel, currentMs);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            CopyToWriteableBitmap(composed, viewModel);
                            viewModel.UpdatePlaybackTime(currentMs);
                            viewModel.UpdateTotalPlaybackTime(totalMs);
                        }
                    }, DispatcherPriority.Render);
                }
            }
        }, token);
    }

    private void PauseAllActiveAudioTracks()
    {
        foreach (var track in activeAudioTracks.Values)
        {
            track.Service.Pause();
        }
    }

    private void StopAllActiveAudioTracks()
    {
        foreach (var track in activeAudioTracks.Values)
        {
            track.Service.Stop();
        }
    }

    private void DisposeAllActiveAudioTracks()
    {
        foreach (var track in activeAudioTracks.Values)
        {
            track.Service.Dispose();
        }

        activeAudioTracks.Clear();
    }

    private void DisposeResources()
    {
        if (boundViewModel is not null)
        {
            boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        playbackCts?.Cancel();
        decoder.Dispose();
        foreach (var kvp in overlayDecoders)
        {
            kvp.Value.Dispose();
        }

        overlayDecoders.Clear();
        audioService.Dispose();
        DisposeAllActiveAudioTracks();
        compositor.Dispose();
        renderTarget = null;
    }
}
