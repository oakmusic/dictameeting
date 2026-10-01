using System.Runtime.InteropServices;
using DictaMeeting.Transcription.Models;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.Transcription.Services;

public static class HardwareDetector
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    public static HardwareCapabilities Detect(ILogger? logger = null)
    {
        int cores = Environment.ProcessorCount;
        double totalRamGb = 8.0;

        try
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                totalRamGb = Math.Round((double)memStatus.ullTotalPhys / (1024 * 1024 * 1024), 1);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "No se pudo consultar la memoria física mediante GlobalMemoryStatusEx.");
        }

        bool hasDedicatedGpu = false;
        string gpuName = "CPU / Gráficos integrados";
        double? vramGb = null;

        // 1. Recomendación en vivo (streaming): Qwen3 0.6B como modelo predeterminado por su fluidez, baja latencia y jerga técnica
        ModelSize liveRecommended;
        if (totalRamGb < 4.0)
        {
            liveRecommended = ModelSize.Tiny;
        }
        else
        {
            liveRecommended = ModelSize.Qwen3_06B;
        }

        // 2. Recomendación final (batch / consolidación): Qwen3 1.7B por defecto para máxima fidelidad y robustez fonética
        ModelSize finalRecommended;
        if (totalRamGb < 4.0)
        {
            finalRecommended = ModelSize.Tiny;
        }
        else
        {
            finalRecommended = ModelSize.Qwen3_17B;
        }

        return new HardwareCapabilities
        {
            CpuLogicalCores = cores,
            TotalRamGb = totalRamGb,
            HasDedicatedGpu = hasDedicatedGpu,
            GpuName = gpuName,
            VramGb = vramGb,
            RecommendedModel = liveRecommended,
            RecommendedLiveModel = liveRecommended,
            RecommendedFinalModel = finalRecommended
        };
    }
}
