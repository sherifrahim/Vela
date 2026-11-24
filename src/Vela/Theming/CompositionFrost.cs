// Composition frost: the system's own live backdrop of a window, blurred and saturated on the GPU, with an
// anti-aliased rounded clip. Recipe (HostBackdropBrush → Gaussian blur σ16 → saturation 1.65, geometric clip) from
// WacOS as published under the MIT License at commit 2beced5 (Copyright (c) 2026 renzyxx; notice in
// THIRD_PARTY_NOTICES.md). The code here is Vela's own: Windows.UI.Composition through the WinRT projection, and the
// two Direct2D effect descriptions as hand-made COM objects (no Win2D package).
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;
using Vela.Core;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace Vela.Theming;

/// <summary>
/// One frosted pane behind a window: a sprite visual on the window's own composition target, filled with the
/// blurred host backdrop and clipped to a rounded rectangle (anti-aliased, unlike a window region). DWM keeps it
/// live; Vela spends no CPU on it.
/// </summary>
internal sealed unsafe class CompositionFrost : IDisposable
{
    public const float BlurSigma = 16f, Saturation = 1.65f;

    static Compositor? _compositor;
    static CompositionEffectFactory? _factory;
    static IntPtr _queueController;
    static bool _broken;

    readonly DesktopWindowTarget _target;
    readonly SpriteVisual _visual;
    readonly CompositionRoundedRectangleGeometry _shape;
    readonly CompositionBrush _brush;

    CompositionFrost(DesktopWindowTarget target, SpriteVisual visual, CompositionRoundedRectangleGeometry shape, CompositionBrush brush)
    {
        _target = target; _visual = visual; _shape = shape; _brush = brush;
    }

    /// <summary>Frost for <paramref name="hwnd"/> (UI thread), or null when composition isn't available here.</summary>
    public static CompositionFrost? Create(IntPtr hwnd)
    {
        if (_broken || hwnd == IntPtr.Zero) return null;
        try
        {
            var c = Compositor();
            // Win32 windows must opt in to host-backdrop brushes.
            int on = 1;
            Interop.Native.DwmSetWindowAttribute(hwnd, 17 /*DWMWA_USE_HOSTBACKDROPBRUSH*/, ref on, sizeof(int));
            var target = TargetFor(c, hwnd);
            var backdrop = c.CreateHostBackdropBrush();
            CompositionBrush brush = backdrop;
            if (Factory(c) is { } f)
            {
                var fx = f.CreateBrush();
                fx.SetSourceParameter("backdrop", backdrop);
                brush = fx;
            }
            var shape = c.CreateRoundedRectangleGeometry();
            var visual = c.CreateSpriteVisual();
            visual.Brush = brush;
            visual.Clip = c.CreateGeometricClip(shape);
            target.Root = visual;
            return new CompositionFrost(target, visual, shape, brush);
        }
        catch (Exception ex)
        {
            _broken = true;
            Log.Warn("Composition frost unavailable, using accent blur: " + ex.Message);
            return null;
        }
    }

    /// <summary>Size and corner radius in physical px (the window's client area).</summary>
    public void Resize(int width, int height, double radius)
    {
        var size = new Vector2(width, height);
        _visual.Size = size;
        _shape.Size = size;
        _shape.CornerRadius = new Vector2((float)radius, (float)radius);
    }

    public void Dispose()
    {
        try { _target.Root = null; _visual.Dispose(); _brush.Dispose(); _shape.Dispose(); _target.Dispose(); } catch { }
    }

    // ---------- shared compositor ----------

    [StructLayout(LayoutKind.Sequential)] struct DispatcherQueueOptions { public int dwSize, threadType, apartmentType; }
    [DllImport("CoreMessaging.dll")] static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);

    static Compositor Compositor()
    {
        if (_compositor != null) return _compositor;
        // Composition needs a DispatcherQueue on this (the UI) thread; WPF's message loop pumps it.
        if (Windows.System.DispatcherQueue.GetForCurrentThread() == null)
        {
            var o = new DispatcherQueueOptions { dwSize = sizeof(DispatcherQueueOptions), threadType = 2 /*current*/, apartmentType = 2 /*STA*/ };
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(o, out _queueController));
        }
        return _compositor = new Compositor();
    }

    static DesktopWindowTarget TargetFor(Compositor c, IntPtr hwnd)
    {
        var iid = new Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"); // ICompositorDesktopInterop
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(((WinRT.IWinRTObject)c).NativeObject.ThisPtr, in iid, out var interop));
        try
        {
            IntPtr result;
            var vt = *(void***)interop;
            // isTopmost: drawn above the window's own (transparent) WPF content.
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr*, int>)vt[3])(interop, hwnd, 1, &result));
            try { return DesktopWindowTarget.FromAbi(result); }
            finally { Marshal.Release(result); }
        }
        finally { Marshal.Release(interop); }
    }

    /// <summary>Saturation(GaussianBlur(backdrop)), built once; null (plain host backdrop) if the effect is refused.</summary>
    static CompositionEffectFactory? Factory(Compositor c)
    {
        if (_factory != null) return _factory;
        IntPtr param = 0, blurSrc = 0, top = 0;
        try
        {
            var source = new CompositionEffectSourceParameter("backdrop");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(((WinRT.IWinRTObject)source).NativeObject.ThisPtr, in D2DEffect.IID_Source, out param));
            var blur = new D2DEffect(D2DEffect.GaussianBlur, [BlurSigma, 1u /*balanced*/, 1u /*hard edges: no dark rim*/], [param]);
            blurSrc = D2DEffect.As(blur, D2DEffect.IID_Source);
            var sat = new D2DEffect(D2DEffect.SaturationFx, [Saturation], [blurSrc]);
            top = D2DEffect.As(sat, D2DEffect.IID_Effect);
            IntPtr result;
            var self = ((WinRT.IWinRTObject)c).NativeObject.ThisPtr; // ICompositor (the default interface)
            var vt = *(void***)self;
            // ICompositor.CreateEffectFactory is slot 11 (read from the projection's own ABI code).
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)vt[11])(self, top, &result));
            try { return _factory = CompositionEffectFactory.FromAbi(result); }
            finally { Marshal.Release(result); }
        }
        catch (Exception ex)
        {
            Log.Warn("Frost effect unavailable, unblurred host backdrop: " + ex.Message);
            return null;
        }
        finally
        {
            if (top != 0) Marshal.Release(top);
            if (blurSrc != 0) Marshal.Release(blurSrc);
            if (param != 0) Marshal.Release(param);
        }
    }
}

/// <summary>
/// A Direct2D effect description for Windows.UI.Composition (what Win2D's effect classes are): a COM object with
/// IGraphicsEffect, IGraphicsEffectSource and IGraphicsEffectD2D1Interop, made with ComWrappers.
/// Properties are float or uint, by Direct2D property index.
/// </summary>
internal sealed unsafe class D2DEffect
{
    public static readonly Guid GaussianBlur = new("1FEB6D69-2FE6-4AC9-8C58-1D7F93E7A6A5");
    public static readonly Guid SaturationFx = new("5CB2D9CF-327D-459F-A0CE-40C0B2086BF7");

    public static readonly Guid IID_Effect = new("CB51C0CE-8FE6-4636-B202-861FAA07D8F3");
    public static readonly Guid IID_Source = new("2D8F9DDC-4339-4EB9-9216-F9DEB75658A2");
    static readonly Guid IID_Interop = new("2FC57384-A068-44D7-A331-30982FCF7177");
    static readonly Guid IID_Inspectable = new("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90");
    static readonly Guid IID_PropertyValue = new("4BD682DD-7554-40E9-9A9B-82654EDE7E62");
    static readonly Guid IID_PropertyValueStatics = new("629BDBC8-D932-4FF4-96B9-8D96C5C1E858");

    readonly Guid _clsid;
    readonly object[] _props;
    readonly IntPtr[] _sources;   // IGraphicsEffectSource*, one reference each, held for the object's life

    public D2DEffect(Guid clsid, object[] props, IntPtr[] sources)
    {
        _clsid = clsid; _props = props; _sources = sources;
        foreach (var s in sources) Marshal.AddRef(s);
    }

    ~D2DEffect() { foreach (var s in _sources) Marshal.Release(s); }

    /// <summary>A new reference to <paramref name="e"/> as interface <paramref name="iid"/>.</summary>
    public static IntPtr As(D2DEffect e, Guid iid)
    {
        var unk = Wrappers.GetOrCreateComInterfaceForObject(e, CreateComInterfaceFlags.None);
        try { Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unk, in iid, out var p)); return p; }
        finally { Marshal.Release(unk); }
    }

    static readonly EffectWrappers Wrappers = new();

    sealed class EffectWrappers : ComWrappers
    {
        static ComInterfaceEntry* _entries;

        protected override ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
        {
            count = 4;
            if (_entries != null) return _entries;
            GetIUnknownImpl(out var qi, out var addRef, out var release);

            // IInspectable-shaped: IGraphicsEffectSource and IInspectable itself.
            var insp = (IntPtr*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(D2DEffect), sizeof(IntPtr) * 6);
            insp[0] = qi; insp[1] = addRef; insp[2] = release;
            insp[3] = (IntPtr)(delegate* unmanaged<IntPtr, uint*, Guid**, int>)&GetIids;
            insp[4] = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&GetRuntimeClassName;
            insp[5] = (IntPtr)(delegate* unmanaged<IntPtr, int*, int>)&GetTrustLevel;

            var effect = (IntPtr*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(D2DEffect), sizeof(IntPtr) * 8);
            for (int i = 0; i < 6; i++) effect[i] = insp[i];
            effect[6] = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&GetName;
            effect[7] = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int>)&PutName;

            var interop = (IntPtr*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(D2DEffect), sizeof(IntPtr) * 9);
            interop[0] = qi; interop[1] = addRef; interop[2] = release;
            interop[3] = (IntPtr)(delegate* unmanaged<IntPtr, Guid*, int>)&GetEffectId;
            interop[4] = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, uint*, int*, int>)&GetNamedPropertyMapping;
            interop[5] = (IntPtr)(delegate* unmanaged<IntPtr, uint*, int>)&GetPropertyCount;
            interop[6] = (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr*, int>)&GetProperty;
            interop[7] = (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr*, int>)&GetSource;
            interop[8] = (IntPtr)(delegate* unmanaged<IntPtr, uint*, int>)&GetSourceCount;

            var e = (ComInterfaceEntry*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(D2DEffect), sizeof(ComInterfaceEntry) * 4);
            e[0] = new ComInterfaceEntry { IID = IID_Effect, Vtable = (IntPtr)effect };
            e[1] = new ComInterfaceEntry { IID = IID_Source, Vtable = (IntPtr)insp };
            e[2] = new ComInterfaceEntry { IID = IID_Inspectable, Vtable = (IntPtr)insp };
            e[3] = new ComInterfaceEntry { IID = IID_Interop, Vtable = (IntPtr)interop };
            return _entries = e;
        }

        protected override object CreateObject(IntPtr externalComObject, CreateObjectFlags flags) => throw new NotSupportedException();
        protected override void ReleaseObjects(System.Collections.IEnumerable objects) => throw new NotSupportedException();
    }

    static D2DEffect Self(IntPtr p) => ComWrappers.ComInterfaceDispatch.GetInstance<D2DEffect>((ComWrappers.ComInterfaceDispatch*)p);

    const int S_OK = 0, E_INVALIDARG = unchecked((int)0x80070057), E_BOUNDS = unchecked((int)0x8000000B);

    [UnmanagedCallersOnly] static int GetIids(IntPtr _, uint* count, Guid** iids) { *count = 0; *iids = null; return S_OK; }
    [UnmanagedCallersOnly] static int GetRuntimeClassName(IntPtr _, IntPtr* name) { *name = 0; return S_OK; }
    [UnmanagedCallersOnly] static int GetTrustLevel(IntPtr _, int* level) { *level = 0; return S_OK; }
    [UnmanagedCallersOnly] static int GetName(IntPtr _, IntPtr* name) { *name = 0; return S_OK; } // empty HSTRING
    [UnmanagedCallersOnly] static int PutName(IntPtr _, IntPtr name) => S_OK;
    [UnmanagedCallersOnly] static int GetEffectId(IntPtr p, Guid* id) { *id = Self(p)._clsid; return S_OK; }
    [UnmanagedCallersOnly] static int GetNamedPropertyMapping(IntPtr _, IntPtr name, uint* index, int* mapping) => E_INVALIDARG; // nothing animated
    [UnmanagedCallersOnly] static int GetPropertyCount(IntPtr p, uint* count) { *count = (uint)Self(p)._props.Length; return S_OK; }
    [UnmanagedCallersOnly] static int GetSourceCount(IntPtr p, uint* count) { *count = (uint)Self(p)._sources.Length; return S_OK; }

    [UnmanagedCallersOnly]
    static int GetSource(IntPtr p, uint index, IntPtr* source)
    {
        var s = Self(p)._sources;
        if (index >= s.Length) { *source = 0; return E_BOUNDS; }
        Marshal.AddRef(s[index]);
        *source = s[index];
        return S_OK;
    }

    [UnmanagedCallersOnly]
    static int GetProperty(IntPtr p, uint index, IntPtr* value)
    {
        *value = 0;
        var props = Self(p)._props;
        if (index >= props.Length) return E_BOUNDS;
        try { *value = PropertyValue(props[index]); return S_OK; }
        catch (Exception ex) { return ex.HResult; }
    }

    /// <summary>A Windows.Foundation.PropertyValue (IPropertyValue*) for a float or uint, from the system's own factory.</summary>
    static IntPtr PropertyValue(object v)
    {
        var factory = WinRT.ActivationFactory.Get("Windows.Foundation.PropertyValue");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(factory.ThisPtr, in IID_PropertyValueStatics, out var statics));
        IntPtr inspectable = 0;
        try
        {
            var vt = *(void***)statics;
            int hr = v switch
            {
                float f => ((delegate* unmanaged[Stdcall]<IntPtr, float, IntPtr*, int>)vt[14])(statics, f, &inspectable), // CreateSingle
                uint u => ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)vt[11])(statics, u, &inspectable),   // CreateUInt32
                _ => E_INVALIDARG,
            };
            Marshal.ThrowExceptionForHR(hr);
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(inspectable, in IID_PropertyValue, out var pv));
            return pv;
        }
        finally
        {
            if (inspectable != 0) Marshal.Release(inspectable);
            Marshal.Release(statics);
        }
    }
}
