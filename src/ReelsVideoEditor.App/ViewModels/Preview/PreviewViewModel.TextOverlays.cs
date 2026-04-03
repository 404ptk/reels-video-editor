using System;
using System.Collections.ObjectModel;
using ReelsVideoEditor.App.ViewModels.Timeline;

namespace ReelsVideoEditor.App.ViewModels.Preview;

public sealed partial class PreviewViewModel
{
    public ObservableCollection<PreviewTextOverlayLayer> TextOverlays { get; } = [];

    public void UpdateTextOverlayState(TimelineTextOverlayState state)
    {
        TextOverlays.Clear();
        if (!state.IsVisible)
        {
            return;
        }

        var safeWidth = Math.Max(1.0, PreviewFrameWidth);
        var safeHeight = Math.Max(1.0, PreviewFrameHeight);
        for (var i = 0; i < state.Layers.Count; i++)
        {
            var layer = state.Layers[i];
            if (string.IsNullOrWhiteSpace(layer.Text))
            {
                continue;
            }

            var previewLayer = new PreviewTextOverlayLayer();
            previewLayer.Apply(layer, safeWidth, safeHeight);
            TextOverlays.Add(previewLayer);
        }
    }

    private void UpdateTextOverlayLayouts()
    {
        var safeWidth = Math.Max(1.0, PreviewFrameWidth);
        var safeHeight = Math.Max(1.0, PreviewFrameHeight);
        for (var i = 0; i < TextOverlays.Count; i++)
        {
            TextOverlays[i].RecomputeLayout(safeWidth, safeHeight);
        }
    }
}
