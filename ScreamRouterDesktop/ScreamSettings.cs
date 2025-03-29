using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace ScreamRouterDesktop
{
    public class ScreamSettings
    {
        private Process? senderProcess;
        private Process? receiverProcess;

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
