// Jump lists (recent / frequent / pinned destinations and app tasks) read straight from the Shell.
// Ported from OpenTaskbar (MIT License, Copyright (c) 2026 OpenTaskbar contributors,
// github.com/alztrk/opentaskbar; full notice in THIRD_PARTY_NOTICES.md):
// Services/NativeJumpListSession.cs, ApplicationJumpListService.cs and ShellComObject.cs, with Vela's
// strings and without the resource-icon fallback. The COM ABI follows Open-Shell / System Informer.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Vela.Shell;

public enum JumpListStatus { Ready, Empty, IdentityUnavailable, Unsupported, Unavailable }
public sealed record JumpListEntry(int CommandId, string Title, BitmapSource? Icon, bool IsSeparator = false);
public sealed record JumpListGroup(string Title, IReadOnlyList<JumpListEntry> Entries);
public sealed record ApplicationJumpList(long SessionId, JumpListStatus Status, IReadOnlyList<JumpListGroup> Groups);

static class UiText
{
    public static string Get(string key) => key switch
    {
        "PinnedDestinations" => "Pinned", "Frequent" => "Frequent", "Recent" => "Recent", "Tasks" => "Tasks", _ => key,
    };
}

// Native objects stay on the Shell worker's STA; only immutable menu data crosses threads.
internal sealed class ShellComObject(nint handle) : IDisposable
{
    public nint Handle { get; private set; } = handle;

    public static ShellComObject Create(Guid classId, Guid interfaceId)
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref classId, 0, 1, ref interfaceId, out var pointer));
        return new ShellComObject(pointer);
    }

    public ShellComObject? Query(Guid interfaceId)
    {
        var result = Marshal.QueryInterface(Handle, in interfaceId, out var pointer);
        if (result == unchecked((int)0x80004002)) return null;
        Marshal.ThrowExceptionForHR(result);
        return new ShellComObject(pointer);
    }

    public T Method<T>(int slot) where T : Delegate
    {
        ObjectDisposedException.ThrowIf(Handle == 0, this);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(Handle), slot * IntPtr.Size));
    }

    public void Dispose()
    {
        if (Handle == 0) return;
        Marshal.Release(Handle);
        Handle = 0;
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid classId, nint outer, uint context, ref Guid interfaceId, out nint pointer);
}
internal sealed class NativeJumpListSession : IDisposable
{
    // Read-only ABI references: Open-Shell JumpLists.cpp and System Informer appresolverp.h.
    private static readonly Guid ResolverClass = new("660B90C8-73A9-4B58-8CAE-355B7F55341B");
    private static readonly Guid ResolverInterface = new("DE25675A-72DE-44B4-9373-05170450C140");
    private static readonly Guid DestinationClass = new("77F10CF0-3DB5-4966-B520-B7C54FD35ED6");
    private static readonly Guid DestinationInterface = new("507101CD-F6AD-46C8-8E20-EEB9E6BAC47F");
    private static readonly Guid AutomaticClass = new("F0AE1542-F497-484B-A175-A20DB09144BA");
    private static readonly Guid AutomaticInterface = new("E9C5EF8D-FD41-4F72-BA87-EB03BAD5817C");
    private static readonly Guid ShellItemInterface = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid ObjectArrayInterface = new("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9");
    private static readonly Guid PropertyStoreInterface = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Guid ContextMenuInterface = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid ImageFactoryInterface = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");
    private static readonly Guid ShellLinkInterface = new("000214F9-0000-0000-C000-000000000046");
    private static readonly PropertyKey AppIdKey = new(new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    private static readonly PropertyKey SeparatorKey = new(AppIdKey.Format, 6);
    private static readonly PropertyKey HostEnvironmentKey = new(AppIdKey.Format, 14);
    private static readonly PropertyKey ActivationContextKey = new(AppIdKey.Format, 20);
    private static readonly PropertyKey TitleKey = new(new("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
    private readonly Dictionary<int, ShellComObject> _commands = [];
    private int _nextCommand;

    public ApplicationJumpList Read(long sessionId, nint window, string? shortcut, CancellationToken cancellation)
    {
        if (IntPtr.Size != 8) return new(sessionId, JumpListStatus.Unsupported, []);
        var appId = ResolveIdentity(window, shortcut);
        if (string.IsNullOrWhiteSpace(appId)) return new(sessionId, JumpListStatus.IdentityUnavailable, []);
        cancellation.ThrowIfCancellationRequested();
        using var custom = ShellComObject.Create(DestinationClass, DestinationInterface);
        Marshal.ThrowExceptionForHR(custom.Method<SetAppId>(4)(custom.Handle, appId));
        using var automatic = ShellComObject.Create(AutomaticClass, AutomaticInterface);
        Marshal.ThrowExceptionForHR(automatic.Method<InitializeAutomatic>(3)(automatic.Handle, appId, 0, 0));
        var groups = new List<JumpListGroup>();
        AddAutomatic(groups, automatic, 0, UiText.Get("PinnedDestinations"), cancellation);
        var categoryResult = custom.Method<GetCount>(6)(custom.Handle, out var count);
        if (MissingList(categoryResult)) count = 0;
        else Marshal.ThrowExceptionForHR(categoryResult);
        if (count > 32) throw new InvalidOperationException("Jump List category limit exceeded.");
        var hasKnownCategory = false;
        for (uint index = 0; index < count && _commands.Count < 100; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            Marshal.ThrowExceptionForHR(custom.Method<GetCategory>(7)(custom.Handle, index, 1, out var category));
            try
            {
                if (category.Type == 1)
                {
                    var kind = category.Kind;
                    if (kind is 1 or 2)
                    {
                        hasKnownCategory = true;
                        AddAutomatic(groups, automatic, 3 - kind, kind == 1 ? UiText.Get("Frequent") : UiText.Get("Recent"), cancellation);
                    }
                }
                else if (category.Type is 0 or 2)
                {
                    var iid = ObjectArrayInterface;
                    Marshal.ThrowExceptionForHR(custom.Method<EnumerateCategory>(9)(custom.Handle, index, ref iid, out var pointer));
                    using var array = new ShellComObject(pointer);
                    var title = category.Type == 2 ? UiText.Get("Tasks") : ResolveText(Marshal.PtrToStringUni(category.NameOrKind));
                    AddGroup(groups, title, array, cancellation);
                }
            }
            finally
            {
                if (category.Type == 0 && category.NameOrKind != 0) Marshal.FreeCoTaskMem(category.NameOrKind);
            }
        }
        if (!hasKnownCategory) AddAutomatic(groups, automatic, 1, UiText.Get("Recent"), cancellation);
        return new(sessionId, groups.Count > 0 ? JumpListStatus.Ready : JumpListStatus.Empty, groups);
    }

    private static string? ResolveIdentity(nint window, string? shortcut)
    {
        if (window != 0)
        {
            var iid = PropertyStoreInterface;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, ref iid, out var pointer));
            using var store = new ShellComObject(pointer);
            var explicitId = ReadString(store, AppIdKey);
            if (!string.IsNullOrWhiteSpace(explicitId)) return explicitId;
        }
        using var resolver = ShellComObject.Create(ResolverClass, ResolverInterface);
        nint appId;
        if (window != 0)
        {
            Marshal.ThrowExceptionForHR(resolver.Method<ResolveWindow>(5)(resolver.Handle, window, out appId, 0, 0, 0));
        }
        else if (!string.IsNullOrWhiteSpace(shortcut))
        {
            var iid = ShellItemInterface;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(shortcut, 0, ref iid, out var pointer));
            using var item = new ShellComObject(pointer);
            Marshal.ThrowExceptionForHR(resolver.Method<ResolveShortcut>(3)(resolver.Handle, item.Handle, out appId));
        }
        else return null;
        try { return Marshal.PtrToStringUni(appId); }
        finally { if (appId != 0) Marshal.FreeCoTaskMem(appId); }
    }

    private void AddAutomatic(List<JumpListGroup> groups, ShellComObject automatic, int kind, string title, CancellationToken cancellation)
    {
        var iid = ObjectArrayInterface;
        var result = automatic.Method<GetAutomaticList>(5)(automatic.Handle, kind, 20, 1, ref iid, out var pointer);
        if (MissingList(result)) return;
        Marshal.ThrowExceptionForHR(result);
        using var array = new ShellComObject(pointer);
        AddGroup(groups, title, array, cancellation);
    }

    private void AddGroup(List<JumpListGroup> groups, string? title, ShellComObject array, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        Marshal.ThrowExceptionForHR(array.Method<GetCount>(3)(array.Handle, out var count));
        var entries = new List<JumpListEntry>();
        for (uint index = 0; index < Math.Min(count, 40u) && _commands.Count < 100; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var iid = new Guid("00000000-0000-0000-C000-000000000046");
            Marshal.ThrowExceptionForHR(array.Method<GetAt>(4)(array.Handle, index, ref iid, out var pointer));
            var item = new ShellComObject(pointer);
            var retained = false;
            try
            {
                using var store = item.Query(PropertyStoreInterface);
                if (store is not null && ReadSeparator(store))
                {
                    if (entries.Count > 0 && !entries[^1].IsSeparator) entries.Add(new(0, "", null, true));
                    continue;
                }
                var label = store is null ? null : ResolveText(ReadString(store, TitleKey));
                using var shellItem = item.Query(ShellItemInterface);
                if (string.IsNullOrWhiteSpace(label) && shellItem is not null)
                {
                    Marshal.ThrowExceptionForHR(shellItem.Method<GetDisplayName>(5)(shellItem.Handle, 0, out var name));
                    try { label = Marshal.PtrToStringUni(name); }
                    finally { Marshal.FreeCoTaskMem(name); }
                }
                if (string.IsNullOrWhiteSpace(label)) continue;
                var id = ++_nextCommand;
                entries.Add(new(id, label, ReadIcon(item)));
                _commands.Add(id, item);
                retained = true;
            }
            finally { if (!retained) item.Dispose(); }
        }
        if (entries.Count > 0 && entries[^1].IsSeparator) entries.RemoveAt(entries.Count - 1);
        if (entries.Count > 0) groups.Add(new(title, entries));
    }

    private static string? ReadString(ShellComObject store, PropertyKey key)
    {
        Marshal.ThrowExceptionForHR(store.Method<GetValue>(5)(store.Handle, ref key, out var value));
        try { return value.Type is 31 or 8 ? Marshal.PtrToStringUni(value.Pointer) : null; }
        finally { PropVariantClear(ref value); }
    }

    private static bool ReadSeparator(ShellComObject store)
    {
        var key = SeparatorKey;
        Marshal.ThrowExceptionForHR(store.Method<GetValue>(5)(store.Handle, ref key, out var value));
        try { return value.Type == 11 && value.Bool != 0; }
        finally { PropVariantClear(ref value); }
    }

    private static string? ResolveText(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.StartsWith('@')) return text;
        var resolved = new StringBuilder(1024);
        Marshal.ThrowExceptionForHR(SHLoadIndirectString(text, resolved, (uint)resolved.Capacity, 0));
        return resolved.ToString();
    }

    private static BitmapSource? ReadIcon(ShellComObject item)
    {
        using var factory = item.Query(ImageFactoryInterface);
        if (factory is not null)
        {
            var result = factory.Method<GetImage>(3)(factory.Handle, new NativeSize(24, 24), 4, out var bitmap);
            if (result >= 0 && bitmap != 0)
            {
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
                finally { DeleteObject(bitmap); }
            }
        }
        // Tasks are often IShellLink objects with an explicit resource, rather than Shell images.
        using var link = item.Query(ShellLinkInterface);
        if (link is null) return null;
        var path = new StringBuilder(2048);
        var iconResult = link.Method<GetIconLocation>(16)(link.Handle, path, path.Capacity, out var iconIndex);
        if (iconResult < 0) return null;
        if (path.Length == 0)
        {
            var pathResult = link.Method<GetLinkPath>(3)(link.Handle, path, path.Capacity, 0, 4);
            if (pathResult < 0 || path.Length == 0) return null;
            iconIndex = 0;
        }
        return null; // (OpenTaskbar extracts the resource icon here; Vela shows text-only tasks)
    }

    private static bool MissingList(int result) => result is unchecked((int)0x80030002) or unchecked((int)0x80070002);

    public void Invoke(int commandId, nint owner)
    {
        if (!_commands.TryGetValue(commandId, out var item)) throw new InvalidOperationException("Jump List session expired.");
        using var store = item.Query(PropertyStoreInterface);
        var appId = store is null ? null : ReadString(store, AppIdKey);
        // Packaged task links activate their AppsFolder target with the published activation context.
        if (appId?.Contains('!') == true)
        {
            var folder = new Guid("1E87508D-89C2-42F0-8A7E-645A0F50CA58");
            var iid = ShellItemInterface;
            Marshal.ThrowExceptionForHR(SHCreateItemInKnownFolder(ref folder, 0, appId, ref iid, out var pointer));
            using var target = new ShellComObject(pointer);
            using var properties = Bind(target, new("0384E1A4-1523-439C-A4C8-AB911052F586"), PropertyStoreInterface);
            var key = HostEnvironmentKey;
            Marshal.ThrowExceptionForHR(properties.Method<GetValue>(5)(properties.Handle, ref key, out var host));
            try
            {
                if (host.Type == 19 && host.UInt32 != 0)
                {
                    using var targetMenu = Bind(target, new("3981E225-F559-11D3-8E3A-00C04F6837D5"), ContextMenuInterface);
                    InvokeDefault(targetMenu, owner, store is null ? null : ReadString(store, ActivationContextKey));
                    return;
                }
            }
            finally { PropVariantClear(ref host); }
        }
        using var direct = item.Query(ContextMenuInterface);
        if (direct is not null)
        {
            InvokeDefault(direct, owner, null);
            return;
        }
        using var shellItem = item.Query(ShellItemInterface);
        if (shellItem is null) throw new COMException("No Shell invocation interface.");
        using var bound = Bind(shellItem, new("3981E225-F559-11D3-8E3A-00C04F6837D5"), ContextMenuInterface);
        InvokeDefault(bound, owner, null);
    }

    private static ShellComObject Bind(ShellComObject item, Guid handler, Guid iid)
    {
        Marshal.ThrowExceptionForHR(item.Method<BindHandler>(3)(item.Handle, 0, ref handler, ref iid, out var pointer));
        return new ShellComObject(pointer);
    }

    private static void InvokeDefault(ShellComObject menu, nint owner, string? activationContext)
    {
        var nativeMenu = CreatePopupMenu();
        if (nativeMenu == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            Marshal.ThrowExceptionForHR(menu.Method<QueryMenu>(3)(menu.Handle, nativeMenu, 0, 1, 0x7fff, 1));
            var defaultId = GetMenuDefaultItem(nativeMenu, false, 0);
            if (defaultId == uint.MaxValue || defaultId == 0) throw new InvalidOperationException("Shell default command unavailable.");
            var command = new InvokeInfo
            {
                Size = Marshal.SizeOf<InvokeInfo>(), Mask = 0x4000, Window = owner,
                Verb = (nint)(defaultId - 1), VerbW = (nint)(defaultId - 1), Show = 1,
                Parameters = activationContext, ParametersW = activationContext
            };
            Marshal.ThrowExceptionForHR(menu.Method<InvokeMenu>(4)(menu.Handle, ref command));
        }
        finally { DestroyMenu(nativeMenu); }
    }

    public void Dispose()
    {
        foreach (var item in _commands.Values) item.Dispose();
        _commands.Clear();
    }

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey(Guid format, uint id) { public Guid Format = format; public uint Id = id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct Variant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Pointer; [FieldOffset(8)] public short Bool; [FieldOffset(8)] public uint UInt32; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] private struct Category { [FieldOffset(0)] public int Type; [FieldOffset(8)] public nint NameOrKind; [FieldOffset(8)] public int Kind; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeSize(int width, int height) { public readonly int Width = width; public readonly int Height = height; }
    [StructLayout(LayoutKind.Sequential)] private struct InvokeInfo
    {
        public int Size; public uint Mask; public nint Window; public nint Verb;
        [MarshalAs(UnmanagedType.LPStr)] public string? Parameters;
        public nint Directory; public int Show; public uint HotKey; public nint Icon; public nint Title; public nint VerbW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ParametersW;
        public nint DirectoryW; public nint TitleW; public int PointX; public int PointY;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetAppId(nint self, [MarshalAs(UnmanagedType.LPWStr)] string id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetCount(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetCategory(nint self, uint index, int flags, out Category category);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumerateCategory(nint self, uint index, ref Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InitializeAutomatic(nint self, [MarshalAs(UnmanagedType.LPWStr)] string id, nint link, nint extra);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetAutomaticList(nint self, int kind, uint max, uint flags, ref Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ResolveWindow(nint self, nint window, out nint id, nint prevented, nint explicitId, nint embedded);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ResolveShortcut(nint self, nint item, out nint id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetValue(nint self, ref PropertyKey key, out Variant value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetAt(nint self, uint index, ref Guid iid, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDisplayName(nint self, uint kind, out nint name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetImage(nint self, NativeSize size, uint flags, out nint bitmap);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] private delegate int GetIconLocation(nint self, StringBuilder path, int length, out int index);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] private delegate int GetLinkPath(nint self, StringBuilder path, int length, nint findData, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int BindHandler(nint self, nint context, ref Guid handler, ref Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QueryMenu(nint self, nint menu, uint index, uint first, uint last, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InvokeMenu(nint self, ref InvokeInfo info);
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(nint window, ref Guid iid, out nint store);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, out nint item);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemInKnownFolder(ref Guid folder, uint flags, string path, ref Guid iid, out nint item);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int SHLoadIndirectString(string source, StringBuilder output, uint count, nint reserved);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref Variant value);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint bitmap);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern uint GetMenuDefaultItem(nint menu, [MarshalAs(UnmanagedType.Bool)] bool byPosition, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(nint menu);
}
public sealed class ApplicationJumpListService : IDisposable
{
    private readonly object _gate = new();
    private Task<Dispatcher>? _worker;
    private NativeJumpListSession? _session;
    private long _sessionId;
    private volatile bool _disposed;

    public async Task<ApplicationJumpList> ReadAsync(nint window, string? shortcut, CancellationToken cancellation)
    {
        var dispatcher = await GetWorker().WaitAsync(cancellation);
        return await dispatcher.InvokeAsync(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            _session?.Dispose();
            _session = new NativeJumpListSession();
            var id = ++_sessionId;
            try
            {
                var result = _session.Read(id, window, shortcut, cancellation);
                cancellation.ThrowIfCancellationRequested();
                return result;
            }
            catch (OperationCanceledException)
            {
                _session.Dispose();
                _session = null;
                throw;
            }
            catch (COMException ex)
            {
                _session.Dispose();
                _session = null;
                Trace.TraceWarning("Jump List read failed: 0x{0:X8}", ex.HResult);
                var unsupported = ex.HResult is unchecked((int)0x80004002) or unchecked((int)0x80040154);
                return new ApplicationJumpList(id, unsupported ? JumpListStatus.Unsupported : JumpListStatus.Unavailable, []);
            }
            catch (InvalidOperationException)
            {
                _session.Dispose();
                _session = null;
                return new ApplicationJumpList(id, JumpListStatus.Unavailable, []);
            }
        }, DispatcherPriority.Normal, cancellation).Task.WaitAsync(cancellation);
    }

    public async Task<bool> InvokeAsync(long sessionId, int commandId, nint owner)
    {
        try
        {
            var dispatcher = await GetWorker();
            return await dispatcher.InvokeAsync(() =>
            {
                if (_disposed || _session is null || _sessionId != sessionId) return false;
                try { _session.Invoke(commandId, owner); return true; }
                catch (COMException ex) { Trace.TraceWarning("Jump List invocation failed: 0x{0:X8}", ex.HResult); return false; }
                catch (InvalidOperationException) { return false; }
                catch (Win32Exception ex) { Trace.TraceWarning("Jump List invocation failed: {0}", ex.NativeErrorCode); return false; }
            }).Task;
        }
        catch (OperationCanceledException) when (_disposed) { return false; }
        catch (ObjectDisposedException) when (_disposed) { return false; }
    }

    public async Task ReleaseAsync(long sessionId)
    {
        if (_disposed) return;
        try
        {
            var dispatcher = await GetWorker();
            await dispatcher.InvokeAsync(() =>
            {
                if (_sessionId != sessionId) return;
                _session?.Dispose();
                _session = null;
            }).Task;
        }
        catch (OperationCanceledException) when (_disposed) { /* The worker's finally releases the session. */ }
        catch (ObjectDisposedException) when (_disposed) { /* Shutdown owns cleanup. */ }
    }

    private Task<Dispatcher> GetWorker()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_worker is not null) return _worker;
            var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
            _worker = ready.Task;
            var thread = new Thread(() =>
            {
                var result = CoInitializeEx(0, 2);
                if (result < 0) { ready.SetException(new COMException("Shell STA initialization failed.", result)); return; }
                try
                {
                    ready.SetResult(Dispatcher.CurrentDispatcher);
                    if (_disposed) Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    Dispatcher.Run();
                }
                finally
                {
                    _session?.Dispose();
                    _session = null;
                    CoUninitialize();
                }
            }) { IsBackground = true, Name = "Vela Jump Lists" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return _worker;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_worker?.IsCompletedSuccessfully == true)
                _worker.Result.BeginInvokeShutdown(DispatcherPriority.Background);
        }
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
