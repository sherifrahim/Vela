// GPU temperature without a driver or kernel module: WDDM's own adapter performance data (the number Task Manager
// shows), with NVIDIA's NVML from System32 as a fallback.
// Ported from OpenTaskbar (MIT License, Copyright (c) 2026 OpenTaskbar contributors, github.com/alztrk/opentaskbar;
// full notice in THIRD_PARTY_NOTICES.md): Services/Interop/SystemSensors.cs (GpuThermalReader, Nvml).
using System.Runtime.InteropServices;
using Vela.Core;

namespace Vela.Services;

static class GpuThermal
{
    const double MaxPlausibleCelsius = 130;
    static int _mode; // 0 = not probed, 1 = D3DKMT, 2 = NVML, -1 = unavailable

    /// <summary>Hottest GPU in °C, or null when this PC exposes no GPU temperature.</summary>
    public static double? Hottest()
    {
        if (_mode == 0)
        {
            if (SampleD3dKmt() is { } t) { _mode = 1; Log.Info($"GPU temperature: WDDM adapter data ({t:0} °C)"); return t; }
            if (Nvml.TryInitialize()) { _mode = 2; Log.Info("GPU temperature: NVML"); }
            else { _mode = -1; Log.Info("GPU temperature: not available on this PC"); }
        }
        return _mode switch { 1 => SampleD3dKmt(), 2 => Nvml.Hottest(), _ => null };
    }

    static double? SampleD3dKmt()
    {
        const uint KMTQAITYPE_ADAPTERPERFDATA = 62;
        var enumArg = new D3dkmtEnumAdapters2();
        try
        {
            if (D3DKMTEnumAdapters2(ref enumArg) != 0 || enumArg.NumAdapters == 0) return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }

        int infoSize = Marshal.SizeOf<D3dkmtAdapterInfo>(), perfSize = Marshal.SizeOf<D3dkmtAdapterPerfData>();
        var buffer = Marshal.AllocHGlobal(infoSize * (int)enumArg.NumAdapters);
        var perfPtr = Marshal.AllocHGlobal(perfSize);
        try
        {
            enumArg.Adapters = buffer;
            if (D3DKMTEnumAdapters2(ref enumArg) != 0) return null;
            double? hottest = null;
            for (int i = 0; i < enumArg.NumAdapters; i++)
            {
                var info = Marshal.PtrToStructure<D3dkmtAdapterInfo>(buffer + i * infoSize);
                try
                {
                    var query = new D3dkmtQueryAdapterInfo { Adapter = info.Adapter, Type = KMTQAITYPE_ADAPTERPERFDATA, PrivateDriverData = perfPtr, PrivateDriverDataSize = (uint)perfSize };
                    if (D3DKMTQueryAdapterInfo(ref query) == 0)
                    {
                        double c = Marshal.PtrToStructure<D3dkmtAdapterPerfData>(perfPtr).Temperature / 10.0; // deci-°C
                        if (c is > 0 and < MaxPlausibleCelsius) hottest = Math.Max(hottest ?? 0, c);
                    }
                }
                finally
                {
                    var close = new D3dkmtCloseAdapter { Adapter = info.Adapter };
                    D3DKMTCloseAdapter(ref close);
                }
            }
            return hottest;
        }
        finally
        {
            Marshal.FreeHGlobal(perfPtr);
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)] struct Luid { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)]
    struct D3dkmtAdapterInfo
    {
        public uint Adapter;
        public Luid AdapterLuid;
        public uint NumOfSources;
        [MarshalAs(UnmanagedType.Bool)] public bool PresentMoveRegionsPreferred;
    }
    [StructLayout(LayoutKind.Sequential)] struct D3dkmtEnumAdapters2 { public uint NumAdapters; public IntPtr Adapters; }
    [StructLayout(LayoutKind.Sequential)] struct D3dkmtCloseAdapter { public uint Adapter; }
    [StructLayout(LayoutKind.Sequential)]
    struct D3dkmtQueryAdapterInfo { public uint Adapter; public uint Type; public IntPtr PrivateDriverData; public uint PrivateDriverDataSize; }
    [StructLayout(LayoutKind.Sequential)]
    struct D3dkmtAdapterPerfData
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency, MaxMemoryFrequency, MaxMemoryFrequencyOC, MemoryBandwidth, PcieBandwidth;
        public uint FanRpm, Power, Temperature;
        public byte PowerStateOverride;
    }

    [DllImport("gdi32.dll")] static extern int D3DKMTEnumAdapters2(ref D3dkmtEnumAdapters2 arg);
    [DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(ref D3dkmtCloseAdapter arg);
    [DllImport("gdi32.dll")] static extern int D3DKMTQueryAdapterInfo(ref D3dkmtQueryAdapterInfo arg);

    static class Nvml
    {
        public static bool TryInitialize()
        {
            try { return nvmlInit_v2() == 0; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
        }

        public static double? Hottest()
        {
            if (nvmlDeviceGetCount_v2(out uint count) != 0) return null;
            double? hottest = null;
            for (uint i = 0; i < count; i++)
                if (nvmlDeviceGetHandleByIndex_v2(i, out var device) == 0 && nvmlDeviceGetTemperature(device, 0, out uint t) == 0)
                    hottest = Math.Max(hottest ?? 0, t);
            return hottest;
        }

        // System32 only, so a library planted next to Vela.exe can't stand in for the driver's.
        [DllImport("nvml.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] static extern int nvmlInit_v2();
        [DllImport("nvml.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] static extern int nvmlDeviceGetCount_v2(out uint count);
        [DllImport("nvml.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
        [DllImport("nvml.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temperature);
    }
}
