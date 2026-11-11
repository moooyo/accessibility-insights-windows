// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System.Globalization;

namespace AccessibilityInsights.Models;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static bool TryParse(string? text, out RgbColor color)
    {
        color = default;
        if (text is null)
        {
            return false;
        }

        var hex = text.Trim().AsSpan();
        if (hex.StartsWith("#"))
        {
            hex = hex[1..];
        }

        if (hex.Length != 6 ||
            !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        color = new((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

public static class ContrastCalculator
{
    public static double CalculateRatio(RgbColor first, RgbColor second)
    {
        var firstLuminance = GetRelativeLuminance(first);
        var secondLuminance = GetRelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05)
            / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    public static double GetRelativeLuminance(RgbColor color) =>
        0.2126 * Linearize(color.R) + 0.7152 * Linearize(color.G) + 0.0722 * Linearize(color.B);

    private static double Linearize(byte channel)
    {
        var srgb = channel / 255.0;
        return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }
}
