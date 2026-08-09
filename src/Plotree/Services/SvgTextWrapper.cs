using System.Globalization;
using System.Text;
using System.Xml;

namespace Plotree.Services;

/// <summary>
/// Provides deterministic, Unicode-safe line wrapping for SVG text. The SVG renderer still
/// chooses the final glyph shapes, so callers must clip text to its allocated rectangle.
/// </summary>
public static class SvgTextWrapper
{
    /// <summary>
    /// Sanitizes a value for XML text while retaining every valid Unicode scalar value.
    /// Invalid XML characters and unpaired surrogates become the replacement character.
    /// </summary>
    public static string SanitizeXmlText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sanitized = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 < value.Length
                    && XmlConvert.IsXmlSurrogatePair(value[index + 1], character))
                {
                    sanitized.Append(character);
                    sanitized.Append(value[++index]);
                }
                else
                {
                    sanitized.Append('\uFFFD');
                }
            }
            else if (char.IsLowSurrogate(character))
            {
                sanitized.Append('\uFFFD');
            }
            else
            {
                sanitized.Append(XmlConvert.IsXmlChar(character) ? character : '\uFFFD');
            }
        }

        return sanitized.ToString();
    }

    /// <summary>
    /// Wraps text at grapheme boundaries. Explicit line breaks are retained, long unbroken
    /// words are split only between text elements, and a final ellipsis marks truncation.
    /// </summary>
    public static IReadOnlyList<string> Wrap(
        string? value,
        double availableWidth,
        int maxLines,
        double fontSize)
    {
        if (string.IsNullOrEmpty(value) || availableWidth <= 0 || maxLines <= 0 || fontSize <= 0)
        {
            return [];
        }

        var allLines = new List<string>();
        var normalized = SanitizeXmlText(value)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        foreach (var paragraph in normalized.Split('\n'))
        {
            AppendParagraphLines(allLines, paragraph, availableWidth, fontSize);
        }

        if (allLines.Count <= maxLines)
        {
            return allLines;
        }

        var result = allLines.Take(maxLines).ToList();
        result[^1] = WithEllipsis(result[^1], availableWidth, fontSize);
        return result;
    }

    /// <summary>Returns the deterministic width estimate used by <see cref="Wrap"/>.</summary>
    public static double EstimateWidth(string value, double fontSize) =>
        TextElements(value).Sum(element => EstimateElementWidth(element, fontSize));

    private static void AppendParagraphLines(
        List<string> lines,
        string paragraph,
        double availableWidth,
        double fontSize)
    {
        if (paragraph.Length == 0)
        {
            lines.Add(string.Empty);
            return;
        }

        var elements = TextElements(paragraph).ToList();
        var current = new List<string>();
        var currentWidth = 0d;
        var lastBreakIndex = -1;

        for (var index = 0; index < elements.Count;)
        {
            var element = elements[index];
            if (current.Count == 0 && IsWhitespace(element))
            {
                index++;
                continue;
            }

            var elementWidth = EstimateElementWidth(element, fontSize);
            if (current.Count == 0 || currentWidth + elementWidth <= availableWidth)
            {
                current.Add(element);
                currentWidth += elementWidth;
                if (IsWhitespace(element))
                {
                    lastBreakIndex = current.Count;
                }

                index++;
                continue;
            }

            if (lastBreakIndex >= 0)
            {
                lines.Add(JoinTrimmedEnd(current.Take(lastBreakIndex)));
                var remainder = current.Skip(lastBreakIndex).SkipWhile(IsWhitespace).ToList();
                current.Clear();
                current.AddRange(remainder);
                currentWidth = EstimateWidth(string.Concat(current), fontSize);
                lastBreakIndex = FindLastBreak(current);
                continue;
            }

            lines.Add(JoinTrimmedEnd(current));
            current.Clear();
            currentWidth = 0;
            lastBreakIndex = -1;
        }

        if (current.Count > 0)
        {
            lines.Add(JoinTrimmedEnd(current));
        }
    }

    private static string WithEllipsis(string line, double availableWidth, double fontSize)
    {
        const string ellipsis = "…";
        var elements = TextElements(line).ToList();
        while (elements.Count > 0
            && EstimateWidth(string.Concat(elements) + ellipsis, fontSize) > availableWidth)
        {
            elements.RemoveAt(elements.Count - 1);
        }

        return string.Concat(elements).TrimEnd() + ellipsis;
    }

    private static IEnumerable<string> TextElements(string value)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            yield return (string)enumerator.Current;
        }
    }

    private static int FindLastBreak(IReadOnlyList<string> elements)
    {
        for (var index = elements.Count - 1; index >= 0; index--)
        {
            if (IsWhitespace(elements[index]))
            {
                return index + 1;
            }
        }

        return -1;
    }

    private static string JoinTrimmedEnd(IEnumerable<string> elements) =>
        string.Concat(elements).TrimEnd();

    private static bool IsWhitespace(string element) =>
        element.All(char.IsWhiteSpace);

    private static double EstimateElementWidth(string element, double fontSize)
    {
        if (IsWhitespace(element))
        {
            return fontSize * 0.35;
        }

        var scalar = char.ConvertToUtf32(element, 0);
        if (scalar is >= 0x2E80 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF)
        {
            return fontSize;
        }

        var first = element[0];
        if (char.IsPunctuation(first))
        {
            return fontSize * 0.45;
        }

        if (first is 'i' or 'I' or 'l' or '|' or '!' or '1')
        {
            return fontSize * 0.35;
        }

        if (first is 'M' or 'W' or 'm' or 'w')
        {
            return fontSize * 0.85;
        }

        return fontSize * 0.6;
    }
}
