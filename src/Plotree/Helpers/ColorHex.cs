using Windows.UI;

namespace Plotree.Helpers;

/// <summary>Parses "#RGB", "#RRGGBB" or "#AARRGGBB" hex strings into colors.</summary>
public static class ColorHex
{
    public static Color? Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        var s = hex.TrimStart('#');
        try
        {
            return s.Length switch
            {
                3 => Color.FromArgb(
                    0xFF,
                    (byte)(Convert.ToByte(s[0..1], 16) * 17),
                    (byte)(Convert.ToByte(s[1..2], 16) * 17),
                    (byte)(Convert.ToByte(s[2..3], 16) * 17)),
                6 => Color.FromArgb(
                    0xFF,
                    Convert.ToByte(s[0..2], 16),
                    Convert.ToByte(s[2..4], 16),
                    Convert.ToByte(s[4..6], 16)),
                8 => Color.FromArgb(
                    Convert.ToByte(s[0..2], 16),
                    Convert.ToByte(s[2..4], 16),
                    Convert.ToByte(s[4..6], 16),
                    Convert.ToByte(s[6..8], 16)),
                _ => null,
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
