using System;
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
    private IntPtr _windowHandle = IntPtr.Zero;
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
            // Fallback: dedicated thread connected to "Default" desktop
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

    public void AttachToDesktop(Window window, AppConfig config)
    {
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.EnsureHandle();

        Log($"AttachToDesktop starting for hWnd: 0x{_windowHandle.ToInt64():X}");

        // 1. Locate the WorkerW window responsible for rendering the static desktop wallpaper
        _workerW = FindWorkerW();
        if (_workerW != IntPtr.Zero)
        {
            // Hide static wallpaper so our WPF window at HWND_BOTTOM shines through!
            NativeMethods.ShowWindow(_workerW, NativeMethods.SW_HIDE);
            Log($"WorkerW 0x{_workerW.ToInt64():X} found and hidden.");
        }
        else
        {
            Log("Warning: WorkerW not found at startup; will attempt discovery during maintenance ticks.");
        }

        // 2. Set window style as a non-activating tool window
        // Do NOT call SetParent - keeping it as a top-level window preserves 100% DirectX hardware acceleration
        // and eliminates cross-process reparenting bugs and mouse click interception issues.
        long exStyle = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= (NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        // Ensure WS_EX_LAYERED is removed so video playback renders with full hardware direct flip
        exStyle &= ~NativeMethods.WS_EX_LAYERED;
        NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

        // 3. Add WndProc hook to ignore mouse activations
        var source = HwndSource.FromHwnd(_windowHandle);
        source?.AddHook(WndProc);

        // 4. Position window at HWND_BOTTOM
        UpdateWindowBounds(window, config);

        // 5. Start lightweight maintenance timer to guarantee HWND_BOTTOM Z-order and keep WorkerW hidden
        _zOrderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _zOrderTimer.Tick += (s, e) =>
        {
            if (_windowHandle != IntPtr.Zero && NativeMethods.IsWindow(_windowHandle))
            {
                NativeMethods.SetWindowPos(
                    _windowHandle,
                    new IntPtr(NativeMethods.HWND_BOTTOM),
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }

            if (_workerW == IntPtr.Zero || !NativeMethods.IsWindow(_workerW))
            {
                _workerW = FindWorkerW();
                if (_workerW != IntPtr.Zero)
                {
                    Log($"WorkerW 0x{_workerW.ToInt64():X} discovered dynamically.");
                }
            }

            if (_workerW != IntPtr.Zero && NativeMethods.IsWindow(_workerW) && NativeMethods.IsWindowVisible(_workerW))
            {
                NativeMethods.ShowWindow(_workerW, NativeMethods.SW_HIDE);
            }
        };
        _zOrderTimer.Start();

        Log("AttachToDesktop completed successfully.");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }

        return IntPtr.Zero;
    }

    public void UpdateWindowBounds(Window window, AppConfig config)
    {
        var helper = new WindowInteropHelper(window);
        IntPtr hWnd = helper.Handle;
        if (hWnd == IntPtr.Zero) return;

        int x, y, width, height;

        if (config.MultiMonitorMode == "PrimaryOnly")
        {
            var primary = Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
            x = primary.X;
            y = primary.Y;
            width = primary.Width;
            height = primary.Height;
        }
        else
        {
            var virtualScreen = SystemInformation.VirtualScreen;
            x = virtualScreen.X;
            y = virtualScreen.Y;
            width = virtualScreen.Width;
            height = virtualScreen.Height;
        }

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

        Log($"UpdateWindowBounds applied: {x},{y} {width}x{height} at HWND_BOTTOM");
    }

    public void RestoreDesktop()
    {
        Log("RestoreDesktop called.");
        _zOrderTimer?.Stop();
        _zOrderTimer = null;

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
