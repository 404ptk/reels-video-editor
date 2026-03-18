using System;
using System.ComponentModel;
using Avalonia.Controls;
using LibVLCSharp.Shared;
using ReelsVideoEditor.App.ViewModels.Preview;

namespace ReelsVideoEditor.App.Views.Preview;

public partial class PreviewPanelView : UserControl
{
    private readonly LibVLC libVlc;
    private readonly MediaPlayer mediaPlayer;
    private PreviewViewModel? boundViewModel;
    private string? loadedPath;
    private int handledStopRequestVersion;

    public PreviewPanelView()
    {
        InitializeComponent();

        Core.Initialize();
        libVlc = new LibVLC();
        mediaPlayer = new MediaPlayer(libVlc);

        if (this.FindControl<LibVLCSharp.Avalonia.VideoView>("PreviewVideoView") is { } videoView)
        {
            videoView.MediaPlayer = mediaPlayer;
        }

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
            or nameof(PreviewViewModel.StopRequestVersion))
        {
            ApplyPlaybackState(boundViewModel);
        }
    }

    private void ApplyPlaybackState(PreviewViewModel viewModel)
    {
        var path = viewModel.CurrentVideoPath;
        if (!string.IsNullOrWhiteSpace(path) && !string.Equals(path, loadedPath, StringComparison.OrdinalIgnoreCase))
        {
            LoadMedia(path);
        }

        if (viewModel.IsPlaying)
        {
            if (mediaPlayer.Media is not null)
            {
                mediaPlayer.Play();
            }

            return;
        }

        if (viewModel.StopRequestVersion > handledStopRequestVersion && mediaPlayer.Media is not null)
        {
            handledStopRequestVersion = viewModel.StopRequestVersion;
            mediaPlayer.Stop();
            return;
        }

        mediaPlayer.SetPause(true);
    }

    private void LoadMedia(string path)
    {
        mediaPlayer.Stop();
        mediaPlayer.Media = new Media(libVlc, path, FromType.FromPath);
        loadedPath = path;
    }

    private void DisposePlayer()
    {
        if (boundViewModel is not null)
        {
            boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        mediaPlayer.Dispose();
        libVlc.Dispose();
    }
}
