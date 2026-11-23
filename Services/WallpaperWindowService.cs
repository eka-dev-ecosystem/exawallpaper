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
    private IntPtr _desktopParent = IntPtr.Zero;
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

    public IntPtr FindProgman()
    {
        IntPtr progman = NativeMethods.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            var thread = new Thread(() =>
            {
                IntPtr hDesk = NativeMethods.OpenDesktop("Default", 0, false, 0x01FF);
                if (hDesk != IntPtr.Zero && NativeMethods.SetThreadDesktop(hDesk))
                {
                    progman = NativeMethods.FindWindow("Progman", null);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }
        return progman;
    }

    public IntPtr FindDesktopWorkerW()
    {
        IntPtr progman = FindProgman();
        if (progman != IntPtr.Zero)
        {
            // Send 0x052C to Progman to instruct Windows Explorer to spawn WorkerW
            NativeMethods.SendMessageTimeout(
                progman,
                0x052C,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.SendMessageTimeoutFlags.SMTO_NORMAL,
                1000,
                out _);

            // 1. In Windows 11 (24H2 / Build 26200+), WorkerW is a child inside Progman!
            IntPtr childWorker = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
            if (childWorker != IntPtr.Zero)
            {
                Log($"Found child WorkerW inside Progman (Win11 style): 0x{childWorker.ToInt64():X}");
                return childWorker;
            }
        }

        // 2. In Windows 10, look for top-level WorkerW spawned behind SHELLDLL_DefView
        IntPtr workerw = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            IntPtr shell = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shell != IntPtr.Zero)
            {
                workerw = NativeMethods.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                if (workerw != IntPtr.Zero)
                {
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);

        if (workerw != IntPtr.Zero)
        {
            Log($"Found spawned WorkerW behind desktop view (Win10 style): 0x{workerw.ToInt64():X}");
            return workerw;
        }

        // 3. Fallback to Progman if no WorkerW found
        Log($"Fallback: using Progman 0x{progman.ToInt64():X}");
        return progman;
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

        // 1. Locate the desktop canvas window (WorkerW)
        if (_desktopParent == IntPtr.Zero || !NativeMethods.IsWindow(_desktopParent))
        {
            _desktopParent = FindDesktopWorkerW();
        }
        Log($"Target desktop parent: 0x{_desktopParent.ToInt64():X}");

        // 2. Ensure WorkerW is visible! (Never hide it, it is the canvas)
        if (_desktopParent != IntPtr.Zero && NativeMethods.IsWindow(_desktopParent))
        {
            NativeMethods.ShowWindow(_desktopParent, NativeMethods.SW_SHOW);
        }

        // 3. Reparent window to desktopParent (WorkerW)
        if (_desktopParent != IntPtr.Zero)
        {
            long style = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE).ToInt64();
            style &= ~NativeMethods.WS_POPUP;
            style |= (NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPSIBLINGS);
            NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE, new IntPtr(style));

            IntPtr prevParent = NativeMethods.SetParent(hWnd, _desktopParent);
            Log($"SetParent called for hWnd: 0x{hWnd.ToInt64():X} -> parent: 0x{_desktopParent.ToInt64():X}, prevParent: 0x{prevParent.ToInt64():X}");
        }

        // 4. Set window extended style as non-activating, transparent-to-mouse tool window
        long exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= (NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT);
        exStyle &= ~NativeMethods.WS_EX_LAYERED;
        NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

        // 5. Add WndProc hook for hit testing and mouse activation pass-through
        var source = HwndSource.FromHwnd(hWnd);
        source?.AddHook(WndProc);

        // 6. Map screen coordinates to parent-relative coordinates
        var pt = new NativeMethods.POINT { X = x, Y = y };
        if (_desktopParent != IntPtr.Zero)
        {
            NativeMethods.MapWindowPoints(IntPtr.Zero, _desktopParent, ref pt, 1);
        }

        // 7. Position window at HWND_BOTTOM inside desktopParent
        NativeMethods.SetWindowPos(
            hWnd,
            new IntPtr(NativeMethods.HWND_BOTTOM),
            pt.X,
            pt.Y,
            width,
            height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        Log($"Placed window 0x{hWnd.ToInt64():X} at HWND_BOTTOM ({pt.X},{pt.Y} {width}x{height}) inside parent 0x{_desktopParent.ToInt64():X}");

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

        try
        {
            NativeMethods.SetParent(hWnd, IntPtr.Zero);
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_HIDE);
        }
        catch { }
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

            if (_desktopParent != IntPtr.Zero && NativeMethods.IsWindow(_desktopParent) && !NativeMethods.IsWindowVisible(_desktopParent))
            {
                NativeMethods.ShowWindow(_desktopParent, NativeMethods.SW_SHOW);
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
            foreach (var h in _attachedHandles)
            {
                if (h != IntPtr.Zero && NativeMethods.IsWindow(h))
                {
                    try
                    {
                        NativeMethods.SetParent(h, IntPtr.Zero);
                        NativeMethods.ShowWindow(h, NativeMethods.SW_HIDE);
                    }
                    catch { }
                }
            }
            _attachedHandles.Clear();
        }

        if (_desktopParent != IntPtr.Zero && NativeMethods.IsWindow(_desktopParent))
        {
            NativeMethods.ShowWindow(_desktopParent, NativeMethods.SW_SHOW);
            NativeMethods.InvalidateRect(IntPtr.Zero, IntPtr.Zero, true);
            Log($"Desktop parent 0x{_desktopParent.ToInt64():X} restored and desktop invalidated.");
        }
    }
}
