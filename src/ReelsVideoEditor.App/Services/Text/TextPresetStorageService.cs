using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Media;
using ReelsVideoEditor.App.Models;

namespace ReelsVideoEditor.App.Services.Text;

public sealed class TextPresetStorageService
{
    private const string AppDirectoryName = "ReelsVideoEditor";
    private const string PresetsFileName = "text-presets.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string presetsFilePath;

    public TextPresetStorageService()
        : this(ResolveDefaultPath())
    {
    }

    internal TextPresetStorageService(string filePath)
    {
        presetsFilePath = filePath;
    }

    public IReadOnlyList<TextPresetDefinition> LoadCustomPresets()
    {
        try
        {
            if (!File.Exists(presetsFilePath))
            {
                return [];
            }

            var rawJson = File.ReadAllText(presetsFilePath);
            var stored = JsonSerializer.Deserialize<List<StoredTextPreset>>(rawJson, JsonOptions);
            if (stored is null || stored.Count == 0)
            {
                return [];
            }

            return stored
                .Select(CreateValidatedPreset)
                .Where(preset => preset is not null)
                .Select(preset => preset!)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public void SaveCustomPresets(IEnumerable<TextPresetDefinition> presets)
    {
        try
        {
            var normalized = presets
                .Select(CreateValidatedPreset)
                .Where(preset => preset is not null)
                .Select(preset => preset!)
                .Select(preset => new StoredTextPreset(
                    preset.Name,
                    preset.FontFamily,
                    preset.FontSize,
                    preset.ColorHex,
                    preset.OutlineColorHex,
                    preset.OutlineThickness))
                .ToArray();

            var directoryPath = Path.GetDirectoryName(presetsFilePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var payload = JsonSerializer.Serialize(normalized, JsonOptions);
            File.WriteAllText(presetsFilePath, payload);
        }
        catch
        {
            // Ignore write errors to keep UI flow uninterrupted.
        }
    }

    private static TextPresetDefinition? CreateValidatedPreset(StoredTextPreset? stored)
    {
        if (stored is null)
        {
            return null;
        }

        return CreateValidatedPreset(new TextPresetDefinition(
            stored.Name,
            stored.FontFamily,
            stored.FontSize,
            stored.ColorHex));
    }

    private static TextPresetDefinition? CreateValidatedPreset(TextPresetDefinition? preset)
    {
        if (preset is null)
        {
            return null;
        }

        var name = preset.Name?.Trim();
        var fontFamily = preset.FontFamily?.Trim();
        var fontSize = Math.Clamp(preset.FontSize, 10, 180);
        var colorHex = preset.ColorHex?.Trim();
        var outlineColorHex = preset.OutlineColorHex?.Trim();
        var outlineThickness = Math.Clamp(preset.OutlineThickness, 0, 24);

        if (string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(fontFamily)
            || string.IsNullOrWhiteSpace(colorHex)
            || !Color.TryParse(colorHex, out var parsedColor))
        {
            return null;
        }

        var parsedOutlineColor = Colors.Black;
        if (!string.IsNullOrWhiteSpace(outlineColorHex) && Color.TryParse(outlineColorHex, out var validOutlineColor))
        {
            parsedOutlineColor = validOutlineColor;
        }

        var normalizedHex = $"#{parsedColor.R:X2}{parsedColor.G:X2}{parsedColor.B:X2}";
        var normalizedOutlineHex = $"#{parsedOutlineColor.R:X2}{parsedOutlineColor.G:X2}{parsedOutlineColor.B:X2}";
        return new TextPresetDefinition(name, fontFamily, fontSize, normalizedHex, normalizedOutlineHex, outlineThickness);
    }

    private static string ResolveDefaultPath()
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = AppContext.BaseDirectory;
        }

        return Path.Combine(baseDirectory, AppDirectoryName, PresetsFileName);
    }

    private sealed record StoredTextPreset(
        string Name,
        string FontFamily,
        double FontSize,
        string ColorHex,
        string OutlineColorHex = "#000000",
        double OutlineThickness = 0);
}
