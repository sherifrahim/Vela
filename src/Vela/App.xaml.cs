using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Vela.Core;
using Vela.Shell;
using Vela.Theming;
using Vela.Views;

namespace Vela;

public partial class App : Application
{
    Mutex? _singleInstance;
    DockWindow? _dock;
    bool _cleanedUp;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Developer check of the live-blur backdrop variants (second display if present), 12 s, then exit.
        if (e.Args.Contains("--blur-test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Settings.Load(); ThemeManager.Apply();
            var mons = Interop.Native.AllMonitors();
            var mon = (mons.FirstOrDefault(x => !x.Primary).Handle != IntPtr.Zero ? mons.First(x => !x.Primary) : mons.First()).Info.rcMonitor;
            // A bright striped window behind the bars, so blur is unmistakable.
            var stripes = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            foreach (var hex in new[] { "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#0A84FF", "#AF52DE", "#FFFFFF", "#000000" })
                stripes.Children.Add(new System.Windows.Controls.Border { Width = 60, Background = UI.UI.Brush(hex), Child = new System.Windows.Controls.TextBlock { Text = "Blur", FontSize = 22, Foreground = System.Windows.Media.Brushes.Black } });
            var bg = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Content = stripes, Topmost = true, Left = (mon.Left + 150) / 1.0, Top = (mon.Bottom - 470) / 1.0, Width = 1000, Height = 420, ShowActivated = false };
            bg.Show();
            Interop.Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(bg).Handle, Interop.Native.HWND_TOPMOST, mon.Left + 150, mon.Bottom - 470, 1000, 420, Interop.Native.SWP_NOACTIVATE);
            int i = 0;
            var variants = new (int, uint, int, bool)[] { (4, 0x01000000u, 0, true), (4, 0x01000000u, 0, false), (4, 0x10FFFFFFu, 0, false), (4, 0x00000000u, 0, false) };
            foreach (var v in variants)
            {
                var bw = new BackdropWindow(BackdropWindow.Kind.AccentAcrylic) { Raw = v };
                bw.Show();
                int y = mon.Bottom - 470 + 20 + i * 100;
                bw.Place(new Interop.Native.RECT { Left = mon.Left + 200, Top = y, Right = mon.Left + 1100, Bottom = y + 70 }, 28, IntPtr.Zero);
                // Right half: the dock's 40 % #141414 tint on top, i.e. the final look.
                var tint = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
                    Content = new System.Windows.Controls.Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0x14, 0x14, 0x14)), CornerRadius = new CornerRadius(0, 16, 16, 0) } };
                tint.Show();
                Interop.Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(tint).Handle, Interop.Native.HWND_TOPMOST, mon.Left + 650, y, 450, 70, Interop.Native.SWP_NOACTIVATE);
                i++;
            }
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            t.Tick += (_, _) => Shutdown();
            t.Start();
            return;
        }

        // Off-screen render of the dock + every widget to PNGs (agents' visual checks): no window, no taskbar change.
        int ri = Array.IndexOf(e.Args, "--render");
        if (ri >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int ti = Array.IndexOf(e.Args, "--theme");
            try { RenderCatalog.Run(ri + 1 < e.Args.Length ? e.Args[ri + 1] : "render", ti >= 0 && ti + 1 < e.Args.Length ? e.Args[ti + 1] : null); }
            catch (Exception ex) { Log.Error("Render failed", ex); }
            Shutdown();
            return;
        }

        if (e.Args.Contains("--restore-taskbar"))
        {
            TaskbarController.EmergencyRestore();
            Shutdown();
            return;
        }

        // Remote commands to a running dock (handy for scripts, tests and agents):
        //   --quit       close it (it restores the taskbar itself)
        //   --widgets    open the widget picker
        //   --settings   open Settings
        var remote = e.Args.FirstOrDefault(a => a is "--quit" or "--widgets" or "--settings");
        if (remote != null)
        {
            var h = Interop.Native.FindWindow(null, "Vela Dock");
            if (h != IntPtr.Zero)
            {
                if (remote == "--quit") Interop.Native.PostMessage(h, Interop.Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                else Interop.Native.PostMessage(h, (int)DockWindow.CommandMessage, new IntPtr(remote == "--widgets" ? 1 : 2), IntPtr.Zero);
            }
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(true, "Vela.Dock.SingleInstance", out bool first);
        if (!first && e.Args.Contains("--wait-for-exit"))
            first = _singleInstance.WaitOne(TimeSpan.FromSeconds(8));
        if (!first)
        {
            Log.Info("Another instance is running; exiting");
            _singleInstance = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Error("Fatal", args.ExceptionObject as Exception);
            Cleanup();
        };
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Error("Unobserved task", args.Exception); args.SetObserved(); };
        SystemEvents.SessionEnding += (_, _) => Cleanup();
        SystemEvents.SessionSwitch += (_, args) =>
        {
            bool locked = args.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleDisconnect;
            Svc.ScreenTime.Locked = locked;
            Svc.Ticker.Paused = locked;
        };

        Log.Info($"Vela starting (pid {Environment.ProcessId})");
        Settings.Load();
        ThemeManager.Apply();
        WallpaperGlass.Reload();
        Svc.ScreenTime.Start();
        Svc.Ticker.Start();

        _dock = new DockWindow();
        _dock.Show();
        Views.IslandWindow.Sync();

        // Startup allocations are done; hand the pages back to Windows.
        var trim = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        trim.Tick += (_, _) => { trim.Stop(); GC.Collect(2, GCCollectionMode.Optimized, false); WinActions.TrimWorkingSet(); };
        trim.Start();
    }

    void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("UI exception (continuing)", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    /// <summary>
    /// Shutdown in independent stages: a failure in one never skips the next, and restoring the Windows
    /// taskbar always runs last, unconditionally (ground rule 1). Safe from any thread: WPF teardown is
    /// marshalled to the UI thread with a timeout; the taskbar restore is plain Win32 and runs here.
    /// </summary>
    void Cleanup()
    {
        if (_cleanedUp || _dock == null) return; // helper invocations (--quit etc.) own nothing
        _cleanedUp = true;
        bool clean = true;
        void Stage(string name, Action a)
        {
            try { a(); }
            catch (Exception ex) { clean = false; try { Log.Error("Cleanup: " + name, ex); } catch { } }
        }
        void OnUi(Action a)
        {
            if (Dispatcher.CheckAccess()) { a(); return; }
            Dispatcher.Invoke(a, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(3));
        }
        try
        {
            Stage("flush settings and data", Svc.FlushAll);
            Stage("dock teardown", () => OnUi(() => _dock?.Teardown()));
        }
        finally
        {
            Stage("restore taskbar", Svc.Taskbar.Restore);
            try { Log.Info(clean ? "Vela stopped cleanly" : "Vela stopped (with cleanup errors; taskbar restored)"); } catch { }
            try { _singleInstance?.ReleaseMutex(); } catch { }
        }
    }
}
