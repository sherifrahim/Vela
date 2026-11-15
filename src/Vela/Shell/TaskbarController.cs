using System.Runtime.InteropServices;
using Vela.Core;
using static Vela.Interop.Native;

namespace Vela.Shell;

/// <summary>
/// Hides / auto-hides the Windows taskbar while Vela runs and ALWAYS puts it back.
///
/// Safety model (read before changing):
///  1. Before touching anything we write taskbar-restore.json with the user's original state.
///  2. Restore() runs on normal exit, session end, and unhandled exceptions (see App.xaml.cs).
///  3. If Vela was killed, the marker survives; the next launch (or `Vela.exe --restore-taskbar`)
///     reads the ORIGINAL state from it instead of the current (modified) one.
/// </summary>
public sealed class TaskbarController
{
    static readonly string MarkerPath = Paths.File("taskbar-restore.json");

    sealed class Marker { public int State { get; set; } }

    int _originalState = -1;
    string _mode = "keep";

    public TaskbarController()
    {
        if (File.Exists(MarkerPath))
        {
            _originalState = Json.Load<Marker>(MarkerPath).State;
            Log.Warn($"Found taskbar marker from a previous run; original state = {_originalState}");
        }
    }

    public void Apply(string mode)
    {
        _mode = mode;
        if (mode == "keep") { Restore(); return; }

        if (_originalState < 0)
        {
            _originalState = GetState();
            Json.SaveAtomic(MarkerPath, new Marker { State = _originalState });
        }

        SetState(ABS_AUTOHIDE | (_originalState & ABS_ALWAYSONTOP));
        ShowTaskbars(mode != "hidden");
    }

    /// <summary>Called periodically: Explorer sometimes re-shows the taskbar (e.g. after Start opens).</summary>
    public void Enforce()
    {
        if (_mode != "hidden") return;
        foreach (var h in TaskbarWindows())
            if (IsWindowVisible(h)) ShowWindow(h, SW_HIDE);
    }

    /// <summary>Explorer restarted: our hidden state was lost. Re-apply.</summary>
    public void OnExplorerRestarted() => Apply(_mode);

    public void Restore()
    {
        try
        {
            ShowTaskbars(true);
            if (_originalState >= 0) SetState(_originalState);
            _originalState = -1;
            if (File.Exists(MarkerPath)) File.Delete(MarkerPath);
        }
        catch (Exception ex) { Log.Error("Taskbar restore failed", ex); }
    }

    /// <summary>Static entry used by `Vela.exe --restore-taskbar` and crash handlers.</summary>
    public static void EmergencyRestore()
    {
        var c = new TaskbarController();
        if (c._originalState < 0) c._originalState = 0; // no marker: assume "always visible"
        c.Restore();
    }

    static IEnumerable<IntPtr> TaskbarWindows()
    {
        var main = FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) yield return main;
        var h = IntPtr.Zero;
        while ((h = FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            yield return h;
    }

    static void ShowTaskbars(bool show)
    {
        foreach (var h in TaskbarWindows()) ShowWindow(h, show ? SW_SHOWNA : SW_HIDE);
    }

    static int GetState()
    {
        var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = FindWindow("Shell_TrayWnd", null) };
        return SHAppBarMessage(ABM_GETSTATE, ref d).ToInt32();
    }

    static void SetState(int state)
    {
        var d = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = FindWindow("Shell_TrayWnd", null),
            lParam = new IntPtr(state),
        };
        SHAppBarMessage(ABM_SETSTATE, ref d);
    }
}
