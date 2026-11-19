using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Microsoft.Win32;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using ExaWallpaper.Models;
using ExaWallpaper.Services;

namespace ExaWallpaper.Views;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly ConfigService _configService;
    private readonly Dictionary<string, System.Windows.Controls.TextBox> _monitorTextBoxes = new();

    public event Action<AppConfig>? ConfigApplied;

    public SettingsWindow(AppConfig config, ConfigService configService)
    {
        InitializeComponent();
        _config = config;
        _configService = configService;

        LoadValues();
        PopulateMonitorList();
    }

    private void LoadValues()
    {
        TxtVideoPath.Text = !string.IsNullOrEmpty(_config.VideoPath) ? _config.VideoPath : "No video selected";
        ChkMute.IsChecked = _config.IsMuted;
        SldVolume.Value = _config.Volume * 100;
        TxtVolumeVal.Text = $"{(int)(_config.Volume * 100)}%";

        ChkPauseFullscreen.IsChecked = _config.AutoPauseOnFullscreen;
        ChkPauseBattery.IsChecked = _config.AutoPauseOnBattery;

        foreach (ComboBoxItem item in CmbStretch.Items)
        {
            if (item.Tag?.ToString() == _config.StretchMode)
            {
                CmbStretch.SelectedItem = item;
                break;
            }
        }

        foreach (ComboBoxItem item in CmbMonitors.Items)
        {
            if (item.Tag?.ToString() == _config.MultiMonitorMode)
            {
                CmbMonitors.SelectedItem = item;
                break;
            }
        }

        UpdatePerMonitorVisibility();
        ChkRunAtStartup.IsChecked = StartupService.IsStartupEnabled();
    }

    private void PopulateMonitorList()
    {
        PnlMonitorList.Children.Clear();
        _monitorTextBoxes.Clear();

        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            string key = i.ToString();
            string existingPath = _config.MonitorVideos.TryGetValue(key, out var p) ? p : string.Empty;

            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x1F, 0x23)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var stack = new StackPanel();

            string title = $"Monitor {i + 1}: {screen.Bounds.Width}x{screen.Bounds.Height}" + (screen.Primary ? " (Primary)" : "");
            var lbl = new TextBlock
            {
                Text = title,
                FontSize = 11,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA1, 0xA1, 0xAA)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(lbl);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var txt = new System.Windows.Controls.TextBox
            {
                Text = !string.IsNullOrEmpty(existingPath) ? existingPath : "Using default video",
                IsReadOnly = true,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x18, 0x18, 0x1B)),
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD4, 0xD4, 0xD8)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3F, 0x3F, 0x46)),
                Padding = new Thickness(6, 4, 6, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 11
            };
            Grid.SetColumn(txt, 0);
            grid.Children.Add(txt);

            var btn = new System.Windows.Controls.Button
            {
                Content = "Browse...",
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(10, 4, 10, 4),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 11
            };
            btn.Click += (s, e) =>
            {
                var dialog = new OpenFileDialog
                {
                    Title = $"Select Video for Monitor {i + 1}",
                    Filter = "Video Files (*.mp4;*.mov;*.webm)|*.mp4;*.mov;*.webm|All Files (*.*)|*.*",
                    Multiselect = false
                };
                if (dialog.ShowDialog() == true)
                {
                    txt.Text = dialog.FileName;
                }
            };
            Grid.SetColumn(btn, 1);
            grid.Children.Add(btn);

            stack.Children.Add(grid);
            border.Child = stack;

            PnlMonitorList.Children.Add(border);
            _monitorTextBoxes[key] = txt;
        }
    }

    private void CmbMonitors_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePerMonitorVisibility();
    }

    private void UpdatePerMonitorVisibility()
    {
        if (PnlPerMonitor == null) return;

        if (CmbMonitors.SelectedItem is ComboBoxItem item && item.Tag?.ToString() == "PerMonitor")
        {
            PnlPerMonitor.Visibility = Visibility.Visible;
        }
        else
        {
            PnlPerMonitor.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Default Video Wallpaper",
            Filter = "Video Files (*.mp4;*.mov;*.webm)|*.mp4;*.mov;*.webm|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            TxtVideoPath.Text = dialog.FileName;
        }
    }

    private void ChkMute_Changed(object sender, RoutedEventArgs e)
    {
        if (SldVolume != null)
        {
            SldVolume.IsEnabled = !(ChkMute.IsChecked ?? true);
        }
    }

    private void SldVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtVolumeVal != null)
        {
            TxtVolumeVal.Text = $"{(int)e.NewValue}%";
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        string selectedPath = TxtVideoPath.Text;
        if (File.Exists(selectedPath))
        {
            _config.VideoPath = selectedPath;
        }

        _config.IsMuted = ChkMute.IsChecked ?? true;
        _config.Volume = SldVolume.Value / 100.0;
        _config.AutoPauseOnFullscreen = ChkPauseFullscreen.IsChecked ?? true;
        _config.AutoPauseOnBattery = ChkPauseBattery.IsChecked ?? true;

        if (CmbStretch.SelectedItem is ComboBoxItem stretchItem && stretchItem.Tag != null)
        {
            _config.StretchMode = stretchItem.Tag.ToString()!;
        }

        if (CmbMonitors.SelectedItem is ComboBoxItem monitorItem && monitorItem.Tag != null)
        {
            _config.MultiMonitorMode = monitorItem.Tag.ToString()!;
        }

        // Save per-monitor videos
        _config.MonitorVideos.Clear();
        foreach (var (key, txt) in _monitorTextBoxes)
        {
            string path = txt.Text;
            if (File.Exists(path))
            {
                _config.MonitorVideos[key] = path;
            }
        }

        bool startup = ChkRunAtStartup.IsChecked ?? false;
        _config.RunAtStartup = startup;
        StartupService.SetStartup(startup);

        _configService.Save(_config);
        ConfigApplied?.Invoke(_config);

        Close();
    }
}
