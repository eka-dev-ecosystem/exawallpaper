using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LiveWallpaper.Services;

public class PowerMonitorService : IDisposable
{
    public event Action<bool>? PowerPauseStateChanged;

    public bool IsSuspended { get; private set; } = false;
    public bool IsOnBattery { get; private set; } = false;

    public PowerMonitorService()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        CheckBatteryStatus();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                IsSuspended = true;
                PowerPauseStateChanged?.Invoke(true);
                break;

            case PowerModes.Resume:
                IsSuspended = false;
                CheckBatteryStatus();
                PowerPauseStateChanged?.Invoke(IsOnBattery);
                break;

            case PowerModes.StatusChange:
                CheckBatteryStatus();
                break;
        }
    }

    public void CheckBatteryStatus()
    {
        var powerLine = SystemInformation.PowerStatus.PowerLineStatus;
        bool onBattery = powerLine == PowerLineStatus.Offline;

        if (onBattery != IsOnBattery)
        {
            IsOnBattery = onBattery;
            PowerPauseStateChanged?.Invoke(IsOnBattery);
        }
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
