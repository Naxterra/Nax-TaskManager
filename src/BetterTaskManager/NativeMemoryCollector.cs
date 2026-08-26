using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BetterTaskManager
{
    internal sealed class SystemMemorySnapshot
    {
        public DateTime Timestamp { get; set; }
        public ulong PhysicalTotalBytes { get; set; }
        public ulong PhysicalAvailableBytes { get; set; }
        public ulong CommitTotalBytes { get; set; }
        public ulong CommitLimitBytes { get; set; }
        public ulong CommitPeakBytes { get; set; }
        public ulong SystemCacheBytes { get; set; }
        public ulong KernelPagedBytes { get; set; }
        public ulong KernelNonpagedBytes { get; set; }
        public uint ProcessCount { get; set; }
        public uint ThreadCount { get; set; }
        public uint HandleCount { get; set; }

        public ulong PhysicalUsedBytes => PhysicalTotalBytes >= PhysicalAvailableBytes ? PhysicalTotalBytes - PhysicalAvailableBytes : 0;
        public double PhysicalLoadPercent => PhysicalTotalBytes == 0 ? 0 : PhysicalUsedBytes * 100d / PhysicalTotalBytes;
    }

    internal static class NativeMemoryCollector
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PerformanceInformation
        {
            public uint Size;
            public UIntPtr CommitTotal;
            public UIntPtr CommitLimit;
            public UIntPtr CommitPeak;
            public UIntPtr PhysicalTotal;
            public UIntPtr PhysicalAvailable;
            public UIntPtr SystemCache;
            public UIntPtr KernelTotal;
            public UIntPtr KernelPaged;
            public UIntPtr KernelNonpaged;
            public UIntPtr PageSize;
            public uint HandleCount;
            public uint ProcessCount;
            public uint ThreadCount;
        }

        [DllImport("psapi.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPerformanceInfo(out PerformanceInformation performanceInformation, uint structureSize);

        public static SystemMemorySnapshot GetSnapshot()
        {
            uint structureSize = checked((uint)Marshal.SizeOf<PerformanceInformation>());
            PerformanceInformation information;
            if (!GetPerformanceInfo(out information, structureSize)) throw new Win32Exception(Marshal.GetLastWin32Error(), "GetPerformanceInfo failed.");

            ulong pageSize = ToUInt64(information.PageSize);
            return new SystemMemorySnapshot
            {
                Timestamp = DateTime.Now,
                PhysicalTotalBytes = PagesToBytes(information.PhysicalTotal, pageSize),
                PhysicalAvailableBytes = PagesToBytes(information.PhysicalAvailable, pageSize),
                CommitTotalBytes = PagesToBytes(information.CommitTotal, pageSize),
                CommitLimitBytes = PagesToBytes(information.CommitLimit, pageSize),
                CommitPeakBytes = PagesToBytes(information.CommitPeak, pageSize),
                SystemCacheBytes = PagesToBytes(information.SystemCache, pageSize),
                KernelPagedBytes = PagesToBytes(information.KernelPaged, pageSize),
                KernelNonpagedBytes = PagesToBytes(information.KernelNonpaged, pageSize),
                HandleCount = information.HandleCount,
                ProcessCount = information.ProcessCount,
                ThreadCount = information.ThreadCount
            };
        }

        private static ulong PagesToBytes(UIntPtr pages, ulong pageSize)
        {
            return checked(ToUInt64(pages) * pageSize);
        }

        private static ulong ToUInt64(UIntPtr value)
        {
            return UIntPtr.Size == sizeof(ulong) ? value.ToUInt64() : value.ToUInt32();
        }
    }
}
