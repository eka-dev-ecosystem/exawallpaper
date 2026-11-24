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

    public IntPtr Handle => _form.Handle;

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
            BackColor = Color.Black
        };

        _form.Show();
        IntPtr hWnd = _form.Handle;

        if (parentHwnd != IntPtr.Zero && NativeMethods.IsWindow(parentHwnd))
        {
            try { NativeMethods.SetThreadDpiHostingBehavior(NativeMethods.DPI_HOSTING_BEHAVIOR_MIXED); } catch { }

            // 1. Change style to WS_CHILD
            long style = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE).ToInt64();
            style &= ~NativeMethods.WS_POPUP;
            style |= (NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPSIBLINGS);
            NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE, new IntPtr(style));

            // 2. SetParent to desktop container (Progman on Win11, WorkerW on Win10)
            IntPtr prevParent = NativeMethods.SetParent(hWnd, parentHwnd);
            int err = Marshal.GetLastWin32Error();
            Log($"SetParent(0x{hWnd.ToInt64():X}, 0x{parentHwnd.ToInt64():X}) -> prev=0x{prevParent.ToInt64():X}, Error={err}, CurrentParent=0x{NativeMethods.GetParent(hWnd).ToInt64():X}");

            // 3. Map screen coordinates to parent client coordinates
            var pt = new NativeMethods.POINT { X = x, Y = y };
            NativeMethods.MapWindowPoints(IntPtr.Zero, parentHwnd, ref pt, 1);

            IntPtr insertPos = (insertAfterHwnd != IntPtr.Zero && NativeMethods.IsWindow(insertAfterHwnd))
                ? insertAfterHwnd
                : new IntPtr(NativeMethods.HWND_BOTTOM);

            NativeMethods.SetWindowPos(
                hWnd,
                insertPos,
                pt.X, pt.Y, width, height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_FRAMECHANGED);

            Log($"Form 0x{hWnd.ToInt64():X} successfully reparented to 0x{parentHwnd.ToInt64():X} behind 0x{insertPos.ToInt64():X} at ({pt.X},{pt.Y} {width}x{height})");
        }

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.Black
        };

        _form.Controls.Add(_webView);
        _ = InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string userDataDir = Path.Combine(appData, "ExaWallpaper", "WebView2Data");
            Directory.CreateDirectory(userDataDir);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataDir);
            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;

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
        catch (Exception ex)
        {
            Log($"InitializeWebViewAsync exception: {ex.Message}");
        }
    }

    public void LoadVideo(string filePath)
    {
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
        if (string.IsNullOrEmpty(_currentVideoPath) || _webView.CoreWebView2 == null) return;

        string folder = Path.GetDirectoryName(_currentVideoPath) ?? "";
        string fileName = Path.GetFileName(_currentVideoPath);

        try
        {
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
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
        if (_webView.CoreWebView2 != null)
        {
            try { _webView.CoreWebView2.ExecuteScriptAsync("document.getElementById('bgvid')?.play();"); } catch { }
        }
    }

    public void Pause()
    {
        if (_webView.CoreWebView2 != null)
        {
            try { _webView.CoreWebView2.ExecuteScriptAsync("document.getElementById('bgvid')?.pause();"); } catch { }
        }
    }

    public void SetMuted(bool isMuted)
    {
        _isMuted = isMuted;
        if (_webView.CoreWebView2 != null)
        {
            try { _webView.CoreWebView2.ExecuteScriptAsync($"var v=document.getElementById('bgvid'); if(v) v.muted = {isMuted.ToString().ToLower()};"); } catch { }
        }
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0.0, 1.0);
        if (_webView.CoreWebView2 != null)
        {
            string vol = _volume.ToString("0.00", CultureInfo.InvariantCulture);
            try { _webView.CoreWebView2.ExecuteScriptAsync($"var v=document.getElementById('bgvid'); if(v) v.volume = {vol};"); } catch { }
        }
    }

    public void SetStretchMode(string mode)
    {
        _stretchMode = mode;
        if (_webView.CoreWebView2 != null)
        {
            string fit = mode switch { "Uniform" => "contain", "Fill" => "fill", _ => "cover" };
            try { _webView.CoreWebView2.ExecuteScriptAsync($"var v=document.getElementById('bgvid'); if(v) v.style.objectFit = '{fit}';"); } catch { }
        }
    }

    public void Close()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Log($"Disposing WallpaperWindow 0x{Handle.ToInt64():X}");

        try
        {
            _webView.Dispose();
            _form.Close();
            _form.Dispose();
        }
        catch { }
    }
}
