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

    void Cleanup()
    {
        if (_cleanedUp || _dock == null) return; // helper invocations (--quit etc.) own nothing
        _cleanedUp = true;
        try
        {
            Svc.FlushAll();
            _dock?.Teardown();
            Svc.Taskbar.Restore();
            Log.Info("Vela stopped cleanly");
        }
        catch (Exception ex) { Log.Error("Cleanup", ex); }
        finally
        {
            try { _singleInstance?.ReleaseMutex(); } catch { }
        }
    }
}
