// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace AccessibilityInsights.Core;

public static class ColorContrast
{
    public static double Calculate(byte firstRed, byte firstGreen, byte firstBlue,
        byte secondRed, byte secondGreen, byte secondBlue)
    {
        var first = Luminance(firstRed, firstGreen, firstBlue);
        var second = Luminance(secondRed, secondGreen, secondBlue);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static double Luminance(byte red, byte green, byte blue) =>
        0.2126 * Linear(red) + 0.7152 * Linear(green) + 0.0722 * Linear(blue);

    private static double Linear(byte component)
    {
        var value = component / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
