// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AccessibilityInsights.Core;

internal sealed class NativeHighlightService : IDisposable
{
    private const uint ExecuteMessage = 0x8001;
    private const uint WmEraseBackground = 0x0014;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;
    private const int MouseActivateNoActivate = 3;
    private const int ShowHide = 0;
    private const int ShowNoActivate = 8;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint WsPopup = 0x80000000;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExNoActivate = 0x08000000;
    private const uint LayeredWindowAlpha = 0x00000002;
    private const uint GetHighContrast = 0x0042;
    private const uint HighContrastEnabled = 0x00000001;
    private const int SystemHotlightColor = 26;
    private const int BorderGap = 2;
    private const int BorderWidth = 4;
    private static readonly nint TopmostWindow = new(-1);
    private static readonly nint MessageOnlyWindow = new(-3);

    private readonly ConcurrentQueue<Action> commands = new();
    private readonly ManualResetEventSlim ready = new();
    private readonly Thread thread;
    private readonly WindowProcedure windowProcedure;
    private readonly string className = $"AccessibilityInsightsHighlight-{Guid.NewGuid():N}";
    private readonly nint[] borderWindows = new nint[4];
    private nint commandWindow;
    private nint brush;
    private nint module;
    private Exception? startupException;
    private int managedThreadId;
    private bool disposed;

    public NativeHighlightService()
    {
        windowProcedure = WindowProc;
        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Accessibility Insights highlighter",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        ready.Wait();
        if (startupException is not null)
        {
            thread.Join();
            ready.Dispose();
            disposed = true;
            throw new InvalidOperationException("The native highlighter could not be initialized.", startupException);
        }
    }

    public void Show(ScreenRectangle bounds)
    {
        if (bounds.IsEmpty)
        {
            Hide();
            return;
        }

        Invoke(() => ShowCore(bounds));
    }

    public void Hide()
    {
        if (!disposed)
        {
            Invoke(HideCore);
        }
    }

    private void Run()
    {
        managedThreadId = Environment.CurrentManagedThreadId;
        try
        {
            module = GetModuleHandle(null);
            brush = CreateSolidBrush(GetHighlightColor());
            if (brush == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                Instance = module,
                WindowProcedure = windowProcedure,
                BackgroundBrush = brush,
                ClassName = className,
            };
            if (RegisterClassEx(ref windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            commandWindow = CreateWindowEx(
                0,
                className,
                string.Empty,
                0,
                0,
                0,
                0,
                0,
                MessageOnlyWindow,
                0,
                module,
                0);
            if (commandWindow == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            for (var index = 0; index < borderWindows.Length; index++)
            {
                borderWindows[index] = CreateWindowEx(
                    WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate,
                    className,
                    string.Empty,
                    WsPopup,
                    0,
                    0,
                    1,
                    1,
                    0,
                    0,
                    module,
                    0);
                if (borderWindows[index] == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                if (!SetLayeredWindowAttributes(borderWindows[index], 0, byte.MaxValue, LayeredWindowAlpha))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }

            ready.Set();
            while (true)
            {
                var result = GetMessage(out var message, 0, 0, 0);
                if (result == 0)
                {
                    break;
                }

                if (result < 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            startupException = exception;
            ready.Set();
        }
        finally
        {
            foreach (var window in borderWindows)
            {
                if (window != 0)
                {
                    DestroyWindow(window);
                }
            }

            if (commandWindow != 0)
            {
                DestroyWindow(commandWindow);
            }

            if (module != 0)
            {
                UnregisterClass(className, module);
            }

            if (brush != 0)
            {
                DeleteObject(brush);
            }
        }
    }

    private nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case ExecuteMessage:
                while (commands.TryDequeue(out var command))
                {
                    command();
                }

                return 0;
            case WmNcHitTest:
                return HtTransparent;
            case WmMouseActivate:
                return MouseActivateNoActivate;
            case WmEraseBackground:
                if (GetClientRect(window, out var rectangle))
                {
                    FillRect(wParam, ref rectangle, brush);
                }

                return 1;
            default:
                return DefWindowProc(window, message, wParam, lParam);
        }
    }

    private void ShowCore(ScreenRectangle bounds)
    {
        var thickness = BorderWidth + 2;
        var outerLeft = bounds.X - BorderGap - thickness;
        var outerTop = bounds.Y - BorderGap - thickness;
        var outerWidth = bounds.Width + ((BorderGap + thickness) * 2);
        var outerHeight = bounds.Height + ((BorderGap + thickness) * 2);

        SetBorderPosition(borderWindows[0], outerLeft, bounds.Y - BorderGap, thickness, bounds.Height + (BorderGap * 2));
        SetBorderPosition(borderWindows[1], outerLeft, outerTop, outerWidth, thickness);
        SetBorderPosition(borderWindows[2], bounds.X + bounds.Width + BorderGap, bounds.Y - BorderGap, thickness, bounds.Height + (BorderGap * 2));
        SetBorderPosition(borderWindows[3], outerLeft, bounds.Y + bounds.Height + BorderGap, outerWidth, thickness);
    }

    private static void SetBorderPosition(nint window, int x, int y, int width, int height)
    {
        if (!SetWindowPos(window, TopmostWindow, x, y, Math.Max(1, width), Math.Max(1, height), SwpNoActivate | SwpShowWindow))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        InvalidateRect(window, 0, true);
        UpdateWindow(window);
        ShowWindow(window, ShowNoActivate);
    }

    private void HideCore()
    {
        foreach (var window in borderWindows)
        {
            if (window != 0)
            {
                ShowWindow(window, ShowHide);
            }
        }
    }

    private void Invoke(Action action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId == managedThreadId)
        {
            action();
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Enqueue(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });

        if (!PostMessage(commandWindow, ExecuteMessage, 0, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        completion.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Invoke(() =>
        {
            HideCore();
            PostQuitMessage(0);
        });
        disposed = true;
        thread.Join();
        ready.Dispose();
    }

    private static uint ToColorRef(byte red, byte green, byte blue) =>
        (uint)(red | (green << 8) | (blue << 16));

    private static uint GetHighlightColor()
    {
        var highContrast = new HighContrast
        {
            Size = (uint)Marshal.SizeOf<HighContrast>(),
        };
        return SystemParametersInfo(GetHighContrast, highContrast.Size, ref highContrast, 0) &&
            (highContrast.Flags & HighContrastEnabled) != 0
            ? GetSysColor(SystemHotlightColor)
            : ToColorRef(0x0C, 0x51, 0x86);
    }

    private delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public WindowProcedure WindowProcedure;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint BackgroundBrush;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct HighContrast
    {
        public uint Size;
        public uint Flags;
        public nint DefaultScheme;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, nint window, uint minimumMessage, uint maximumMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(nint window, nint rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint deviceContext, ref NativeRectangle rectangle, nint brush);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        ref HighContrast value,
        uint update);

    [DllImport("user32.dll")]
    private static extern uint GetSysColor(int index);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);
}
