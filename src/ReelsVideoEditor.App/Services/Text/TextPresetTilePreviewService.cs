using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ReelsVideoEditor.App.Models;
using SkiaSharp;

namespace ReelsVideoEditor.App.Services.Text;

public static class TextPresetTilePreviewService
{
    private const int PreviewWidth = 128;
    private const int PreviewHeight = 44;
    private const float HorizontalPadding = 4f;
    private const float VerticalPadding = 4f;

    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new(StringComparer.Ordinal);

    public static IImage? GetOrCreate(TextPresetDefinition preset)
    {
        if (preset.IsAddTile)
        {
            return null;
        }

        var cacheKey = BuildCacheKey(preset);
        return Cache.GetOrAdd(cacheKey, _ => RenderPreviewBitmap(preset));
    }

    private static string BuildCacheKey(TextPresetDefinition preset)
    {
        return string.Join("|",
            preset.Name,
            preset.DisplayText,
            preset.FontFamily,
            preset.FontSize.ToString("0.###", CultureInfo.InvariantCulture),
            preset.ColorHex,
            preset.OutlineColorHex,
            preset.OutlineThickness.ToString("0.###", CultureInfo.InvariantCulture),
            preset.LineHeightMultiplier.ToString("0.###", CultureInfo.InvariantCulture),
            preset.LetterSpacing.ToString("0.###", CultureInfo.InvariantCulture));
    }

    private static Bitmap RenderPreviewBitmap(TextPresetDefinition preset)
    {
        using var skBitmap = new SKBitmap(PreviewWidth, PreviewHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        skBitmap.Erase(SKColors.Transparent);

        var rawText = string.IsNullOrWhiteSpace(preset.DisplayText)
            ? "Preview"
            : preset.DisplayText;
        var text = rawText
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ');

        if (string.IsNullOrWhiteSpace(text))
        {
            text = "Preview";
        }

        var typeface = ResolveTypeface(preset.FontFamily);
        var fillColor = ParseColor(preset.ColorHex, SKColors.White);
        var outlineColor = ParseColor(preset.OutlineColorHex, SKColors.Black);
        var maxFontSize = (float)Math.Clamp(preset.FontSize * 1.8, 16, 34);
        var letterSpacing = (float)Math.Clamp(preset.LetterSpacing, 0, 20);

        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Color = fillColor,
            Typeface = typeface,
            TextAlign = SKTextAlign.Left,
            Style = SKPaintStyle.Fill
        };

        var targetWidth = PreviewWidth - (HorizontalPadding * 2f);
        var targetHeight = PreviewHeight - (VerticalPadding * 2f);
        var fittedFontSize = FitSingleLineFontSize(text, fillPaint, maxFontSize, targetWidth, targetHeight, letterSpacing);
        fillPaint.TextSize = fittedFontSize;

        var metrics = fillPaint.FontMetrics;
        var baselineY = (PreviewHeight / 2f) - ((metrics.Ascent + metrics.Descent) / 2f);
        var textWidth = MeasureTextWidth(text, fillPaint, letterSpacing);
        var startX = (PreviewWidth - textWidth) / 2f;

        var outlineThickness = (float)Math.Clamp(preset.OutlineThickness, 0, 24);
        if (outlineThickness > 0.01f)
        {
            using var outlinePaint = new SKPaint
            {
                IsAntialias = true,
                Color = outlineColor,
                Typeface = typeface,
                TextSize = fittedFontSize,
                TextAlign = SKTextAlign.Left,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(0.5f, outlineThickness * (fittedFontSize / 32f)),
                StrokeJoin = SKStrokeJoin.Round,
                StrokeCap = SKStrokeCap.Round
            };

            using var outlineCanvas = new SKCanvas(skBitmap);
            DrawTextWithLetterSpacing(outlineCanvas, text, startX, baselineY, outlinePaint, letterSpacing);
        }

        using (var fillCanvas = new SKCanvas(skBitmap))
        {
            DrawTextWithLetterSpacing(fillCanvas, text, startX, baselineY, fillPaint, letterSpacing);
        }

        using var image = SKImage.FromBitmap(skBitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null)
        {
            throw new InvalidOperationException("Failed to encode preset preview bitmap.");
        }

        using var stream = new MemoryStream(encoded.ToArray());
        return new Bitmap(stream);
    }

    private static float FitSingleLineFontSize(
        string text,
        SKPaint paint,
        float maxFontSize,
        float maxWidth,
        float maxHeight,
        float letterSpacing)
    {
        var fontSize = maxFontSize;
        while (fontSize > 8f)
        {
            paint.TextSize = fontSize;
            var width = MeasureTextWidth(text, paint, letterSpacing);
            var metrics = paint.FontMetrics;
            var height = metrics.Descent - metrics.Ascent;
            if (width <= maxWidth && height <= maxHeight)
            {
                break;
            }

            fontSize -= 0.5f;
        }

        return Math.Max(8f, fontSize);
    }

    private static void DrawTextWithLetterSpacing(SKCanvas canvas, string text, float startX, float baselineY, SKPaint paint, float letterSpacing)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (letterSpacing <= 0.01f || text.Length == 1)
        {
            canvas.DrawText(text, startX, baselineY, paint);
            return;
        }

        var penX = startX;
        for (var i = 0; i < text.Length; i++)
        {
            var glyph = text.Substring(i, 1);
            canvas.DrawText(glyph, penX, baselineY, paint);
            penX += paint.MeasureText(glyph) + letterSpacing;
        }
    }

    private static float MeasureTextWidth(string text, SKPaint paint, float letterSpacing)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0f;
        }

        var width = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            width += paint.MeasureText(text.Substring(i, 1));
        }

        return width + Math.Max(0f, letterSpacing) * Math.Max(0, text.Length - 1);
    }

    private static SKTypeface ResolveTypeface(string fontFamily)
    {
        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            var byFamily = SKTypeface.FromFamilyName(
                fontFamily,
                SKFontStyleWeight.SemiBold,
                SKFontStyleWidth.Normal,
                SKFontStyleSlant.Upright);
            if (byFamily is not null)
            {
                return byFamily;
            }
        }

        return SKTypeface.Default;
    }

    private static SKColor ParseColor(string? value, SKColor fallback)
    {
        if (!string.IsNullOrWhiteSpace(value) && SKColor.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }
}
