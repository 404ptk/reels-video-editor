using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using ReelsVideoEditor.App.Services.AudioPlayback;
using ReelsVideoEditor.App.Services.Compositor;
using ReelsVideoEditor.App.Services.VideoDecoder;
using ReelsVideoEditor.App.ViewModels.Preview;

namespace ReelsVideoEditor.App.Views.Preview;

public partial class PreviewPanelView : UserControl
{
    private const double PreviewAspectRatio = 9.0 / 16.0;
    private const double PreviewPadding = 8;
    private const int AudioPlaybackResyncToleranceMs = 280;

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
    private readonly Dictionary<string, VideoFrameDecoder> overlayDecoders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ActiveAudioTrackContext> activeAudioTracks = new(StringComparer.Ordinal);

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
    private bool hasActiveTransformCropEdit;

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

    private sealed class ActiveAudioTrackContext
    {
        public required string Path { get; init; }

        public required AudioPlaybackService Service { get; init; }
    }

    public PreviewPanelView()
    {
        InitializeComponent();
        Focusable = true;

        VideoFrameDecoder.InitializeFFmpeg();

        var previewCanvas = this.FindControl<PreviewCanvasView>("PreviewCanvas");
        previewFrame = previewCanvas?.FindControl<Border>("PreviewFrame");
        previewViewport = previewCanvas?.FindControl<Control>("PreviewViewport");
        previewImage = previewCanvas?.FindControl<Image>("PreviewImage");

        if (previewViewport is not null)
        {
            previewViewport.Focusable = true;
            previewViewport.SizeChanged += (_, _) => UpdatePreviewFrameSize();
            previewViewport.PointerWheelChanged += OnPreviewPointerWheelChanged;
            previewViewport.PointerPressed += OnPreviewPointerPressed;
            previewViewport.PointerMoved += OnPreviewPointerMoved;
            previewViewport.PointerReleased += OnPreviewPointerReleased;
            previewViewport.PointerCaptureLost += OnPreviewPointerCaptureLost;
            previewViewport.KeyDown += OnPreviewViewportKeyDown;
        }

        Loaded += (_, _) => UpdatePreviewFrameSize();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => DisposeResources();
    }
}
