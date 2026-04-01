using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Avalonia.Media;
using Microsoft.Win32;

namespace ReelsVideoEditor.App.ViewModels.Text;

public sealed partial class TextViewModel
{
    private string ResolveAvailableFontFamily(string? fontFamily)
    {
        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            var requested = fontFamily.Trim();

            var exactRenderable = RenderableFonts.FirstOrDefault(font =>
                string.Equals(font, requested, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exactRenderable))
            {
                return exactRenderable;
            }

            var normalizedRequested = NormalizeFontLookup(requested);
            var normalizedRequestedWithoutPrefix = TrimLeadingDigits(normalizedRequested);
            if (normalizedRequested.Length > 0)
            {
                var normalizedExact = RenderableFonts.FirstOrDefault(font =>
                    string.Equals(NormalizeFontLookup(font), normalizedRequested, StringComparison.Ordinal));
                if (!string.IsNullOrWhiteSpace(normalizedExact))
                {
                    return normalizedExact;
                }

                if (normalizedRequestedWithoutPrefix.Length > 0)
                {
                    var normalizedExactWithoutPrefix = RenderableFonts.FirstOrDefault(font =>
                        string.Equals(TrimLeadingDigits(NormalizeFontLookup(font)), normalizedRequestedWithoutPrefix, StringComparison.Ordinal));
                    if (!string.IsNullOrWhiteSpace(normalizedExactWithoutPrefix))
                    {
                        return normalizedExactWithoutPrefix;
                    }
                }

                var aliasMatch = RenderableFonts
                    .Select(font => new
                    {
                        Font = font,
                        Normalized = NormalizeFontLookup(font),
                        NormalizedWithoutPrefix = TrimLeadingDigits(NormalizeFontLookup(font))
                    })
                    .Where(item => item.Normalized.Length > 0
                        && (
                            normalizedRequested.StartsWith(item.Normalized, StringComparison.Ordinal)
                            || item.Normalized.StartsWith(normalizedRequested, StringComparison.Ordinal)
                            || (normalizedRequestedWithoutPrefix.Length > 0
                                && item.NormalizedWithoutPrefix.Length > 0
                                && (normalizedRequestedWithoutPrefix.StartsWith(item.NormalizedWithoutPrefix, StringComparison.Ordinal)
                                    || item.NormalizedWithoutPrefix.StartsWith(normalizedRequestedWithoutPrefix, StringComparison.Ordinal)))))
                    .OrderBy(item => Math.Abs(item.Normalized.Length - normalizedRequested.Length))
                    .ThenBy(item => item.Font, StringComparer.OrdinalIgnoreCase)
                    .Select(item => item.Font)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(aliasMatch))
                {
                    return aliasMatch;
                }
            }
        }

        return RenderableFonts.FirstOrDefault() ?? AvailableFonts.FirstOrDefault() ?? "Inter";
    }

    private static IReadOnlyList<string> LoadRenderableFonts()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in FontManager.Current.SystemFonts)
        {
            if (!string.IsNullOrWhiteSpace(family.Name))
            {
                names.Add(family.Name);
            }
        }

        if (names.Count == 0)
        {
            names.Add("Inter");
        }

        return names
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> LoadAvailableFonts(IReadOnlyList<string> renderableFonts)
    {
        var names = new HashSet<string>(renderableFonts, StringComparer.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows())
        {
            AddWindowsRegistryFonts(names, Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
            AddWindowsRegistryFonts(names, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
        }

        if (names.Count == 0)
        {
            names.Add("Inter");
        }

        return names
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeFontLookup(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsRegistryFonts(HashSet<string> names, RegistryKey root, string subKeyPath)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyPath);
            if (key is null)
            {
                return;
            }

            foreach (var valueName in key.GetValueNames())
            {
                var normalizedName = NormalizeRegistryFontName(valueName);
                if (!string.IsNullOrWhiteSpace(normalizedName))
                {
                    names.Add(normalizedName);
                }
            }
        }
        catch
        {
            // Ignore registry access problems and continue with discovered fonts.
        }
    }

    private static string NormalizeRegistryFontName(string? valueName)
    {
        if (string.IsNullOrWhiteSpace(valueName))
        {
            return string.Empty;
        }

        var separatorIndex = valueName.IndexOf(" (", StringComparison.Ordinal);
        var normalized = separatorIndex > 0
            ? valueName[..separatorIndex].Trim()
            : valueName.Trim();

        return TrimRegistryPrefix(normalized);
    }

    private static string TrimRegistryPrefix(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var startIndex = 0;
        while (startIndex < value.Length
               && (char.IsDigit(value[startIndex])
                   || char.IsWhiteSpace(value[startIndex])
                   || value[startIndex] == '-'
                   || value[startIndex] == '_'
                   || value[startIndex] == '.'))
        {
            startIndex++;
        }

        var trimmed = value[startIndex..].Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? value.Trim() : trimmed;
    }

    private static string TrimLeadingDigits(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var index = 0;
        while (index < value.Length && char.IsDigit(value[index]))
        {
            index++;
        }

        return value[index..];
    }
}
