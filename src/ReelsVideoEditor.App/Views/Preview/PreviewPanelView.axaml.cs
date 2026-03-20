using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using ReelsVideoEditor.App.ViewModels.Preview;

namespace ReelsVideoEditor.App.Views.Preview;

public partial class PreviewPanelView : UserControl
{
    private const double PreviewAspectRatio = 9.0 / 16.0;
    private const double PreviewPadding = 8;

    private readonly LibVLC libVlc;
    private readonly MediaPlayer mediaPlayer;
    private readonly DispatcherTimer playbackTimeTimer;
    private readonly Border? previewFrame;
    private readonly Control? previewViewport;
    private readonly LibVLCSharp.Avalonia.VideoView? previewVideoView;
    private PreviewViewModel? boundViewModel;
    private string? loadedPath;
    private int handledStopRequestVersion;
    private int handledSeekRequestVersion;
    private double smoothedPlaybackMilliseconds;
    private DateTime lastPlaybackSampleUtc;
    private bool hasPlaybackSample;

    public PreviewPanelView()
    {
        InitializeComponent();

        Core.Initialize();
        libVlc = new LibVLC();
        mediaPlayer = new MediaPlayer(libVlc);
        mediaPlayer.EndReached += OnMediaPlayerEndReached;

        playbackTimeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        playbackTimeTimer.Tick += OnPlaybackTimeTimerTick;
        playbackTimeTimer.Start();

        previewFrame = this.FindControl<Border>("PreviewFrame");
        previewViewport = this.FindControl<Control>("PreviewViewport");

        if (previewViewport is not null)
        {
            previewViewport.SizeChanged += (_, _) => UpdatePreviewFrameSize();
        }

        previewVideoView = this.FindControl<LibVLCSharp.Avalonia.VideoView>("PreviewVideoView");
        if (previewVideoView is { } videoView)
        {
            videoView.MediaPlayer = mediaPlayer;
        }

        Loaded += (_, _) => UpdatePreviewFrameSize();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => DisposePlayer();
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
            ApplyPlaybackState(boundViewModel);
            ApplyAudioState(boundViewModel);
            ApplyVideoState(boundViewModel);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (boundViewModel is null)
        {
            return;
        }

        if (eventArgs.PropertyName is nameof(PreviewViewModel.IsPlaying)
            or nameof(PreviewViewModel.CurrentVideoPath)
            or nameof(PreviewViewModel.StopRequestVersion)
            or nameof(PreviewViewModel.SeekRequestVersion))
        {
            ApplyPlaybackState(boundViewModel);
        }
        else if (eventArgs.PropertyName == nameof(PreviewViewModel.IsAudioMuted))
        {
            ApplyAudioState(boundViewModel);
        }
        else if (eventArgs.PropertyName == nameof(PreviewViewModel.CurrentAudioVolume))
        {
            ApplyAudioState(boundViewModel);
        }
        else if (eventArgs.PropertyName == nameof(PreviewViewModel.IsVideoHidden))
        {
            ApplyVideoState(boundViewModel);
        }
    }

    private void ApplyAudioState(PreviewViewModel viewModel)
    {
        var clampedVolume = Math.Clamp(viewModel.CurrentAudioVolume, 0.0, 1.0);
        var targetVolume = viewModel.IsAudioMuted ? 0 : (int)Math.Round(clampedVolume * 100, MidpointRounding.AwayFromZero);
        mediaPlayer.Mute = targetVolume <= 0;
        if (targetVolume > 0)
        {
            mediaPlayer.Volume = targetVolume;
        }
    }

    private void ApplyVideoState(PreviewViewModel viewModel)
    {
        if (previewVideoView is null)
        {
            return;
        }

        if (viewModel.IsVideoHidden)
        {
            previewVideoView.Opacity = 0.0;
            mediaPlayer.SetAdjustInt(VideoAdjustOption.Enable, 1);
            mediaPlayer.SetAdjustFloat(VideoAdjustOption.Contrast, 1f);
            mediaPlayer.SetAdjustFloat(VideoAdjustOption.Brightness, 0f);
            return;
        }

        previewVideoView.Opacity = 1.0;

        mediaPlayer.SetAdjustInt(VideoAdjustOption.Enable, 1);
        mediaPlayer.SetAdjustFloat(VideoAdjustOption.Gamma, 1f);
        mediaPlayer.SetAdjustFloat(VideoAdjustOption.Saturation, 1f);
        mediaPlayer.SetAdjustFloat(VideoAdjustOption.Hue, 0f);

        mediaPlayer.SetAdjustFloat(VideoAdjustOption.Contrast, 1f);
        mediaPlayer.SetAdjustFloat(VideoAdjustOption.Brightness, 1f);
    }

    private void ApplyPlaybackState(PreviewViewModel viewModel)
    {
        var path = viewModel.CurrentVideoPath;
        if (!string.IsNullOrWhiteSpace(path) && !string.Equals(path, loadedPath, StringComparison.OrdinalIgnoreCase))
        {
            LoadMedia(path);
        }

        ApplySeekRequest(viewModel);

        if (viewModel.IsPlaying)
        {
            if (mediaPlayer.Media is not null)
            {
                if (mediaPlayer.State == VLCState.Ended)
                {
                    mediaPlayer.Time = 0;
                    ResetPlaybackClock();
                    viewModel.UpdatePlaybackTime(0);
                }

                mediaPlayer.Play();
            }

            return;
        }

        if (viewModel.StopRequestVersion > handledStopRequestVersion && mediaPlayer.Media is not null)
        {
            handledStopRequestVersion = viewModel.StopRequestVersion;
            mediaPlayer.Stop();
            ResetPlaybackClock();
            viewModel.UpdatePlaybackTime(0);
            return;
        }

        mediaPlayer.SetPause(true);
        FreezePlaybackClockAtCurrentFrame(viewModel);
    }

    private void ApplySeekRequest(PreviewViewModel viewModel)
    {
        if (viewModel.SeekRequestVersion <= handledSeekRequestVersion)
        {
            return;
        }

        if (mediaPlayer.Media is null)
        {
            return;
        }

        handledSeekRequestVersion = viewModel.SeekRequestVersion;

        var targetMilliseconds = Math.Max(0, viewModel.RequestedSeekMilliseconds);
        var totalLength = Math.Max(0, mediaPlayer.Length);
        if (totalLength > 0)
        {
            targetMilliseconds = Math.Min(targetMilliseconds, totalLength);
        }

        if (!mediaPlayer.IsPlaying && mediaPlayer.State != VLCState.Paused)
        {
            mediaPlayer.Play();
        }

        mediaPlayer.Time = targetMilliseconds;
        smoothedPlaybackMilliseconds = targetMilliseconds;
        hasPlaybackSample = true;
        lastPlaybackSampleUtc = DateTime.UtcNow;
        viewModel.UpdatePlaybackTime(targetMilliseconds);

        if (!viewModel.IsPlaying)
        {
            mediaPlayer.SetPause(true);
            FreezePlaybackClockAtCurrentFrame(viewModel);
        }
    }

    private void LoadMedia(string path)
    {
        mediaPlayer.Stop();
        mediaPlayer.Media = new Media(libVlc, path, FromType.FromPath);
        mediaPlayer.SetAdjustInt(VideoAdjustOption.Enable, 1);
        mediaPlayer.SetAdjustFloat(VideoAdjustOption.Contrast, 1f);
        mediaPlayer.SetAdjustFloat(VideoAdjustOption.Brightness, 1f);
        mediaPlayer.SetAdjustFloat(VideoAdjustOption.Gamma, 1f);
        mediaPlayer.SetAdjustFloat(VideoAdjustOption.Saturation, 1f);
        mediaPlayer.SetAdjustFloat(VideoAdjustOption.Hue, 0f);
        loadedPath = path;
        ResetPlaybackClock();
        boundViewModel?.UpdatePlaybackTime(0);
    }

    private void DisposePlayer()
    {
        if (boundViewModel is not null)
        {
            boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        playbackTimeTimer.Stop();
        playbackTimeTimer.Tick -= OnPlaybackTimeTimerTick;
        mediaPlayer.EndReached -= OnMediaPlayerEndReached;
        mediaPlayer.Dispose();
        libVlc.Dispose();
    }

    private void OnPlaybackTimeTimerTick(object? sender, EventArgs eventArgs)
    {
        if (boundViewModel is null)
        {
            return;
        }

        if (mediaPlayer.Media is null)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var rawTime = Math.Max(0, mediaPlayer.Time);
        var totalTime = Math.Max(0, mediaPlayer.Length);

        if (!hasPlaybackSample)
        {
            smoothedPlaybackMilliseconds = rawTime;
            lastPlaybackSampleUtc = nowUtc;
            hasPlaybackSample = true;
        }
        else if (boundViewModel.IsPlaying && mediaPlayer.IsPlaying)
        {
            var elapsedMilliseconds = Math.Max(0, (nowUtc - lastPlaybackSampleUtc).TotalMilliseconds);
            var predicted = smoothedPlaybackMilliseconds + elapsedMilliseconds;

            if (rawTime > predicted + 180 || rawTime < predicted - 260)
            {
                smoothedPlaybackMilliseconds = rawTime;
            }
            else
            {
                smoothedPlaybackMilliseconds = Math.Max(predicted, rawTime);
            }

            lastPlaybackSampleUtc = nowUtc;
        }
        else
        {
            smoothedPlaybackMilliseconds = rawTime;
            lastPlaybackSampleUtc = nowUtc;
        }

        if (totalTime > 0)
        {
            smoothedPlaybackMilliseconds = Math.Min(smoothedPlaybackMilliseconds, totalTime);
        }

        boundViewModel.UpdatePlaybackTime((long)smoothedPlaybackMilliseconds);
        boundViewModel.UpdateTotalPlaybackTime(totalTime);

        if (mediaPlayer.State == VLCState.Ended)
        {
            CompletePlaybackAtEnd();
        }
    }

    private void OnMediaPlayerEndReached(object? sender, EventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(CompletePlaybackAtEnd);
    }

    private void CompletePlaybackAtEnd()
    {
        if (boundViewModel is null)
        {
            return;
        }

        var totalTime = Math.Max(0, mediaPlayer.Length);
        if (totalTime > 0)
        {
            smoothedPlaybackMilliseconds = totalTime;
            hasPlaybackSample = true;
            lastPlaybackSampleUtc = DateTime.UtcNow;
            boundViewModel.UpdateTotalPlaybackTime(totalTime);
            boundViewModel.UpdatePlaybackTime(totalTime);
        }

        if (boundViewModel.IsPlaying)
        {
            boundViewModel.IsPlaying = false;
        }
    }

    private void ResetPlaybackClock()
    {
        smoothedPlaybackMilliseconds = 0;
        hasPlaybackSample = false;
        lastPlaybackSampleUtc = DateTime.UtcNow;
    }

    private void FreezePlaybackClockAtCurrentFrame(PreviewViewModel viewModel)
    {
        if (mediaPlayer.Media is null)
        {
            return;
        }

        var rawTime = Math.Max(0, mediaPlayer.Time);
        smoothedPlaybackMilliseconds = rawTime;
        hasPlaybackSample = true;
        lastPlaybackSampleUtc = DateTime.UtcNow;
        viewModel.UpdatePlaybackTime(rawTime);
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
