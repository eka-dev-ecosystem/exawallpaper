using System;
using System.Text;
using System.Windows.Forms;
using System.Windows.Threading;

namespace ExaWallpaper.Services;

public class FullscreenDetectorService
{
    private readonly DispatcherTimer _timer;
    private bool _lastFullscreenState = false;
    private IntPtr _shellWindow;
    private IntPtr _progmanWindow;

    public event Action<bool>? FullscreenStateChanged;
    public bool IsFullscreenAppRunning => _lastFullscreenState;

    public FullscreenDetectorService(int checkIntervalMs = 1000)
    {
        _shellWindow = NativeMethods.GetShellWindow();
        _progmanWindow = NativeMethods.FindWindow("Progman", null);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(checkIntervalMs)
        };
        _timer.Tick += (s, e) => CheckForegroundWindow();
    }

    public void Start()
    {
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    private void CheckForegroundWindow()
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == _shellWindow || fg == _progmanWindow)
        {
            UpdateState(false);
            return;
        }

        if (NativeMethods.IsIconic(fg) || !NativeMethods.IsWindowVisible(fg))
        {
            UpdateState(false);
            return;
        }

        // Filter out Windows Taskbar and Start Menu
        var className = new StringBuilder(256);
        NativeMethods.GetClassName(fg, className, className.Capacity);
        string name = className.ToString();

        if (name == "Shell_TrayWnd" ||
            name == "Shell_SecondaryTrayWnd" ||
            name == "WorkerW" ||
            name == "Progman" ||
            name == "Windows.UI.Core.CoreWindow") // Start menu
        {
            UpdateState(false);
            return;
        }

        if (!NativeMethods.GetWindowRect(fg, out NativeMethods.RECT rect))
        {
            UpdateState(false);
            return;
        }

        // Get the monitor on which this foreground window resides
        Screen screen = Screen.FromHandle(fg);
        var bounds = screen.Bounds;

        // If the window covers or exceeds the screen resolution bounds
        bool isFullscreen = rect.Left <= bounds.Left &&
                             rect.Top <= bounds.Top &&
                             rect.Right >= bounds.Right &&
                             rect.Bottom >= bounds.Bottom;

        UpdateState(isFullscreen);
    }

    private void UpdateState(bool isFullscreen)
    {
        if (isFullscreen != _lastFullscreenState)
        {
            _lastFullscreenState = isFullscreen;
            FullscreenStateChanged?.Invoke(isFullscreen);
        }
    }
}
