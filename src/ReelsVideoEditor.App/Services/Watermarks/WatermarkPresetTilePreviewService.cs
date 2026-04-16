using System;
using System.Collections.Concurrent;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ReelsVideoEditor.App.Services.Watermarks;

public static class WatermarkPresetTilePreviewService
{
    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IImage? GetOrCreate(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return null;
        }

        try
        {
            return Cache.GetOrAdd(imagePath, path => new Bitmap(path));
        }
        catch
        {
            return null;
        }
    }
}
