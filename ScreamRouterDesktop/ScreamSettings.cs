using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ScreamRouterDesktop
{
    public class ScreamSettings
    {
        private Process? senderProcess;
        private Process? receiverProcess;
        private IntPtr jobHandle;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? name);

        [DllImport("kernel32.dll")]
        static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll")]
        static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        private enum JobObjectInfoType
        {
            ExtendedLimitInformation = 9
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public Int64 PerProcessUserTimeLimit;
            public Int64 PerJobUserTimeLimit;
            public UInt32 LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public UInt32 ActiveProcessLimit;
            public UIntPtr Affinity;
            public UInt32 PriorityClass;
            public UInt32 SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public UInt64 ReadOperationCount;
            public UInt64 WriteOperationCount;
            public UInt64 OtherOperationCount;
            public UInt64 ReadTransferCount;
            public UInt64 WriteTransferCount;
            public UInt64 OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        public ScreamSettings()
        {
            // Create the job object
            jobHandle = CreateJobObject(IntPtr.Zero, null);

            // Configure the job object to kill processes when the job is closed
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE

            var length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            var extendedInfoPtr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, extendedInfoPtr, false);
                SetInformationJobObject(jobHandle, JobObjectInfoType.ExtendedLimitInformation, extendedInfoPtr, (uint)length);
            }
            finally
            {
                Marshal.FreeHGlobal(extendedInfoPtr);
            }
        }

        ~ScreamSettings()
        {
            if (jobHandle != IntPtr.Zero)
            {
                CloseHandle(jobHandle);
            }
        }

        public bool SenderEnabled { get; set; }
        public string SenderIP { get; set; } = "127.0.0.1";
        public int SenderPort { get; set; } = 16401;
        public bool SenderMulticast { get; set; }

        public bool ReceiverEnabled { get; set; }
        public int ReceiverPort { get; set; } = 4010;

        public void Save()
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\ScreamRouterDesktop");
            key.SetValue("SenderEnabled", SenderEnabled);
            key.SetValue("SenderIP", SenderIP);
            key.SetValue("SenderPort", SenderPort);
            key.SetValue("SenderMulticast", SenderMulticast);
            key.SetValue("ReceiverEnabled", ReceiverEnabled);
            key.SetValue("ReceiverPort", ReceiverPort);
        }

        public void Load()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\ScreamRouterDesktop");
            if (key != null)
            {
                SenderEnabled = Convert.ToBoolean(key.GetValue("SenderEnabled", false));
                SenderIP = (string?)key.GetValue("SenderIP", "127.0.0.1") ?? "127.0.0.1";
                SenderPort = Convert.ToInt32(key.GetValue("SenderPort", 16401));
                SenderMulticast = Convert.ToBoolean(key.GetValue("SenderMulticast", false));
                ReceiverEnabled = Convert.ToBoolean(key.GetValue("ReceiverEnabled", false));
                ReceiverPort = Convert.ToInt32(key.GetValue("ReceiverPort", 4010));
            }
        }

        public void StartProcesses()
        {
            if (SenderEnabled && (senderProcess == null || senderProcess.HasExited))
            {
                string args = $"{SenderIP} {SenderPort}";
                if (SenderMulticast) args += " -m";
                
                senderProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScreamSender.exe"),
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                senderProcess.Start();
                AssignProcessToJobObject(jobHandle, senderProcess.Handle);
            }

            if (ReceiverEnabled && (receiverProcess == null || receiverProcess.HasExited))
            {
                receiverProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScreamReceiver.exe"),
                        Arguments = ReceiverPort.ToString(),
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                receiverProcess.Start();
                AssignProcessToJobObject(jobHandle, receiverProcess.Handle);
            }
        }

        public void StopProcesses()
        {
            if (senderProcess != null && !senderProcess.HasExited)
            {
                senderProcess.Kill();
                senderProcess.Dispose();
                senderProcess = null;
            }

            if (receiverProcess != null && !receiverProcess.HasExited)
            {
                receiverProcess.Kill();
                receiverProcess.Dispose();
                receiverProcess = null;
            }
        }

        public void RestartProcesses()
        {
            StopProcesses();
            StartProcesses();
        }
    }
}
