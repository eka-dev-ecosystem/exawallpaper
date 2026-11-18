using System;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using ExaWallpaper.Models;

namespace ExaWallpaper.Services;

public class WallpaperWindowService
{
    private IntPtr _workerW = IntPtr.Zero;

    public IntPtr GetWorkerWHandle()
    {
        if (_workerW != IntPtr.Zero)
        {
            return _workerW;
        }

        IntPtr progman = NativeMethods.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        // Send 0x052C to Progman to spawn a WorkerW behind desktop icons (SHELLDLL_DefView)
        IntPtr result = IntPtr.Zero;
        NativeMethods.SendMessageTimeout(
            progman,
            NativeMethods.WM_SPAWN_WORKER,
            new IntPtr(0xD),
            new IntPtr(0x1),
            NativeMethods.SendMessageTimeoutFlags.SMTO_NORMAL,
            1000,
            out result);

        IntPtr foundWorkerW = IntPtr.Zero;

        // Enumerate windows to find the WorkerW window directly behind SHELLDLL_DefView
        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            IntPtr shellDllDefView = NativeMethods.FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellDllDefView != IntPtr.Zero)
            {
                // The WorkerW window created for desktop background is next in Z-order
                foundWorkerW = NativeMethods.FindWindowEx(IntPtr.Zero, hWnd, "WorkerW", null);
                if (foundWorkerW != IntPtr.Zero)
                {
                    return false; // Found, stop enumerating
                }
            }
            return true;
        }, IntPtr.Zero);

        _workerW = foundWorkerW != IntPtr.Zero ? foundWorkerW : progman;
        return _workerW;
    }

    public void AttachToDesktop(Window window, AppConfig config)
    {
        var helper = new WindowInteropHelper(window);
        IntPtr hWnd = helper.Handle;

        IntPtr targetWorkerW = GetWorkerWHandle();
        if (targetWorkerW != IntPtr.Zero)
        {
            NativeMethods.SetParent(hWnd, targetWorkerW);

            long style = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE).ToInt64();
            style |= NativeMethods.WS_CHILD;
            NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE, new IntPtr(style));
        }

        UpdateWindowBounds(window, config);
    }

    public void UpdateWindowBounds(Window window, AppConfig config)
    {
        var helper = new WindowInteropHelper(window);
        IntPtr hWnd = helper.Handle;

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
            // VirtualScreen covers all connected displays
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
            IntPtr.Zero,
            x,
            y,
            width,
            height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }
}
