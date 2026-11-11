// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using AccessibilityInsights.Models;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AccessibilityInsights.Services;

public sealed class NativeHighlighter : IDisposable
{
    private readonly List<nint> windows = [];

    public void Show(ElementBounds bounds)
    {
        Hide();
        if (bounds.Width <= 0 || bounds.Height <= 0) { return; }
        var rectangles = new[]
        {
            new ElementBounds(bounds.X - 4, bounds.Y - 4, bounds.Width + 8, 4),
            new ElementBounds(bounds.X - 4, bounds.Y + bounds.Height, bounds.Width + 8, 4),
            new ElementBounds(bounds.X - 4, bounds.Y, 4, bounds.Height),
            new ElementBounds(bounds.X + bounds.Width, bounds.Y, 4, bounds.Height),
        };
        try
        {
            foreach (var rectangle in rectangles)
            {
                // Nonactivating, click-through STATIC windows keep UIA focus on the target.
                var hwnd = CreateWindowExW(0x08000000 | 0x00000080 | 0x00000020 | 0x00080000, "STATIC", "",
                    0x80000000 | 0x00800000 | 6, rectangle.X, rectangle.Y, rectangle.Width,
                    rectangle.Height, 0, 0, 0, 0);
                if (hwnd == 0) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
                windows.Add(hwnd);
                if (!SetLayeredWindowAttributes(hwnd, 0, 255, 2))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                if (!SetWindowPos(hwnd, -1, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, 0x0010 | 0x0040))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }
        catch
        {
            Hide();
            throw;
        }
    }

    public void Hide()
    {
        foreach (var hwnd in windows)
        {
            if (!DestroyWindow(hwnd)) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
        }
        windows.Clear();
    }

    public void Dispose() => Hide();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string title,
        uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);
}
