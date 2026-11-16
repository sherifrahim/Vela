using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Vela.Core;
using static Vela.Interop.Native;

namespace Vela.Views;

/// <summary>
/// Auto-hide for one dock (Settings.AutoHide):
///  - "smart": slide away while the active window on this display overlaps the dock (or is maximized),
///  - "always": stay tucked away until the pointer touches the screen edge.
/// The dock comes back when the pointer reaches the edge trigger and stays while hovered, while a card,
/// preview or menu is open, or while you drag. Adapted from OpenTaskbar's DockAutoHideController
/// (MIT, see THIRD_PARTY_NOTICES.md). Event-driven: one 400 ms timer only between "should hide" and hiding.
/// </summary>
public sealed class DockAutoHide
{
    readonly DockWindow _dock;
    readonly FrameworkElement _surface;
    readonly TranslateTransform _slide = new();
    readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly DispatcherTimer _evalThrottle = new() { Interval = TimeSpan.FromMilliseconds(250) };
    bool _overlap;

    public bool IsHidden { get; private set; }
    public event Action<bool>? HiddenChanged;

    public DockAutoHide(DockWindow dock, FrameworkElement surface)
    {
        _dock = dock;
        _surface = surface;
        surface.RenderTransform = _slide;
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); if (ShouldHide()) Hide(); };
        _evalThrottle.Tick += (_, _) => { _evalThrottle.Stop(); _overlap = ComputeOverlap(); Evaluate(); };
    }

    static string Mode => Settings.Current.AutoHide;

    /// <summary>Windows moved / foreground changed: re-check overlap soon.</summary>
    public void Poke()
    {
        if (Mode == "off") return;
        if (!_evalThrottle.IsEnabled) _evalThrottle.Start();
    }

    public void Evaluate()
    {
        if (Mode == "off") { _hideTimer.Stop(); if (IsHidden) Reveal(); return; }
        if (ShouldHide()) { if (!IsHidden && !_hideTimer.IsEnabled) _hideTimer.Start(); }
        else { _hideTimer.Stop(); if (IsHidden) Reveal(); }
    }

    bool Busy => _surface.IsMouseOver || FlyoutWindow.Current != null || WindowPreview.IsOpen
                 || _dock.IsInteracting || Widgets.WidgetBase.EditMode;

    bool ShouldHide() => Mode switch
    {
        "always" => !Busy,
        "smart" => _overlap && !Busy,
        _ => false,
    };

    bool ComputeOverlap()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == _dock.WindowHandle || !IsWindowVisible(fg) || IsIconic(fg)) return false;
        if (GetClassNameOf(fg) is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        if (MonitorFromWindow(fg, 2) != MonitorFromWindow(_dock.WindowHandle, 2)) return false;
        if (IsZoomed(fg)) return true;
        if (!GetWindowRect(fg, out var w) || !GetWindowRect(_dock.WindowHandle, out var d)) return false;
        return w.Left < d.Right && w.Right > d.Left && w.Bottom > d.Top + 8 && w.Top < d.Bottom;
    }

    /// <summary>Pointer reached the screen-edge trigger or entered the bar.</summary>
    public void PointerArrived() { _hideTimer.Stop(); if (IsHidden) Reveal(); }

    /// <summary>Pointer left the bar.</summary>
    public void PointerLeft() => Evaluate();

    void Hide()
    {
        IsHidden = true;
        bool top = Settings.Current.Edge == "top";
        double away = (_surface.ActualHeight + Settings.Current.EdgeMargin + 12) * (top ? -1 : 1);
        _slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(away, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        HiddenChanged?.Invoke(true);
    }

    void Reveal()
    {
        IsHidden = false;
        _slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        HiddenChanged?.Invoke(false);
    }
}
