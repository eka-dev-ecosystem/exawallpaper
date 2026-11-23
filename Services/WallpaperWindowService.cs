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
    private IntPtr _insertAfter = IntPtr.Zero;
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

    public void LocateDesktopLayers(out IntPtr parentContainer, out IntPtr insertAfter, out IntPtr staticWallpaperWorker)
    {
        IntPtr pContainer = IntPtr.Zero;
        IntPtr insAfter = IntPtr.Zero;
        IntPtr staticWorker = IntPtr.Zero;

        IntPtr progman = FindProgman();
        if (progman != IntPtr.Zero)
        {
            // Send 0x052C to Progman
            NativeMethods.SendMessageTimeout(
                progman,
                0x052C,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.SendMessageTimeoutFlags.SMTO_NORMAL,
                1000,
                out _);

            // Windows 11 (24H2 / Build 26200+): SHELLDLL_DefView and static wallpaper WorkerW are inside Progman
            IntPtr shell = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shell != IntPtr.Zero)
            {
                pContainer = progman;
                insAfter = shell;
                staticWorker = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
                Log($"Located Win11 layers: Progman=0x{progman.ToInt64():X}, Shell=0x{shell.ToInt64():X}, StaticWorkerW=0x{staticWorker.ToInt64():X}");

                parentContainer = pContainer;
                insertAfter = insAfter;
                staticWallpaperWorker = staticWorker;
                return;
            }
        }

        // Windows 10: SHELLDLL_DefView is inside a top-level WorkerW, and spawned WorkerW is the next sibling
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            IntPtr shell = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shell != IntPtr.Zero)
            {
                IntPtr spawnedWorker = NativeMethods.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                if (spawnedWorker != IntPtr.Zero)
                {
                    pContainer = spawnedWorker;
                    insAfter = new IntPtr(NativeMethods.HWND_BOTTOM);
                    staticWorker = IntPtr.Zero;
                    Log($"Located Win10 layers: SpawnedWorkerW=0x{spawnedWorker.ToInt64():X}");
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);

        parentContainer = pContainer != IntPtr.Zero ? pContainer : progman;
        insertAfter = insAfter != IntPtr.Zero ? insAfter : new IntPtr(NativeMethods.HWND_BOTTOM);
        staticWallpaperWorker = staticWorker;
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

        LocateDesktopLayers(out _desktopParent, out _insertAfter, out _workerW);

        // Hide static wallpaper WorkerW so video wallpaper displays on desktop!
        if (_workerW != IntPtr.Zero && NativeMethods.IsWindow(_workerW))
        {
            NativeMethods.ShowWindow(_workerW, NativeMethods.SW_HIDE);
            Log($"Static wallpaper WorkerW 0x{_workerW.ToInt64():X} hidden.");
        }

        // Reparent window to desktop parent container (Progman in Win11, spawned WorkerW in Win10)
        if (_desktopParent != IntPtr.Zero)
        {
            long style = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE).ToInt64();
            style &= ~NativeMethods.WS_POPUP;
            style |= (NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPSIBLINGS);
            NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE, new IntPtr(style));

            IntPtr prevParent = NativeMethods.SetParent(hWnd, _desktopParent);
            Log($"SetParent called for hWnd: 0x{hWnd.ToInt64():X} -> parent: 0x{_desktopParent.ToInt64():X}, prevParent: 0x{prevParent.ToInt64():X}");
        }

        // Extended style
        long exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= (NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT);
        exStyle &= ~NativeMethods.WS_EX_LAYERED;
        NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

        var source = HwndSource.FromHwnd(hWnd);
        source?.AddHook(WndProc);

        var pt = new NativeMethods.POINT { X = x, Y = y };
        if (_desktopParent != IntPtr.Zero)
        {
            NativeMethods.MapWindowPoints(IntPtr.Zero, _desktopParent, ref pt, 1);
        }

        IntPtr insertPos = (_insertAfter != IntPtr.Zero && NativeMethods.IsWindow(_insertAfter))
            ? _insertAfter
            : new IntPtr(NativeMethods.HWND_BOTTOM);

        NativeMethods.SetWindowPos(
            hWnd,
            insertPos,
            pt.X,
            pt.Y,
            width,
            height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        Log($"Placed window 0x{hWnd.ToInt64():X} at ({pt.X},{pt.Y} {width}x{height}) inside parent 0x{_desktopParent.ToInt64():X} after 0x{insertPos.ToInt64():X}");

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
                        IntPtr pos = (_insertAfter != IntPtr.Zero && NativeMethods.IsWindow(_insertAfter))
                            ? _insertAfter
                            : new IntPtr(NativeMethods.HWND_BOTTOM);

                        NativeMethods.SetWindowPos(
                            h,
                            pos,
                            0, 0, 0, 0,
                            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                    }
                    else
                    {
                        _attachedHandles.RemoveAt(i);
                    }
                }
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

        if (_workerW != IntPtr.Zero && NativeMethods.IsWindow(_workerW))
        {
            NativeMethods.ShowWindow(_workerW, NativeMethods.SW_SHOW);
            NativeMethods.InvalidateRect(IntPtr.Zero, IntPtr.Zero, true);
            Log($"Static WorkerW 0x{_workerW.ToInt64():X} restored and desktop invalidated.");
        }
    }
}
