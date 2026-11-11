// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using AccessibilityInsights.Models;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AccessibilityInsights.Services;

public sealed class GlobalHotkeys : IDisposable
{
    private readonly nint window;
    private readonly SubclassProcedure procedure;
    private Dictionary<int, (Hotkey Key, Action Action)> registrations = [];
    private bool disposed;

    public GlobalHotkeys(nint window)
    {
        this.window = window;
        procedure = WindowProcedure;
        if (!SetWindowSubclass(window, procedure, 0x414931, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to initialize global hotkeys.");
        }
    }

    public void Configure(IReadOnlyList<(Hotkey Key, Action Action)> keys)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (keys.Select(item => item.Key).Distinct().Count() != keys.Count)
        {
            throw new ArgumentException("Global hotkeys must be distinct.");
        }

        var previous = registrations;
        UnregisterAll();
        var next = new Dictionary<int, (Hotkey Key, Action Action)>();
        try
        {
            for (var index = 0; index < keys.Count; index++)
            {
                var id = 0x4140 + index;
                var item = keys[index];
                if (!RegisterHotKey(window, id, item.Key.Modifiers | 0x4000, item.Key.Key))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "A global hotkey is already in use.");
                }
                next.Add(id, item);
            }
            registrations = next;
        }
        catch (Win32Exception)
        {
            foreach (var id in next.Keys) { UnregisterHotKey(window, id); }
            foreach (var item in previous)
            {
                if (!RegisterHotKey(window, item.Key, item.Value.Key.Modifiers | 0x4000, item.Value.Key.Key))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to restore the previous global hotkeys.");
                }
            }
            registrations = previous;
            throw;
        }
    }

    private nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x0312 && registrations.TryGetValue((int)wParam, out var item))
        {
            item.Action();
            return 0;
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void UnregisterAll()
    {
        foreach (var id in registrations.Keys)
        {
            if (!UnregisterHotKey(window, id))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to unregister a global hotkey.");
            }
        }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        UnregisterAll();
        registrations.Clear();
        if (!RemoveWindowSubclass(window, procedure, 0x414931))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to remove the hotkey window subclass.");
        }
        disposed = true;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hwnd, SubclassProcedure procedure, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProcedure procedure, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
}
