using System;
using System.Globalization;

namespace ReelsVideoEditor.App.DragDrop;

public static class VideoClipDragPayload
{
    public const string Format = "application/x-reels-video-clip";

    public static string Build(string path, string name, double durationSeconds)
    {
        return $"{Uri.EscapeDataString(path)}|{Uri.EscapeDataString(name)}|{durationSeconds.ToString(CultureInfo.InvariantCulture)}";
    }

    public static bool TryParse(object? rawPayload, out string path, out string name, out double durationSeconds)
    {
        path = string.Empty;
        name = string.Empty;
        durationSeconds = 0;

        if (rawPayload is not string payload || string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var parts = payload.Split('|');
        if (parts.Length != 3)
        {
            return false;
        }

        var parsedDuration = double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out durationSeconds);
        if (!parsedDuration)
        {
            return false;
        }

        path = Uri.UnescapeDataString(parts[0]);
        name = Uri.UnescapeDataString(parts[1]);
        return !string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(name);
    }
}
