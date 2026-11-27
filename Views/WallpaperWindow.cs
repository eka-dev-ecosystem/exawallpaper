using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ExaWallpaper.Services;

namespace ExaWallpaper.Views;

public class WallpaperWindow : IDisposable
{
    private readonly Form _form;
    private readonly WebView2 _webView;
    private bool _isMuted = true;
    private double _volume = 0.5;
    private string _stretchMode = "UniformToFill";
    private string? _currentVideoPath;
    private bool _isInitialized;
    private bool _isDisposed;
    private IntPtr _cachedHandle;

    public IntPtr Handle =>
        (!_isDisposed && !_form.IsDisposed && !_form.Disposing && _form.IsHandleCreated)
            ? _form.Handle
            : IntPtr.Zero;

    internal IntPtr LastKnownHandle => _cachedHandle;

    public bool IsAlive =>
        !_isDisposed &&
        !_form.IsDisposed &&
        !_form.Disposing &&
        _form.IsHandleCreated &&
        _cachedHandle != IntPtr.Zero &&
        NativeMethods.IsWindow(_cachedHandle);

    private CoreWebView2? SafeCoreWebView =>
        (!_isDisposed && !_form.IsDisposed && !_webView.IsDisposed) ? _webView.CoreWebView2 : null;

    private static void Log(string message)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string logFolder = Path.Combine(appData, "ExaWallpaper");
            Directory.CreateDirectory(logFolder);
            string logFile = Path.Combine(logFolder, "debug.log");
            File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: [WallpaperWindow] {message}\n");
        }
        catch { }
    }

    private class TransparentForm : Form
    {
        public TransparentForm()
        {
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= (int)(NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT);
                cp.ExStyle &= ~(int)(NativeMethods.WS_EX_APPWINDOW | NativeMethods.WS_EX_CONTROLPARENT);
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_NCHITTEST)
            {
                m.Result = (IntPtr)NativeMethods.HTTRANSPARENT;
                return;
            }
            if (m.Msg == NativeMethods.WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)NativeMethods.MA_NOACTIVATE;
                return;
            }
            if (m.Msg == NativeMethods.WM_ACTIVATE && (m.WParam.ToInt64() & 0xFFFF) != 0)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == NativeMethods.WM_SETFOCUS)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == NativeMethods.WM_NCACTIVATE && m.WParam != IntPtr.Zero)
            {
                m.WParam = IntPtr.Zero;
            }
            if (m.Msg == NativeMethods.WM_WINDOWPOSCHANGING && m.LParam != IntPtr.Zero)
            {
                var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(m.LParam);
                pos.flags |= NativeMethods.SWP_NOACTIVATE;
                Marshal.StructureToPtr(pos, m.LParam, false);
            }
            base.WndProc(ref m);
        }
    }

    public WallpaperWindow(IntPtr parentHwnd, IntPtr insertAfterHwnd, int x, int y, int width, int height)
    {
        Log($"Creating WallpaperWindow at ({x},{y} {width}x{height}) for parent 0x{parentHwnd.ToInt64():X}");

        _form = new TransparentForm
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(x, y),
            Size = new Size(width, height),
            ShowInTaskbar = false,
            ControlBox = false,
            MaximizeBox = false,
            MinimizeBox = false,
            Text = string.Empty,
            BackColor = Color.Black
        };

        // Create native HWND in hidden state first so it NEVER appears as a floating top-level window in Alt+Tab
        IntPtr hWnd = _form.Handle;
        _cachedHandle = hWnd;
        _form.Disposed += (s, e) => _isDisposed = true;

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.Black,
            TabStop = false
        };
        _form.Controls.Add(_webView);

        if (parentHwnd != IntPtr.Zero && NativeMethods.IsWindow(parentHwnd))
        {
            try { NativeMethods.SetThreadDpiHostingBehavior(NativeMethods.DPI_HOSTING_BEHAVIOR_MIXED); } catch { }

            // 1. Convert style to child BEFORE showing so Shell/Alt+Tab never registers it as a top-level app window
            long style = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE).ToInt64();
            style &= ~(long)(NativeMethods.WS_POPUP | NativeMethods.WS_VISIBLE);
            style |= (NativeMethods.WS_CHILD | NativeMethods.WS_CLIPSIBLINGS);
            NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE, new IntPtr(style));

            long exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            exStyle |= (NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT);
            exStyle &= ~(long)(NativeMethods.WS_EX_APPWINDOW | NativeMethods.WS_EX_CONTROLPARENT | NativeMethods.WS_EX_LAYERED);
            NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

            // 2. SetParent to desktop container (Progman on Win11, WorkerW on Win10) while still hidden
            IntPtr prevParent = NativeMethods.SetParent(hWnd, parentHwnd);
            int err = Marshal.GetLastWin32Error();
            Log($"SetParent(0x{hWnd.ToInt64():X}, 0x{parentHwnd.ToInt64():X}) -> prev=0x{prevParent.ToInt64():X}, Error={err}, CurrentParent=0x{NativeMethods.GetParent(hWnd).ToInt64():X}");

            // 3. Map screen coordinates to parent client coordinates
            var pt = new NativeMethods.POINT { X = x, Y = y };
            NativeMethods.MapWindowPoints(IntPtr.Zero, parentHwnd, ref pt, 1);

            IntPtr insertPos = (insertAfterHwnd != IntPtr.Zero && NativeMethods.IsWindow(insertAfterHwnd))
                ? insertAfterHwnd
                : new IntPtr(NativeMethods.HWND_BOTTOM);

            // 4. Show inside desktop container without activation, strictly behind SHELLDLL_DefView
            NativeMethods.SetWindowPos(
                hWnd,
                insertPos,
                pt.X, pt.Y, width, height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);

            _form.Show();

            NativeMethods.SetWindowPos(
                hWnd,
                insertPos,
                pt.X, pt.Y, width, height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

            Log($"Form 0x{hWnd.ToInt64():X} successfully reparented to 0x{parentHwnd.ToInt64():X} behind 0x{insertPos.ToInt64():X} at ({pt.X},{pt.Y} {width}x{height})");
        }
        else
        {
            Log("Desktop parent container not available; keeping WallpaperWindow hidden to avoid floating window.");
        }

        _ = InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string userDataDir = Path.Combine(appData, "ExaWallpaper", "WebView2Data");
            Directory.CreateDirectory(userDataDir);

            var options = new CoreWebView2EnvironmentOptions(
                "--disable-background-timer-throttling --disable-features=CalculateNativeWinOcclusion --autoplay-policy=no-user-gesture-required");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataDir, options: options);
            if (_isDisposed || _form.IsDisposed || _webView.IsDisposed) return;

            await _webView.EnsureCoreWebView2Async(env);
            if (_isDisposed || _form.IsDisposed || _webView.IsDisposed) return;

            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            _webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

            _webView.CoreWebView2.WebMessageReceived += (s, e) =>
            {
                try { Log($"[WebView2] {e.TryGetWebMessageAsString()}"); } catch { }
            };

            _isInitialized = true;
            Log("WebView2 initialized successfully.");

            if (!string.IsNullOrEmpty(_currentVideoPath))
            {
                NavigateVideo();
            }
        }
        catch (ObjectDisposedException)
        {
            Log("InitializeWebViewAsync aborted because WallpaperWindow was already disposed.");
        }
        catch (Exception ex)
        {
            Log($"InitializeWebViewAsync exception: {ex.Message}");
        }
    }

    public void LoadVideo(string filePath)
    {
        if (_isDisposed || _form.IsDisposed) return;
        Log($"LoadVideo: '{filePath}'");
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Log($"Video file does not exist: '{filePath}'");
            return;
        }

        _currentVideoPath = filePath;

        if (_isInitialized)
        {
            NavigateVideo();
        }
    }

    private void NavigateVideo()
    {
        var core = SafeCoreWebView;
        if (string.IsNullOrEmpty(_currentVideoPath) || core == null) return;

        string folder = Path.GetDirectoryName(_currentVideoPath) ?? "";
        string fileName = Path.GetFileName(_currentVideoPath);

        try
        {
            core.SetVirtualHostNameToFolderMapping(
                "wallpaper.assets",
                folder,
                CoreWebView2HostResourceAccessKind.Allow);
        }
        catch (Exception ex)
        {
            Log($"SetVirtualHostNameToFolderMapping error: {ex.Message}");
        }

        string videoUrl = $"https://wallpaper.assets/{Uri.EscapeDataString(fileName)}";
        string objectFit = _stretchMode switch
        {
            "Uniform" => "contain",
            "Fill" => "fill",
            _ => "cover"
        };
        string mutedAttr = _isMuted ? "muted" : "";
        string volumeStr = _volume.ToString("0.00", CultureInfo.InvariantCulture);

        string html = $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'/>
<style>
  html, body {{
    margin: 0;
    padding: 0;
    overflow: hidden;
    width: 100%;
    height: 100%;
    background-color: #000000;
    pointer-events: none;
    user-select: none;
  }}
  video {{
    width: 100%;
    height: 100%;
    object-fit: {objectFit};
    pointer-events: none;
    display: block;
  }}
</style>
</head>
<body>
  <video id='bgvid' src='{videoUrl}' autoplay loop {mutedAttr} playsinline></video>
  <script>
    const v = document.getElementById('bgvid');
    v.onplay = () => chrome.webview.postMessage('Video started playing');
    v.onerror = () => chrome.webview.postMessage('Video error: ' + (v.error ? v.error.message : 'unknown'));
    v.volume = {volumeStr};
    v.play().catch(() => {{}});
  </script>
</body>
</html>";

        _webView.NavigateToString(html);
        Log($"Navigated video to '{videoUrl}' (Fit: {objectFit}, Muted: {_isMuted}, Volume: {volumeStr})");
    }

    public void Play()
    {
        var core = SafeCoreWebView;
        if (core != null)
        {
            try { core.ExecuteScriptAsync("document.getElementById('bgvid')?.play();"); } catch { }
        }
    }

    public void Pause()
    {
        var core = SafeCoreWebView;
        if (core != null)
        {
            try { core.ExecuteScriptAsync("document.getElementById('bgvid')?.pause();"); } catch { }
        }
    }

    public void SetMuted(bool isMuted)
    {
        _isMuted = isMuted;
        var core = SafeCoreWebView;
        if (core != null)
        {
            try { core.ExecuteScriptAsync($"var v=document.getElementById('bgvid'); if(v) v.muted = {isMuted.ToString().ToLower()};"); } catch { }
        }
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0.0, 1.0);
        var core = SafeCoreWebView;
        if (core != null)
        {
            string vol = _volume.ToString("0.00", CultureInfo.InvariantCulture);
            try { core.ExecuteScriptAsync($"var v=document.getElementById('bgvid'); if(v) v.volume = {vol};"); } catch { }
        }
    }

    public void SetStretchMode(string mode)
    {
        _stretchMode = mode;
        var core = SafeCoreWebView;
        if (core != null)
        {
            string fit = mode switch { "Uniform" => "contain", "Fill" => "fill", _ => "cover" };
            try { core.ExecuteScriptAsync($"var v=document.getElementById('bgvid'); if(v) v.style.objectFit = '{fit}';"); } catch { }
        }
    }

    public void Close()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_isDisposed && _form.IsDisposed && _webView.IsDisposed) return;
        _isDisposed = true;

        Log($"Disposing WallpaperWindow 0x{_cachedHandle.ToInt64():X}");

        try
        {
            if (!_webView.IsDisposed)
            {
                _webView.Dispose();
            }
            if (!_form.IsDisposed && !_form.Disposing)
            {
                if (_form.IsHandleCreated && _cachedHandle != IntPtr.Zero && NativeMethods.IsWindow(_cachedHandle))
                {
                    _form.Close();
                }
                if (!_form.IsDisposed)
                {
                    _form.Dispose();
                }
            }
        }
        catch { }
    }
}
