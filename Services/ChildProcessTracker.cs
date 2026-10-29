using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace LockStepAI.Services
{
    public static class ChildProcessTracker
    {
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private const int JobObjectExtendedLimitInformation = 9;
        private static IntPtr _jobHandle;

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimitInformation
        {
            public BasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr job, int informationClass, IntPtr information, uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        public static void CleanupOrphanedProcesses(string executablePath)
        {
            string expectedPath = Path.GetFullPath(executablePath);
            int currentProcessId = Environment.ProcessId;

            // 🚨 Ask Windows ONLY for processes named "llama-server". This stops all the Access Denied spam.
            foreach (var process in Process.GetProcessesByName("llama-server"))
            {
                using (process)
                {
                    try
                    {
                        if (process.Id != currentProcessId &&
                            string.Equals(process.MainModule?.FileName, expectedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            process.Kill(entireProcessTree: true);
                            process.WaitForExit(3000);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ProcessTracker] Could not clean up process {process.Id}: {ex.Message}");
                    }
                }
            }
        }

        public static void Initialize()
        {
            if (_jobHandle != IntPtr.Zero) return;

            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());

            var limits = new ExtendedLimitInformation
            {
                BasicLimitInformation = new BasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose
                }
            };
            int size = Marshal.SizeOf<ExtendedLimitInformation>();
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(limits, buffer, false);
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)size))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                _jobHandle = job;
                job = IntPtr.Zero;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                if (job != IntPtr.Zero) CloseHandle(job);
            }
        }

        public static void Track(Process process)
        {
            if (_jobHandle == IntPtr.Zero) throw new InvalidOperationException("Child process tracking has not been initialized.");

            try
            {
                if (!AssignProcessToJobObject(_jobHandle, process.Handle))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch { }
                throw;
            }
        }

        public static void Dispose()
        {
            IntPtr job = _jobHandle;
            _jobHandle = IntPtr.Zero;
            if (job != IntPtr.Zero) CloseHandle(job);
        }
    }
}