using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;
using ExaWallpaper.Models;

namespace ExaWallpaper.Services;

public class WallpaperWindowService
{
    private readonly List<IntPtr> _attachedHandles = new();
    private readonly object _handleLock = new();
    private IntPtr _workerW = IntPtr.Zero;
    private DispatcherTimer? _zOrderTimer;

    private static void Log(string message)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string logFolder = Path.Combine(appData, "ExaWallpaper");
            Directory.CreateDirectory(logFolder);
            string logFile = Path.Combine(logFolder, "debug.log");
            File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: [WallpaperWindowService] {message}\n");
        }
        catch { }
    }

    public IntPtr FindWorkerW()
    {
        IntPtr workerw = IntPtr.Zero;
        IntPtr progman = NativeMethods.FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            workerw = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        }

        if (workerw == IntPtr.Zero)
        {
            NativeMethods.EnumWindows((topWnd, lParam) =>
            {
                IntPtr s = NativeMethods.FindWindowEx(topWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (s != IntPtr.Zero)
                {
                    IntPtr next = NativeMethods.FindWindowEx(IntPtr.Zero, topWnd, "WorkerW", null);
                    if (next != IntPtr.Zero)
                    {
                        workerw = next;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
        }

        if (workerw == IntPtr.Zero)
        {
            var thread = new Thread(() =>
            {
                IntPtr hDesk = NativeMethods.OpenDesktop("Default", 0, false, 0x01FF);
                if (hDesk != IntPtr.Zero && NativeMethods.SetThreadDesktop(hDesk))
                {
                    IntPtr p = NativeMethods.FindWindow("Progman", null);
                    if (p != IntPtr.Zero)
                    {
                        workerw = NativeMethods.FindWindowEx(p, IntPtr.Zero, "WorkerW", null);
                    }
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        return workerw;
    }

    public void AttachToDesktop(Window window, int x, int y, int width, int height)
    {
        var helper = new WindowInteropHelper(window);
        IntPtr hWnd = helper.EnsureHandle();
        if (hWnd == IntPtr.Zero) return;

        lock (_handleLock)
        {
            if (!_attachedHandles.Contains(hWnd))
            {
                _attachedHandles.Add(hWnd);
            }
        }

        Log($"AttachToDesktop starting for hWnd: 0x{hWnd.ToInt64():X} at {x},{y} {width}x{height}");

        // 1. Locate the WorkerW window responsible for rendering the static desktop wallpaper
        if (_workerW == IntPtr.Zero || !NativeMethods.IsWindow(_workerW))
        {
            _workerW = FindWorkerW();
        }

        if (_workerW != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(_workerW, NativeMethods.SW_HIDE);
        }

        // 2. Set window style as a non-activating, transparent-to-mouse tool window at HWND_BOTTOM
        // WS_EX_TRANSPARENT ensures mouse input passes directly through to desktop icons and context menus!
        long exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= (NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT);
        exStyle &= ~NativeMethods.WS_EX_LAYERED;
        NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

        // 3. Add WndProc hook to ignore mouse activations and pass hit-tests to underlying desktop
        var source = HwndSource.FromHwnd(hWnd);
        source?.AddHook(WndProc);

        // 4. Position window at HWND_BOTTOM
        window.Left = x;
        window.Top = y;
        window.Width = width;
        window.Height = height;

        NativeMethods.SetWindowPos(
            hWnd,
            new IntPtr(NativeMethods.HWND_BOTTOM),
            x,
            y,
            width,
            height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        EnsureTimerStarted();
    }

    public void DetachWindow(Window window)
    {
        var helper = new WindowInteropHelper(window);
        IntPtr hWnd = helper.Handle;
        if (hWnd == IntPtr.Zero) return;

        lock (_handleLock)
        {
            _attachedHandles.Remove(hWnd);
        }
    }

    private void EnsureTimerStarted()
    {
        if (_zOrderTimer != null) return;

        _zOrderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _zOrderTimer.Tick += (s, e) =>
        {
            lock (_handleLock)
            {
                for (int i = _attachedHandles.Count - 1; i >= 0; i--)
                {
                    IntPtr h = _attachedHandles[i];
                    if (h != IntPtr.Zero && NativeMethods.IsWindow(h))
                    {
                        NativeMethods.SetWindowPos(
                            h,
                            new IntPtr(NativeMethods.HWND_BOTTOM),
                            0, 0, 0, 0,
                            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                    }
                    else
                    {
                        _attachedHandles.RemoveAt(i);
                    }
                }
            }

            if (_workerW == IntPtr.Zero || !NativeMethods.IsWindow(_workerW))
            {
                _workerW = FindWorkerW();
            }

            if (_workerW != IntPtr.Zero && NativeMethods.IsWindow(_workerW) && NativeMethods.IsWindowVisible(_workerW))
            {
                NativeMethods.ShowWindow(_workerW, NativeMethods.SW_HIDE);
            }
        };
        _zOrderTimer.Start();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_NCHITTEST)
        {
            handled = true;
            return new IntPtr(NativeMethods.HTTRANSPARENT);
        }

        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }

        return IntPtr.Zero;
    }

    public void RestoreDesktop()
    {
        Log("RestoreDesktop called.");
        _zOrderTimer?.Stop();
        _zOrderTimer = null;

        lock (_handleLock)
        {
            _attachedHandles.Clear();
        }

        if (_workerW == IntPtr.Zero || !NativeMethods.IsWindow(_workerW))
        {
            _workerW = FindWorkerW();
        }

        if (_workerW != IntPtr.Zero && NativeMethods.IsWindow(_workerW))
        {
            NativeMethods.ShowWindow(_workerW, NativeMethods.SW_SHOW);
            NativeMethods.InvalidateRect(IntPtr.Zero, IntPtr.Zero, true);
            Log($"WorkerW 0x{_workerW.ToInt64():X} restored and desktop invalidated.");
        }
    }
}
