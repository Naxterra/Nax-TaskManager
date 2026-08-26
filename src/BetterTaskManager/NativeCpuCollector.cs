using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace BetterTaskManager
{
    internal struct SystemCpuTimes
    {
        public ulong Idle;
        public ulong Kernel;
        public ulong User;
    }

    internal sealed class SystemCpuSnapshot
    {
        public DateTime Timestamp;
        public bool SampleAvailable;
        public double UsagePercent;
    }

    internal sealed class NativeCpuCollector
    {
        private readonly object syncRoot = new object();
        private bool hasPrevious;
        private SystemCpuTimes previous;

        public SystemCpuSnapshot GetSnapshot()
        {
            SystemCpuTimes current = ReadTimes();
            lock (syncRoot)
            {
                double usage = 0;
                bool available = hasPrevious && TryCalculateUsage(previous, current, out usage);
                previous = current;
                hasPrevious = true;
                return new SystemCpuSnapshot
                {
                    Timestamp = DateTime.Now,
                    SampleAvailable = available,
                    UsagePercent = available ? usage : 0
                };
            }
        }

        internal static bool TryCalculateUsage(SystemCpuTimes previous, SystemCpuTimes current, out double usagePercent)
        {
            usagePercent = 0;
            if (current.Idle < previous.Idle || current.Kernel < previous.Kernel || current.User < previous.User) return false;

            ulong idleDelta = current.Idle - previous.Idle;
            ulong kernelDelta = current.Kernel - previous.Kernel;
            ulong userDelta = current.User - previous.User;
            ulong totalDelta = kernelDelta + userDelta;
            if (totalDelta == 0 || idleDelta > totalDelta) return false;

            usagePercent = Math.Round((totalDelta - idleDelta) * 100d / totalDelta, 1);
            usagePercent = Math.Max(0, Math.Min(100, usagePercent));
            return true;
        }

        private static SystemCpuTimes ReadTimes()
        {
            FILETIME idle;
            FILETIME kernel;
            FILETIME user;
            if (!GetSystemTimes(out idle, out kernel, out user)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return new SystemCpuTimes
            {
                Idle = ToUInt64(idle),
                Kernel = ToUInt64(kernel),
                User = ToUInt64(user)
            };
        }

        private static ulong ToUInt64(FILETIME value)
        {
            return ((ulong)(uint)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);
    }
}
