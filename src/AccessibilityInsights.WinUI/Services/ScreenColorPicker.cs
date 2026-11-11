// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AccessibilityInsights_WinUI.Services;

public sealed class ScreenColorPicker : IScreenColorPicker, IAsyncDisposable
{
    private readonly object gate = new();
    private CancellationTokenSource? activeCancellation;
    private TaskCompletionSource? activeCleanup;
    private bool disposed;

    public async Task<RgbColor?> PickAsync(CancellationToken cancellationToken)
    {
        if (!App.Window.DispatcherQueue.HasThreadAccess)
        {
            throw new InvalidOperationException("Screen color selection must begin on the owning UI thread.");
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (activeCancellation is not null)
            {
                throw new InvalidOperationException("Screen color selection is already active.");
            }

            activeCancellation = cancellation;
            activeCleanup = cleanup;
        }

        nint mouseHook = 0;
        nint keyboardHook = 0;
        var selection = new TaskCompletionSource<NativePoint?>(TaskCreationOptions.RunContinuationsAsynchronously);
        NativePoint? clickedPoint = null;
        HookCallback mouseCallback = (code, message, data) =>
        {
            if (code >= 0 && message == 0x0201)
            {
                clickedPoint = Marshal.PtrToStructure<NativePoint>(data);
                return 1;
            }

            if (code >= 0 && message == 0x0202 && clickedPoint is not null)
            {
                selection.TrySetResult(clickedPoint);
                return 1;
            }

            return CallNextHookEx(mouseHook, code, message, data);
        };
        HookCallback keyboardCallback = (code, message, data) =>
        {
            if (code < 0)
            {
                return CallNextHookEx(keyboardHook, code, message, data);
            }

            var key = Marshal.ReadInt32(data);
            if (key is 0x1B or 0x0D or >= 0x25 and <= 0x28)
            {
                if (message is 0x0100 or 0x0104)
                {
                    if (key == 0x1B)
                    {
                        selection.TrySetResult(null);
                    }
                    else if (!GetCursorPos(out var point))
                    {
                        selection.TrySetException(new Win32Exception(Marshal.GetLastWin32Error()));
                    }
                    else if (key == 0x0D)
                    {
                        selection.TrySetResult(point);
                    }
                    else
                    {
                        var step = (GetAsyncKeyState(0x10) & 0x8000) != 0 ? 10 : 1;
                        var x = point.X + (key == 0x25 ? -step : key == 0x27 ? step : 0);
                        var y = point.Y + (key == 0x26 ? -step : key == 0x28 ? step : 0);
                        if (!SetCursorPos(x, y))
                        {
                            selection.TrySetException(new Win32Exception(Marshal.GetLastWin32Error()));
                        }
                    }
                }

                return 1;
            }

            return CallNextHookEx(keyboardHook, code, message, data);
        };
        NativePoint? selectedPoint;
        try
        {
            var module = GetModuleHandleW(null);
            mouseHook = SetWindowsHookExW(14, mouseCallback, module, 0);
            if (mouseHook == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            keyboardHook = SetWindowsHookExW(13, keyboardCallback, module, 0);
            if (keyboardHook == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            using var registration = cancellation.Token.Register(() => selection.TrySetResult(null));
            selectedPoint = await selection.Task;
        }
        finally
        {
            if (mouseHook != 0 && !UnhookWindowsHookEx(mouseHook))
            {
                System.Diagnostics.Trace.TraceError("Mouse hook cleanup failed: {0}", Marshal.GetLastWin32Error());
            }

            if (keyboardHook != 0 && !UnhookWindowsHookEx(keyboardHook))
            {
                System.Diagnostics.Trace.TraceError("Keyboard hook cleanup failed: {0}", Marshal.GetLastWin32Error());
            }

            GC.KeepAlive(mouseCallback);
            GC.KeepAlive(keyboardCallback);
            lock (gate)
            {
                activeCancellation = null;
                activeCleanup = null;
            }

            cleanup.TrySetResult();
        }

        return selectedPoint is { } selected ? ReadPixel(selected) : null;
    }

    private static RgbColor ReadPixel(NativePoint point)
    {
        var dc = GetDC(0);
        if (dc == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var value = GetPixel(dc, point.X, point.Y);
            if (value == uint.MaxValue)
            {
                throw new InvalidOperationException("The selected screen pixel could not be read.");
            }

            return new RgbColor((byte)value, (byte)(value >> 8), (byte)(value >> 16));
        }
        finally
        {
            if (ReleaseDC(0, dc) == 0)
            {
                System.Diagnostics.Trace.TraceError("Screen device context cleanup failed.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? cleanup;
        lock (gate)
        {
            disposed = true;
            activeCancellation?.Cancel();
            cleanup = activeCleanup?.Task;
        }

        if (cleanup is not null)
        {
            await cleanup;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private delegate nint HookCallback(int code, nint message, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExW(int hookId, HookCallback callback, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? moduleName);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetDC(nint window);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(nint dc, int x, int y);
}
