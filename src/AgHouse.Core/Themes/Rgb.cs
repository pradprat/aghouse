using System.Globalization;

namespace AgHouse.Core.Themes;

/// <summary>
/// Colour arithmetic on plain 0xRRGGBB (or 0xAARRGGBB) numbers, so it can be tested without WPF.
/// Contrast follows WCAG 2: relative luminance on linearised sRGB.
/// </summary>
public static class Rgb
{
    public static byte R(uint c) => (byte)(c >> 16);
    public static byte G(uint c) => (byte)(c >> 8);
    public static byte B(uint c) => (byte)c;
    public static byte A(uint c) => (byte)(c >> 24);

    public static uint From(int r, int g, int b) =>
        ((uint)Math.Clamp(r, 0, 255) << 16) | ((uint)Math.Clamp(g, 0, 255) << 8) | (uint)Math.Clamp(b, 0, 255);

    public static uint WithAlpha(uint rgb, byte alpha) => ((uint)alpha << 24) | (rgb & 0xFFFFFF);

    /// <summary>"#RRGGBB", "RRGGBB" or "#RGB". Null when it isn't one of those.</summary>
    public static uint? Parse(string? text)
    {
        var s = text?.Trim().TrimStart('#') ?? "";
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        return s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public static string ToHex(uint rgb) => "#" + (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

    /// <summary>
    /// The colour <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/>.
    /// t outside 0..1 extrapolates, clamped per channel.
    /// </summary>
    public static uint Mix(uint a, uint b, double t) => From(
        (int)Math.Round(R(a) + (R(b) - R(a)) * t),
        (int)Math.Round(G(a) + (G(b) - G(a)) * t),
        (int)Math.Round(B(a) + (B(b) - B(a)) * t));

    /// <summary><paramref name="top"/> at <paramref name="alpha"/> (0..1) laid over an opaque <paramref name="under"/>.</summary>
    public static uint Over(uint top, double alpha, uint under) => Mix(under, top, alpha);

    public static double Luminance(uint rgb)
    {
        static double Lin(byte c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(R(rgb)) + 0.7152 * Lin(G(rgb)) + 0.0722 * Lin(B(rgb));
    }

    /// <summary>WCAG contrast ratio, 1..21.</summary>
    public static double Contrast(uint a, uint b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>A light colour: dark text reads better on it than light text.</summary>
    public static bool IsLight(uint rgb) => Contrast(rgb, 0x000000) > Contrast(rgb, 0xFFFFFF);

    /// <summary>Close to grey: the channels differ by no more than <paramref name="tolerance"/>.</summary>
    public static bool IsNeutral(uint c, int tolerance = 6)
    {
        int r = R(c), g = G(c), b = B(c);
        return Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= tolerance;
    }

    /// <summary>
    /// <paramref name="ink"/> moved away from <paramref name="background"/> (toward black or white)
    /// just far enough to reach <paramref name="ratio"/>, or as far as it goes.
    /// </summary>
    public static uint EnsureContrast(uint ink, uint background, double ratio)
    {
        if (Contrast(ink, background) >= ratio) return ink;
        var target = IsLight(background) ? 0x000000u : 0xFFFFFFu;
        for (var t = 0.05; t < 1.0; t += 0.05)
        {
            var c = Mix(ink, target, t);
            if (Contrast(c, background) >= ratio) return c;
        }
        return target;
    }
}
