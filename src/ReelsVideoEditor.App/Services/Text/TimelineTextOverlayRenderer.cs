using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ReelsVideoEditor.App.ViewModels.Timeline;
using SkiaSharp;

namespace ReelsVideoEditor.App.Services.Text;

public static class TimelineTextOverlayRenderer
{
    private const double TextOverlayReferenceHeight = 1280.0;

    public static void Draw(
        SKBitmap bitmap,
        TimelineTextOverlayState state,
        int targetHeight,
        double previewFrameWidth,
        double previewFrameHeight)
    {
        if (!state.IsVisible)
        {
            return;
        }

        var scale = targetHeight / TextOverlayReferenceHeight;
        var safePreviewWidth = Math.Max(1.0, previewFrameWidth);
        var safePreviewHeight = Math.Max(1.0, previewFrameHeight);
        var offsetScaleX = bitmap.Width / safePreviewWidth;
        var offsetScaleY = bitmap.Height / safePreviewHeight;
        using var canvas = new SKCanvas(bitmap);

        for (var layerIndex = 0; layerIndex < state.Layers.Count; layerIndex++)
        {
            var layerState = state.Layers[layerIndex];
            if (string.IsNullOrWhiteSpace(layerState.Text))
            {
                continue;
            }

            var textScale = Math.Max(0.1, layerState.TransformScale * layerState.AnimationScale);
            var fontSize = Math.Max(1f, (float)(layerState.FontSize * scale * textScale));
            var offsetX = (float)(layerState.TransformX * offsetScaleX);
            var offsetY = (float)(layerState.TransformY * offsetScaleY);
            var color = ParseTextColor(layerState.ColorHex);
            var outlineColor = ParseTextColor(layerState.OutlineColorHex);
            var outlineThickness = Math.Max(0f, (float)(layerState.OutlineThickness * scale * textScale));
            var lineHeightMultiplier = Math.Clamp((float)layerState.LineHeightMultiplier, 0.7f, 2.5f);
            var letterSpacing = Math.Max(0f, (float)(layerState.LetterSpacing * scale * textScale));
            var cropLeft = Math.Clamp(layerState.CropLeft, 0.0, 0.95);
            var cropTop = Math.Clamp(layerState.CropTop, 0.0, 0.95);
            var cropRight = Math.Clamp(layerState.CropRight, 0.0, 0.95);
            var cropBottom = Math.Clamp(layerState.CropBottom, 0.0, 0.95);

            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = color,
                Typeface = ResolveTypeface(layerState.FontFamily),
                TextSize = fontSize,
                TextAlign = SKTextAlign.Center
            };

            using var outlinePaint = new SKPaint
            {
                IsAntialias = true,
                Color = outlineColor,
                Typeface = paint.Typeface,
                TextSize = fontSize,
                TextAlign = SKTextAlign.Center,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = outlineThickness,
                StrokeJoin = SKStrokeJoin.Round,
                StrokeCap = SKStrokeCap.Round
            };

            var lines = layerState.Text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');
            if (lines.Length == 0 || !lines.Any(line => !string.IsNullOrWhiteSpace(line)))
            {
                continue;
            }

            var layerWidth = (float)(bitmap.Width * textScale);
            var layerHeight = (float)(bitmap.Height * textScale);
            var layerLeft = ((bitmap.Width - layerWidth) / 2f) + offsetX;
            var layerTop = ((bitmap.Height - layerHeight) / 2f) + offsetY;
            var clipLeft = layerLeft + (float)(layerWidth * cropLeft);
            var clipTop = layerTop + (float)(layerHeight * cropTop);
            var clipWidth = Math.Max(1f, (float)(layerWidth * (1.0 - cropLeft - cropRight)));
            var clipHeight = Math.Max(1f, (float)(layerHeight * (1.0 - cropTop - cropBottom)));

            var wrappedLines = WrapTextLines(lines, paint, clipWidth, letterSpacing);
            if (wrappedLines.Count == 0 || !wrappedLines.Any(line => !string.IsNullOrWhiteSpace(line)))
            {
                continue;
            }

            var metrics = paint.FontMetrics;
            var lineHeight = Math.Max(1f, (fontSize * 1.2f) * lineHeightMultiplier);

            var totalTextHeight = lineHeight * wrappedLines.Count;
            var centerX = layerLeft + (layerWidth / 2f);
            var centerY = layerTop + (layerHeight / 2f);
            var firstBaselineY = centerY
                - (totalTextHeight / 2f)
                - metrics.Ascent;

            canvas.Save();
            canvas.ClipRect(SKRect.Create(clipLeft, clipTop, clipWidth, clipHeight));

            for (var lineIndex = 0; lineIndex < wrappedLines.Count; lineIndex++)
            {
                var line = wrappedLines[lineIndex];
                if (line.Length == 0)
                {
                    continue;
                }

                var baselineY = firstBaselineY + (lineIndex * lineHeight);
                if (outlineThickness > 0.01f)
                {
                    DrawTextWithLetterSpacing(canvas, line, centerX, baselineY, outlinePaint, letterSpacing);
                }

                DrawTextWithLetterSpacing(canvas, line, centerX, baselineY, paint, letterSpacing);
            }

            canvas.Restore();
        }
    }

    private static void DrawTextWithLetterSpacing(SKCanvas canvas, string text, float centerX, float baselineY, SKPaint paint, float letterSpacing)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (letterSpacing <= 0.01f || text.Length == 1)
        {
            canvas.DrawText(text, centerX, baselineY, paint);
            return;
        }

        var glyphWidths = new float[text.Length];
        var totalWidth = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            var glyph = text.Substring(i, 1);
            glyphWidths[i] = paint.MeasureText(glyph);
            totalWidth += glyphWidths[i];
        }

        totalWidth += letterSpacing * (text.Length - 1);

        var originalAlign = paint.TextAlign;
        paint.TextAlign = SKTextAlign.Left;

        var penX = centerX - (totalWidth / 2f);
        for (var i = 0; i < text.Length; i++)
        {
            var glyph = text.Substring(i, 1);
            canvas.DrawText(glyph, penX, baselineY, paint);
            penX += glyphWidths[i] + letterSpacing;
        }

        paint.TextAlign = originalAlign;
    }

    private static List<string> WrapTextLines(string[] lines, SKPaint paint, float maxWidth, float letterSpacing)
    {
        var wrapped = new List<string>(lines.Length);
        var safeMaxWidth = Math.Max(1f, maxWidth);

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i] ?? string.Empty;
            if (raw.Length == 0)
            {
                wrapped.Add(string.Empty);
                continue;
            }

            var words = raw.Split(' ', StringSplitOptions.None);
            var current = string.Empty;
            for (var wordIndex = 0; wordIndex < words.Length; wordIndex++)
            {
                var word = words[wordIndex];
                var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
                if (string.IsNullOrEmpty(current) || MeasureTextWidth(candidate, paint, letterSpacing) <= safeMaxWidth)
                {
                    current = candidate;
                    continue;
                }

                wrapped.AddRange(BreakLineIfNeeded(current, paint, safeMaxWidth, letterSpacing));
                current = word;
            }

            if (!string.IsNullOrEmpty(current))
            {
                wrapped.AddRange(BreakLineIfNeeded(current, paint, safeMaxWidth, letterSpacing));
            }
        }

        return wrapped;
    }

    private static IEnumerable<string> BreakLineIfNeeded(string line, SKPaint paint, float maxWidth, float letterSpacing)
    {
        if (string.IsNullOrEmpty(line))
        {
            yield return string.Empty;
            yield break;
        }

        if (MeasureTextWidth(line, paint, letterSpacing) <= maxWidth)
        {
            yield return line;
            yield break;
        }

        var current = string.Empty;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i].ToString();
            var candidate = current + ch;
            if (string.IsNullOrEmpty(current) || MeasureTextWidth(candidate, paint, letterSpacing) <= maxWidth)
            {
                current = candidate;
                continue;
            }

            yield return current;
            current = ch;
        }

        if (!string.IsNullOrEmpty(current))
        {
            yield return current;
        }
    }

    private static float MeasureTextWidth(string text, SKPaint paint, float letterSpacing)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0f;
        }

        if (letterSpacing <= 0.01f || text.Length == 1)
        {
            return paint.MeasureText(text);
        }

        var width = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            width += paint.MeasureText(text[i].ToString());
        }

        return width + (letterSpacing * (text.Length - 1));
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

    private static SKColor ParseTextColor(string colorHex)
    {
        if (!string.IsNullOrWhiteSpace(colorHex))
        {
            var value = colorHex.Trim();
            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                value = value[1..];
            }

            if (value.Length == 8)
            {
                value = value[2..];
            }

            if (value.Length == 6
                && byte.TryParse(value[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
                && byte.TryParse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
                && byte.TryParse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                return new SKColor(r, g, b);
            }
        }

        return SKColors.White;
    }
}
