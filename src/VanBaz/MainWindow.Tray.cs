using System.Windows;
using System.Windows.Media.Animation;

namespace VanBaz;
public partial class MainWindow
{
    private int trayAnimationVersion;
    private bool CanAnimateTray => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast && !System.Windows.Forms.SystemInformation.TerminalServerSession;
    private void CancelTrayAnimation()
    {
        trayAnimationVersion++; BeginAnimation(OpacityProperty, null); Opacity = 1;
    }
    private void HideToTray()
    {
        if (tray == null || inTray) return;
        CaptureNormalPlacement();
        trayWindowState = WindowState == WindowState.Minimized ? lastWindowState : WindowState;
        trayBounds = appliedBounds; trayDisplays = DisplayAreas();
        inTray = true; tray.Visible = true; CancelTrayAnimation();
        if (!CanAnimateTray || WindowState == WindowState.Minimized || !IsVisible) { Hide(); return; }
        int version = trayAnimationVersion;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => { if (version != trayAnimationVersion || !inTray) return; Hide(); CancelTrayAnimation(); };
        BeginAnimation(OpacityProperty, fade);
    }
    private void ShowFromTray()
    {
        trayPanel?.Close();
        CancelTrayAnimation();
        if (inTray)
        {
            PlaceNormalWindow(trayDisplays != null && trayDisplays.SequenceEqual(DisplayAreas()) ? trayBounds : null); WindowState = trayWindowState; inTray = false;
        }
        else if (WindowState == WindowState.Minimized) WindowState = lastWindowState;
        Show(); Activate(); if (tray != null) tray.Visible = false;
    }
}
