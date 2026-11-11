// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AccessibilityInsights_WinUI.Services;

internal sealed record LiveHotkeyConfiguration(
    string Pause,
    string Record,
    string RunTests,
    string ActivateWindow,
    string MoveToParent,
    string MoveToFirstChild,
    string MoveToLastChild,
    string MoveToNextSibling,
    string MoveToPreviousSibling)
{
    public static LiveHotkeyConfiguration Default { get; } = new(
        "Shift + F5",
        "Shift + F7",
        "Shift + F8",
        "Shift + F9",
        "Control,Shift + F6",
        "Control,Shift + F7",
        "Control,Shift + F9",
        "Control,Shift + F8",
        "Control,Shift + F5");
}

internal sealed record LiveHotkeyActions(
    Action Pause,
    Action Record,
    Action RunTests,
    Action ActivateWindow,
    Action MoveToParent,
    Action MoveToFirstChild,
    Action MoveToLastChild,
    Action MoveToNextSibling,
    Action MoveToPreviousSibling);

internal sealed record HotkeyRegistrationFailure(string Name, string Gesture, string Error);

internal interface ILiveHotkeyService : IDisposable
{
    IReadOnlyList<HotkeyRegistrationFailure> Configure(
        nint window,
        LiveHotkeyConfiguration configuration,
        LiveHotkeyActions actions);
}

internal sealed class LiveHotkeyService : ILiveHotkeyService
{
    private const uint WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWindows = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const nuint SubclassId = 0xA11;

    private readonly Dictionary<int, Action> handlers = [];
    private readonly SubclassProcedure subclassProcedure;
    private nint window;
    private bool subclassInstalled;
    private bool disposed;

    public LiveHotkeyService() => subclassProcedure = WindowSubclassProc;

    public IReadOnlyList<HotkeyRegistrationFailure> Configure(
        nint targetWindow,
        LiveHotkeyConfiguration configuration,
        LiveHotkeyActions actions)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(actions);
        if (targetWindow == 0)
        {
            throw new ArgumentException("A valid application window is required.", nameof(targetWindow));
        }

        var registrations = new[]
        {
            new Registration(HotkeyId.Pause, "Pause", configuration.Pause, actions.Pause),
            new Registration(HotkeyId.Record, "Record", configuration.Record, actions.Record),
            new Registration(HotkeyId.RunTests, "RunTests", configuration.RunTests, actions.RunTests),
            new Registration(HotkeyId.ActivateWindow, "ActivateWindow", configuration.ActivateWindow, actions.ActivateWindow),
            new Registration(HotkeyId.MoveToParent, "MoveToParent", configuration.MoveToParent, actions.MoveToParent),
            new Registration(HotkeyId.MoveToFirstChild, "MoveToFirstChild", configuration.MoveToFirstChild, actions.MoveToFirstChild),
            new Registration(HotkeyId.MoveToLastChild, "MoveToLastChild", configuration.MoveToLastChild, actions.MoveToLastChild),
            new Registration(HotkeyId.MoveToNextSibling, "MoveToNextSibling", configuration.MoveToNextSibling, actions.MoveToNextSibling),
            new Registration(HotkeyId.MoveToPreviousSibling, "MoveToPreviousSibling", configuration.MoveToPreviousSibling, actions.MoveToPreviousSibling),
        };
        var parsed = registrations.Select(registration =>
            (Registration: registration, Gesture: ParseGesture(registration.Gesture))).ToArray();

        RemoveRegistrations();
        if (window != 0 && window != targetWindow && subclassInstalled)
        {
            RemoveWindowSubclass(window, subclassProcedure, SubclassId);
            subclassInstalled = false;
        }

        window = targetWindow;
        if (!subclassInstalled)
        {
            if (!SetWindowSubclass(window, subclassProcedure, SubclassId, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            subclassInstalled = true;
        }

        var failures = new List<HotkeyRegistrationFailure>();
        foreach (var item in parsed)
        {
            var id = (int)item.Registration.Id;
            if (RegisterHotKey(window, id, item.Gesture.Modifiers | ModNoRepeat, item.Gesture.VirtualKey))
            {
                handlers[id] = item.Registration.Action;
            }
            else
            {
                failures.Add(new HotkeyRegistrationFailure(
                    item.Registration.Name,
                    item.Registration.Gesture,
                    new Win32Exception(Marshal.GetLastWin32Error()).Message));
            }
        }

        return failures;
    }

    private nint WindowSubclassProc(
        nint targetWindow,
        uint message,
        nint wParam,
        nint lParam,
        nuint subclass,
        nuint referenceData)
    {
        if (message == WmHotkey && handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            try
            {
                handler();
            }
            catch (Exception exception)
            {
                Trace.TraceError("A live inspection hotkey failed: {0}", exception);
            }

            return 0;
        }

        return DefSubclassProc(targetWindow, message, wParam, lParam);
    }

    private void RemoveRegistrations()
    {
        if (window != 0)
        {
            foreach (var id in handlers.Keys.ToArray())
            {
                UnregisterHotKey(window, id);
            }
        }

        handlers.Clear();
    }

    private static HotkeyGesture ParseGesture(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var parts = value
            .Replace("+", ",", StringComparison.Ordinal)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new FormatException($"Invalid hotkey: {value}");
        }

        uint modifiers = 0;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            modifiers |= parts[index].ToUpperInvariant() switch
            {
                "ALT" => ModAlt,
                "CONTROL" or "CTRL" => ModControl,
                "SHIFT" => ModShift,
                "WINDOWS" or "WIN" => ModWindows,
                _ => throw new FormatException($"Unsupported hotkey modifier: {parts[index]}"),
            };
        }

        var key = parts[^1].Trim().ToUpperInvariant();
        uint virtualKey;
        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            virtualKey = key[0];
        }
        else if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
        }
        else
        {
            virtualKey = key switch
            {
                "BACKSPACE" => 0x08,
                "TAB" => 0x09,
                "ENTER" or "RETURN" => 0x0D,
                "ESCAPE" or "ESC" => 0x1B,
                "SPACE" => 0x20,
                "PAGEUP" or "PRIOR" => 0x21,
                "PAGEDOWN" or "NEXT" => 0x22,
                "END" => 0x23,
                "HOME" => 0x24,
                "LEFT" => 0x25,
                "UP" => 0x26,
                "RIGHT" => 0x27,
                "DOWN" => 0x28,
                "INSERT" => 0x2D,
                "DELETE" => 0x2E,
                _ => throw new FormatException($"Unsupported hotkey key: {parts[^1]}"),
            };
        }

        return new HotkeyGesture(modifiers, virtualKey);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        RemoveRegistrations();
        if (subclassInstalled && window != 0)
        {
            RemoveWindowSubclass(window, subclassProcedure, SubclassId);
        }

        subclassInstalled = false;
        window = 0;
        disposed = true;
    }

    private enum HotkeyId
    {
        Pause = 0xA110,
        Record,
        RunTests,
        ActivateWindow,
        MoveToParent,
        MoveToFirstChild,
        MoveToLastChild,
        MoveToNextSibling,
        MoveToPreviousSibling,
    }

    private sealed record Registration(HotkeyId Id, string Name, string Gesture, Action Action);
    private readonly record struct HotkeyGesture(uint Modifiers, uint VirtualKey);

    private delegate nint SubclassProcedure(
        nint window,
        uint message,
        nint wParam,
        nint lParam,
        nuint subclass,
        nuint referenceData);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint window,
        SubclassProcedure procedure,
        nuint subclass,
        nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure procedure, nuint subclass);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
}
