using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ReelsVideoEditor.App.Models;

namespace ReelsVideoEditor.App.Services.Watermarks;

public sealed class WatermarkPresetStorageService
{
    private const string AppDirectoryName = "ReelsVideoEditor";
    private const string PresetsFileName = "watermark-presets.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string presetsFilePath;

    public WatermarkPresetStorageService()
        : this(ResolveDefaultPath())
    {
    }

    internal WatermarkPresetStorageService(string filePath)
    {
        presetsFilePath = filePath;
    }

    public IReadOnlyList<WatermarkPresetDefinition> LoadCustomPresets()
    {
        try
        {
            if (!File.Exists(presetsFilePath))
            {
                return [];
            }

            var rawJson = File.ReadAllText(presetsFilePath);
            var stored = JsonSerializer.Deserialize<List<StoredWatermarkPreset>>(rawJson, JsonOptions);
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

    public void SaveCustomPresets(IEnumerable<WatermarkPresetDefinition> presets)
    {
        try
        {
            var normalized = presets
                .Select(CreateValidatedPreset)
                .Where(preset => preset is not null)
                .Select(preset => preset!)
                .Select(preset => new StoredWatermarkPreset(
                    preset.Name,
                    preset.ImagePath,
                    Math.Clamp(preset.Opacity, 0.0, 1.0)))
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

    private static WatermarkPresetDefinition? CreateValidatedPreset(StoredWatermarkPreset? stored)
    {
        if (stored is null)
        {
            return null;
        }

        return CreateValidatedPreset(new WatermarkPresetDefinition(stored.Name, stored.ImagePath, stored.Opacity));
    }

    private static WatermarkPresetDefinition? CreateValidatedPreset(WatermarkPresetDefinition? preset)
    {
        if (preset is null)
        {
            return null;
        }

        var name = preset.Name?.Trim();
        var imagePath = preset.ImagePath?.Trim();
        var opacity = Math.Clamp(preset.Opacity, 0.0, 1.0);

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return null;
        }

        return new WatermarkPresetDefinition(name, imagePath, opacity);
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

    private sealed record StoredWatermarkPreset(string Name, string ImagePath, double Opacity);
}
