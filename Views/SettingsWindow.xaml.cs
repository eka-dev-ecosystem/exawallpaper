using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using LiveWallpaper.Models;
using LiveWallpaper.Services;

namespace LiveWallpaper.Views;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly ConfigService _configService;

    public event Action<AppConfig>? ConfigApplied;

    public SettingsWindow(AppConfig config, ConfigService configService)
    {
        InitializeComponent();
        _config = config;
        _configService = configService;

        LoadValues();
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

        ChkRunAtStartup.IsChecked = StartupService.IsStartupEnabled();
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Video Wallpaper",
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

        bool startup = ChkRunAtStartup.IsChecked ?? false;
        _config.RunAtStartup = startup;
        StartupService.SetStartup(startup);

        _configService.Save(_config);
        ConfigApplied?.Invoke(_config);

        Close();
    }
}
