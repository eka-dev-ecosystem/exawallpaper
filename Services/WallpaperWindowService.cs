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

    public IntPtr FindDesktopParent()
    {
        IntPtr progman = FindProgman();
        if (progman != IntPtr.Zero)
        {
            IntPtr shell = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shell != IntPtr.Zero)
            {
                Log($"Found SHELLDLL_DefView inside Progman (0x{progman.ToInt64():X})");
                return progman;
            }
        }

        IntPtr shellHost = IntPtr.Zero;
        NativeMethods.EnumWindows((topWnd, lParam) =>
        {
            IntPtr s = NativeMethods.FindWindowEx(topWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (s != IntPtr.Zero)
            {
                shellHost = topWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (shellHost != IntPtr.Zero)
        {
            Log($"Found SHELLDLL_DefView inside WorkerW/Host (0x{shellHost.ToInt64():X})");
            return shellHost;
        }

        Log($"Fallback: using Progman 0x{progman.ToInt64():X}");
        return progman;
    }

    public IntPtr FindWorkerW(IntPtr desktopParent)
    {
        IntPtr workerw = IntPtr.Zero;
        if (desktopParent != IntPtr.Zero)
        {
            workerw = NativeMethods.FindWindowEx(desktopParent, IntPtr.Zero, "WorkerW", null);
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

        // 1. Locate the desktop parent window hosting SHELLDLL_DefView (Progman or WorkerW)
        if (_desktopParent == IntPtr.Zero || !NativeMethods.IsWindow(_desktopParent))
        {
            _desktopParent = FindDesktopParent();
        }
        Log($"Target desktop parent: 0x{_desktopParent.ToInt64():X}");

        // 2. Locate WorkerW responsible for rendering static wallpaper and hide it
        if (_workerW == IntPtr.Zero || !NativeMethods.IsWindow(_workerW))
        {
            _workerW = FindWorkerW(_desktopParent);
        }

        if (_workerW != IntPtr.Zero && NativeMethods.IsWindow(_workerW))
        {
            NativeMethods.ShowWindow(_workerW, NativeMethods.SW_HIDE);
            Log($"WorkerW 0x{_workerW.ToInt64():X} hidden.");
        }

        // 3. Reparent window to desktopParent (behind SHELLDLL_DefView!)
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
                _workerW = FindWorkerW(_desktopParent);
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
            _workerW = FindWorkerW(_desktopParent);
        }

        if (_workerW != IntPtr.Zero && NativeMethods.IsWindow(_workerW))
        {
            NativeMethods.ShowWindow(_workerW, NativeMethods.SW_SHOW);
            NativeMethods.InvalidateRect(IntPtr.Zero, IntPtr.Zero, true);
            Log($"WorkerW 0x{_workerW.ToInt64():X} restored and desktop invalidated.");
        }
    }
}
