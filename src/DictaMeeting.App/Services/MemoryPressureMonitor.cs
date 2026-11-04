using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.App.Services;

public enum MemoryPressureLevel
{
    Low,
    Medium,
    High,
    Critical
}

public readonly record struct SystemMemorySnapshot(
    long AvailablePhysicalMB,
    long TotalPhysicalMB,
    uint MemoryLoadPercent,
    long ProcessWorkingSetMB,
    long ProcessPrivateBytesMB,
    long ManagedHeapMB,
    MemoryPressureLevel PressureLevel);

public interface IMemoryPressureMonitor
{
    SystemMemorySnapshot GetCurrentSnapshot();
    bool CanSafelyLoadModel(long estimatedModelBytes);
    bool ShouldUnloadInactiveModels();
    MemoryPressureLevel CurrentPressureLevel { get; }
}

public sealed class MemoryPressureMonitor : IMemoryPressureMonitor
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    private readonly ILogger<MemoryPressureMonitor>? _logger;
    private readonly object _lock = new();
    private SystemMemorySnapshot _lastSnapshot;
    private long _lastSnapshotTicks;
    private const long CacheValidityTicks = 500 * TimeSpan.TicksPerMillisecond; // 500ms throttle

    public MemoryPressureLevel CurrentPressureLevel => GetCurrentSnapshot().PressureLevel;

    public MemoryPressureMonitor(ILogger<MemoryPressureMonitor>? logger = null)
    {
        _logger = logger;
    }

    public SystemMemorySnapshot GetCurrentSnapshot()
    {
        long now = Stopwatch.GetTimestamp();
        lock (_lock)
        {
            if (_lastSnapshotTicks > 0 && (now - _lastSnapshotTicks) < CacheValidityTicks)
            {
                return _lastSnapshot;
            }

            _lastSnapshot = CaptureSnapshot();
            _lastSnapshotTicks = now;
            return _lastSnapshot;
        }
    }

    public bool CanSafelyLoadModel(long estimatedModelBytes)
    {
        var snapshot = GetCurrentSnapshot();
        long estimatedMB = estimatedModelBytes / (1024 * 1024);

        // Se requiere mantener un margen de seguridad de al menos 800 MB para el sistema operativo
        long safeAvailableMB = snapshot.AvailablePhysicalMB - 800;
        return safeAvailableMB > estimatedMB && snapshot.PressureLevel != MemoryPressureLevel.Critical;
    }

    public bool ShouldUnloadInactiveModels()
    {
        var snapshot = GetCurrentSnapshot();
        // Descargar modelos si el sistema tiene presión alta/crítica o menos de 2 GB de RAM libre
        return snapshot.PressureLevel >= MemoryPressureLevel.High || snapshot.AvailablePhysicalMB < 2048;
    }

    private SystemMemorySnapshot CaptureSnapshot()
    {
        long availPhysMB = 0;
        long totalPhysMB = 0;
        uint memLoad = 0;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    availPhysMB = (long)(memStatus.ullAvailPhys / (1024 * 1024));
                    totalPhysMB = (long)(memStatus.ullTotalPhys / (1024 * 1024));
                    memLoad = memStatus.dwMemoryLoad;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "No se pudo consultar GlobalMemoryStatusEx.");
        }

        long workingSetMB = 0;
        long privateBytesMB = 0;
        try
        {
            using var proc = Process.GetCurrentProcess();
            workingSetMB = proc.WorkingSet64 / (1024 * 1024);
            privateBytesMB = proc.PrivateMemorySize64 / (1024 * 1024);
        }
        catch { }

        long managedHeapMB = GC.GetTotalMemory(false) / (1024 * 1024);

        var pressure = MemoryPressureLevel.Low;
        if (availPhysMB > 0)
        {
            if (availPhysMB < 800 || memLoad >= 92)
            {
                pressure = MemoryPressureLevel.Critical;
            }
            else if (availPhysMB < 1600 || memLoad >= 85)
            {
                pressure = MemoryPressureLevel.High;
            }
            else if (availPhysMB < 3000 || memLoad >= 75)
            {
                pressure = MemoryPressureLevel.Medium;
            }
            else
            {
                pressure = MemoryPressureLevel.Low;
            }
        }

        return new SystemMemorySnapshot(
            AvailablePhysicalMB: availPhysMB,
            TotalPhysicalMB: totalPhysMB,
            MemoryLoadPercent: memLoad,
            ProcessWorkingSetMB: workingSetMB,
            ProcessPrivateBytesMB: privateBytesMB,
            ManagedHeapMB: managedHeapMB,
            PressureLevel: pressure);
    }
}