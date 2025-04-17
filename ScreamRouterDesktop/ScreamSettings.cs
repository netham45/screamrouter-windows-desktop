using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.IO;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ScreamRouterDesktop
{
    public class ScreamSettings : IDisposable // Implement IDisposable
    {
        private Process? senderProcess;
        private Process? receiverProcess;
        private Process? perProcessSenderProcess;
        private IntPtr jobHandle;
        private ZeroconfService? zeroconfService;
        private const string RegistryPath = @"Software\ScreamRouterDesktop"; // Define registry path constant

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

            // Initialize ZeroconfService
            zeroconfService = new ZeroconfService();
        }

        // Dispose pattern implementation
        private bool disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                    StopProcesses(); // Ensure processes and servers are stopped
                    zeroconfService?.Dispose();
                }

                // Free unmanaged resources (unmanaged objects) and override finalizer
                if (jobHandle != IntPtr.Zero)
                {
                    CloseHandle(jobHandle);
                    jobHandle = IntPtr.Zero; // Prevent double closing
                }

                disposed = true;
            }
        }

        // Finalizer (destructor) - only needed if you have unmanaged resources directly in this class
        ~ScreamSettings()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }


        public bool SenderEnabled { get; set; }
        public string SenderIP { get; set; } = ""; // Initialize to blank instead of 127.0.0.1
        public int SenderPort { get; set; } = 16401;
        public bool SenderMulticast { get; set; }

        public bool PerProcessSenderEnabled { get; set; }
        public string PerProcessSenderIP { get; set; } = "";
        public int PerProcessSenderPort { get; set; } = 16402;

        public bool ReceiverEnabled { get; set; }
        public int ReceiverPort { get; set; } = 4010;
        public string WebInterfaceUrl { get; set; } = string.Empty;

        // Unique ID for this receiver instance
        public string ReceiverID { get; private set; } = string.Empty; // Initialize as empty

        // StartAtBoot property that directly checks/sets the Windows startup registry
        public bool StartAtBoot
        {
            get
            {
                // Check if the application is in the Windows startup registry
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                return key != null && key.GetValue("ScreamRouterDesktop") != null;
            }
            set
            {
                // This will be applied through SetStartAtBoot method when Save() is called
                _startAtBootValue = value;
            }
        }

        // Private field to hold the value temporarily until Save() is called
        private bool _startAtBootValue;

        // Method to get the current audio settings from ZeroconfService
        public ZeroconfService.AudioSettings? GetCurrentAudioSettings()
        {
            if (zeroconfService != null)
            {
                return zeroconfService.GetCurrentAudioSettings();
            }
            return null;
        }

        public void Save()
        {
            Logger.Log("ScreamSettings", "Saving settings to registry");
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key.SetValue("SenderEnabled", SenderEnabled);
            key.SetValue("SenderIP", SenderIP);
            key.SetValue("SenderPort", SenderPort);
            key.SetValue("SenderMulticast", SenderMulticast);
            key.SetValue("PerProcessSenderEnabled", PerProcessSenderEnabled);
            key.SetValue("PerProcessSenderIP", PerProcessSenderIP);
            key.SetValue("PerProcessSenderPort", PerProcessSenderPort);
            key.SetValue("ReceiverEnabled", ReceiverEnabled);
            key.SetValue("ReceiverPort", ReceiverPort);
            key.SetValue("ReceiverID", ReceiverID); // Save ReceiverID
            key.SetValue("WebInterfaceUrl", WebInterfaceUrl);

            // Apply the start at boot setting to the OS
            SetStartAtBoot(_startAtBootValue);
        }

        // Set the application to start at boot in the OS
        private void SetStartAtBoot(bool enable)
        {
            Logger.Log("ScreamSettings", $"Setting start at boot to {enable}");
            string appPath = Application.ExecutablePath;
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

            if (key != null)
            {
                if (enable)
                {
                    key.SetValue("ScreamRouterDesktop", appPath);
                }
                else
                {
                    if (key.GetValue("ScreamRouterDesktop") != null)
                    {
                        key.DeleteValue("ScreamRouterDesktop", false);
                    }
                }
            }
        }

        public void Load()
        {
            Logger.Log("ScreamSettings", "Loading settings from registry");
            bool needsSave = false; // Flag to check if we need to save after loading
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true); // Open with write access if needed

            RegistryKey? writeKey = key; // Use the opened key if possible
            if (writeKey == null) // If key doesn't exist, create it
            {
                writeKey = Registry.CurrentUser.CreateSubKey(RegistryPath);
                needsSave = true; // Need to save since we created the key or ReceiverID
            }

            if (writeKey != null)
            {
                SenderEnabled = Convert.ToBoolean(writeKey.GetValue("SenderEnabled", false));

                // Load SenderIP: Check registry. Only query mDNS if the key is completely missing.
                object? senderIpValue = writeKey.GetValue("SenderIP");
                string? currentSenderIp = senderIpValue?.ToString(); // Convert to string once
                Logger.Log("ScreamSettings", $"Registry value for SenderIP read as: '{currentSenderIp ?? "null"}'");

                // Explicitly treat null OR empty string from registry as "not set"
                if (currentSenderIp == null || currentSenderIp == "")
                {
                    string logReason = senderIpValue == null ? "not found in registry" : "is empty string";
                    Logger.Log("ScreamSettings", $"SenderIP {logReason}. Querying mDNS for screamrouter.local...");
                    IPAddress? resolvedIp = ResolveHostnameViaMdns("screamrouter.local");
                    if (resolvedIp != null)
                    {
                        SenderIP = resolvedIp.ToString();
                        Logger.Log("ScreamSettings", $"Found screamrouter.local at {SenderIP}. Saving to registry.");
                        writeKey.SetValue("SenderIP", SenderIP); // Save the discovered IP
                    }
                    else
                    {
                        SenderIP = ""; // Leave blank if mDNS resolution fails
                        Logger.Log("ScreamSettings", "Could not resolve screamrouter.local via mDNS. Leaving SenderIP blank.");
                    }
                }
                else // Key exists and has a non-empty, non-null value, load it.
                {
                    SenderIP = currentSenderIp; // Value is already known to be non-null and non-empty
                    Logger.Log("ScreamSettings", $"Loaded non-empty SenderIP directly from registry: {SenderIP}");
                }

                SenderPort = Convert.ToInt32(writeKey.GetValue("SenderPort", 16401));
                SenderMulticast = Convert.ToBoolean(writeKey.GetValue("SenderMulticast", false));
                PerProcessSenderEnabled = Convert.ToBoolean(writeKey.GetValue("PerProcessSenderEnabled", false));

                // Load PerProcessSenderIP: Check registry. Treat null or empty string as unset and try mDNS.
                object? perProcessSenderIpValue = writeKey.GetValue("PerProcessSenderIP");
                string? currentPerProcessSenderIp = perProcessSenderIpValue?.ToString();
                Logger.Log("ScreamSettings", $"Registry value for PerProcessSenderIP read as: '{currentPerProcessSenderIp ?? "null"}'");

                // Explicitly treat null OR empty string from registry as "not set"
                if (currentPerProcessSenderIp == null || currentPerProcessSenderIp == "")
                {
                    string logReason = perProcessSenderIpValue == null ? "not found in registry" : "is empty string";
                    Logger.Log("ScreamSettings", $"PerProcessSenderIP {logReason}. Querying mDNS for screamrouter.local...");
                    IPAddress? resolvedIp = ResolveHostnameViaMdns("screamrouter.local"); // Reuse the same lookup
                    if (resolvedIp != null)
                    {
                        PerProcessSenderIP = resolvedIp.ToString();
                        Logger.Log("ScreamSettings", $"Found screamrouter.local at {PerProcessSenderIP}. Saving to registry for PerProcessSenderIP.");
                        writeKey.SetValue("PerProcessSenderIP", PerProcessSenderIP); // Save the discovered IP
                    }
                    else
                    {
                        PerProcessSenderIP = ""; // Leave blank if mDNS resolution fails
                        Logger.Log("ScreamSettings", "Could not resolve screamrouter.local via mDNS. Leaving PerProcessSenderIP blank.");
                    }
                }
                else // Key exists and has a non-empty, non-null value, load it.
                {
                    PerProcessSenderIP = currentPerProcessSenderIp; // Value is already known to be non-null and non-empty
                    Logger.Log("ScreamSettings", $"Loaded non-empty PerProcessSenderIP directly from registry: {PerProcessSenderIP}");
                }

                PerProcessSenderPort = Convert.ToInt32(writeKey.GetValue("PerProcessSenderPort", 16402));
                ReceiverEnabled = Convert.ToBoolean(writeKey.GetValue("ReceiverEnabled", false));
                ReceiverPort = Convert.ToInt32(writeKey.GetValue("ReceiverPort", 4010));

                // Load WebInterfaceUrl or look it up if not set
                object? webInterfaceUrlValue = writeKey.GetValue("WebInterfaceUrl");
                if (webInterfaceUrlValue == null || string.IsNullOrEmpty(webInterfaceUrlValue.ToString()))
                {
                    // URL not set, try to look it up using mDNS
                    WebInterfaceUrl = LookupWebInterfaceUrl();
                    if (!string.IsNullOrEmpty(WebInterfaceUrl))
                    {
                        // Save the discovered URL
                        writeKey.SetValue("WebInterfaceUrl", WebInterfaceUrl);
                        Logger.Log("ScreamSettings", $"Discovered and saved WebInterfaceUrl: {WebInterfaceUrl}");
                    }
                }
                else
                {
                    WebInterfaceUrl = webInterfaceUrlValue.ToString() ?? string.Empty;
                    Logger.Log("ScreamSettings", $"Loaded WebInterfaceUrl: {WebInterfaceUrl}");
                }

                // Load or generate ReceiverID
                object? receiverIdValue = writeKey.GetValue("ReceiverID");
                if (receiverIdValue == null || string.IsNullOrEmpty(receiverIdValue.ToString()))
                {
                    ReceiverID = Guid.NewGuid().ToString();
                    writeKey.SetValue("ReceiverID", ReceiverID);
                    Logger.Log("ScreamSettings", $"Generated new ReceiverID: {ReceiverID}");
                    needsSave = true; // Mark that we need to save the new ID (though we just wrote it)
                }
                else
                {
                    ReceiverID = receiverIdValue.ToString() ?? string.Empty;
                    Logger.Log("ScreamSettings", $"Loaded ReceiverID: {ReceiverID}");
                }

                // StartAtBoot is now read directly from Windows startup registry in the property getter
                _startAtBootValue = StartAtBoot;

                // Close the key if we opened it for writing
                if (key != writeKey) // Only close if we created a new key handle
                {
                    writeKey.Close();
                }
            }
            else // Should not happen if CreateSubKey worked, but handle defensively
            {
                Logger.Log("ScreamSettings", "Failed to open or create registry key for loading.");
                // Initialize ReceiverID if key couldn't be accessed at all
                if (string.IsNullOrEmpty(ReceiverID))
                {
                    ReceiverID = Guid.NewGuid().ToString();
                    Logger.Log("ScreamSettings", $"Generated fallback ReceiverID: {ReceiverID}");
                    // Cannot save it here as the key access failed
                }
            }

            // Pass the ReceiverID to the services
            zeroconfService?.SetReceiverID(ReceiverID);
        }

        // Check if the start at boot option has been prompted before
        public bool HasStartAtBootBeenPrompted()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key != null && key.GetValue("StartAtBootPrompted") != null;
        }

        // Mark that the start at boot option has been prompted
        public void SetStartAtBootPrompted()
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key.SetValue("StartAtBootPrompted", true);
        }

        // Show the start at boot dialog
        public void ShowStartAtBootDialog()
        {
            if (!HasStartAtBootBeenPrompted())
            {
                DialogResult result = MessageBox.Show(
                    "Would you like ScreamRouter Desktop to start automatically when you log in?",
                    "Start at Boot",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                _startAtBootValue = result == DialogResult.Yes;
                SetStartAtBootPrompted();
                Save();
            }
        }

        public void StartProcesses()
        {
            Logger.Log("ScreamSettings", "Starting processes");

            // Start the standard sender if enabled
            if (SenderEnabled && !PerProcessSenderEnabled && (senderProcess == null || senderProcess.HasExited))
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
                Logger.Log("ScreamSettings", $"Started sender process with args: {args}");
            }

            // Start the per-process sender if enabled
            if (PerProcessSenderEnabled && !SenderEnabled && (perProcessSenderProcess == null || perProcessSenderProcess.HasExited))
            {
                perProcessSenderProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScreamPerProcessSender.exe"),
                        Arguments = $"{PerProcessSenderIP} {PerProcessSenderPort}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                perProcessSenderProcess.Start();
                AssignProcessToJobObject(jobHandle, perProcessSenderProcess.Handle);
                Logger.Log("ScreamSettings", $"Started per-process sender with port: {PerProcessSenderPort}");
            }

            // Start the receiver if enabled
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
                Logger.Log("ScreamSettings", $"Started receiver process with port: {ReceiverPort}");

                // Start ZeroconfService when receiver is enabled
                if (zeroconfService != null)
                {
                    // Start ZeroconfService (for A record discovery)
                    zeroconfService.Start();
                    Logger.Log("ScreamSettings", "Started ZeroconfService for mDNS discovery");

                }
            }
        }

        public void StopProcesses()
        {
            Logger.Log("ScreamSettings", "Stopping processes");

            // Stop standard sender
            if (senderProcess != null && !senderProcess.HasExited)
            {
                senderProcess.Kill();
                senderProcess.Dispose();
                senderProcess = null;
            }

            // Stop per-process sender
            if (perProcessSenderProcess != null && !perProcessSenderProcess.HasExited)
            {
                perProcessSenderProcess.Kill();
                perProcessSenderProcess.Dispose();
                perProcessSenderProcess = null;
            }

            // Stop receiver
            if (receiverProcess != null && !receiverProcess.HasExited)
            {
                receiverProcess.Kill();
                receiverProcess.Dispose();
                receiverProcess = null;

                // Stop ZeroconfService when receiver is stopped
                if (zeroconfService != null)
                {
                    // Stop ZeroconfService
                    zeroconfService.Stop();
                    Logger.Log("ScreamSettings", "Stopped ZeroconfService");

                    // Stop DNS Server
                    Logger.Log("ScreamSettings", "Stopped DNS Server");
                }
            }
        }

        public void RestartProcesses()
        {
            Logger.Log("ScreamSettings", "Restarting processes");
            StopProcesses();
            StartProcesses();
        }

        // Method to look up the WebInterfaceUrl using mDNS
        private string LookupWebInterfaceUrl()
        {
            try
            {
                Logger.Log("ScreamSettings", "Looking up WebInterfaceUrl using mDNS");

                // Create a temporary UdpClient for mDNS queries
                using (UdpClient client = new UdpClient())
                {
                    // Configure the client for multicast
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    client.EnableBroadcast = true;

                    // Step 1: Resolve screamrouter.local to an IP address
                    IPAddress? ipAddress = ResolveHostnameViaMdns("screamrouter.local");
                    if (ipAddress == null)
                    {
                        Logger.Log("ScreamSettings", "Failed to resolve screamrouter.local via mDNS");
                        return string.Empty;
                    }

                    Logger.Log("ScreamSettings", $"Resolved screamrouter.local to {ipAddress}");

                    // Step 2: Do a reverse DNS lookup on the IP address
                    string? hostname = ReverseLookupViaMdns(ipAddress);
                    if (string.IsNullOrEmpty(hostname))
                    {
                        Logger.Log("ScreamSettings", $"Failed to do reverse lookup for {ipAddress}");
                        return string.Empty;
                    }

                    // Step 3: Remove trailing dot if present
                    hostname = hostname.TrimEnd('.');
                    Logger.Log("ScreamSettings", $"Reverse lookup result: {hostname}");

                    // Step 4: Construct the URL
                    string url = $"https://{ipAddress}/site/DesktopMenu";
                    Logger.Log("ScreamSettings", $"Constructed WebInterfaceUrl: {url}");

                    return url;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ScreamSettings", $"Error looking up WebInterfaceUrl: {ex.Message}");
                return string.Empty;
            }
        }

        // Helper method to resolve a hostname via mDNS
        private IPAddress? ResolveHostnameViaMdns(string hostname)
        {
            Logger.Log("ScreamSettings", $"Resolving hostname via mDNS: {hostname}");

            try
            {
                // Try to resolve using standard DNS
                try
                {
                    IPAddress[] addresses = Dns.GetHostAddresses(hostname);
                    foreach (IPAddress address in addresses)
                    {
                        if (address.AddressFamily == AddressFamily.InterNetwork) // IPv4
                        {
                            Logger.Log("ScreamSettings", $"Resolved {hostname} to {address} using standard DNS");
                            return address;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("ScreamSettings", $"Standard DNS resolution failed: {ex.Message}");
                }

                // If resolution fails, return null (leave setting blank)
                Logger.Log("ScreamSettings", "DNS resolution failed, leaving setting blank");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Log("ScreamSettings", $"Error in ResolveHostnameViaMdns: {ex.Message}");
                return null;
            }
        }

        // Helper method to do a reverse DNS lookup via mDNS
        private string? ReverseLookupViaMdns(IPAddress ipAddress)
        {
            Logger.Log("ScreamSettings", $"Doing reverse lookup via mDNS for: {ipAddress}");

            try
            {
                // Since we can't get a response via mDNS, use a fallback approach

                // Try standard reverse DNS lookup first
                try
                {
                    IPHostEntry hostEntry = Dns.GetHostEntry(ipAddress);
                    if (!string.IsNullOrEmpty(hostEntry.HostName))
                    {
                        Logger.Log("ScreamSettings", $"Resolved {ipAddress} to {hostEntry.HostName} using standard DNS");
                        return hostEntry.HostName;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("ScreamSettings", $"Standard reverse DNS lookup failed: {ex.Message}");
                }

                // If standard DNS fails, return null (leave setting blank)
                Logger.Log("ScreamSettings", "Reverse DNS lookup failed, leaving setting blank");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Log("ScreamSettings", $"Error in ReverseLookupViaMdns: {ex.Message}");
                return null;
            }
        }

        // Helper method to create a DNS query packet
        private byte[] CreateDnsQueryPacket(string name, ushort type = 1) // Default type 1 = A record
        {
            Logger.Log("ScreamSettings", $"Creating DNS query packet for {name}, type {type}");

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                // Transaction ID (16 bits)
                writer.Write((ushort)new Random().Next(0, 65535));

                // Flags (16 bits) - Standard query
                writer.Write((ushort)0x0100);

                // Questions count (16 bits)
                writer.Write((ushort)1);

                // Answer RRs, Authority RRs, Additional RRs (16 bits each)
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((ushort)0);

                // Write the query name in DNS format (length-prefixed labels)
                string[] labels = name.Split('.');
                foreach (string label in labels)
                {
                    writer.Write((byte)label.Length);
                    writer.Write(Encoding.ASCII.GetBytes(label));
                }

                // Terminating zero length
                writer.Write((byte)0);

                // Query type (16 bits)
                writer.Write((ushort)type);

                // Query class (16 bits) - IN (Internet)
                writer.Write((ushort)1);

                return ms.ToArray();
            }
        }

        // Helper method to parse a DNS response for an IP address
        private IPAddress? ParseDnsResponseForIpAddress(byte[] responseData, string queryName)
        {
            try
            {
                // This is a simplified parser and may not work for all DNS responses
                // In a real implementation, you would need to properly parse the DNS packet structure

                // Skip the header (12 bytes)
                int position = 12;

                // Skip the question section
                // First skip the name
                while (position < responseData.Length)
                {
                    byte length = responseData[position];

                    // Check for compression pointer or end of name
                    if (length == 0 || (length & 0xC0) == 0xC0)
                    {
                        if ((length & 0xC0) == 0xC0)
                            position += 2; // Skip compression pointer (2 bytes)
                        else
                            position += 1; // Skip zero length byte
                        break;
                    }

                    position += length + 1; // Skip label
                }

                // Skip the question type and class (4 bytes)
                position += 4;

                // Now we're at the answer section
                // Check if we have enough data for at least one answer
                if (position + 12 > responseData.Length)
                {
                    Logger.Log("ScreamSettings", "DNS response too short for answers");
                    return null;
                }

                // Skip the answer name (usually a compression pointer, 2 bytes)
                position += 2;

                // Read the answer type
                ushort answerType = (ushort)((responseData[position] << 8) | responseData[position + 1]);
                position += 2;

                // Skip the answer class (2 bytes)
                position += 2;

                // Skip the TTL (4 bytes)
                position += 4;

                // Read the data length
                ushort dataLength = (ushort)((responseData[position] << 8) | responseData[position + 1]);
                position += 2;

                // If this is an A record (type 1) and the data length is 4 bytes (IPv4 address)
                if (answerType == 1 && dataLength == 4 && position + 4 <= responseData.Length)
                {
                    // Extract the IP address
                    byte[] ipBytes = new byte[4];
                    Array.Copy(responseData, position, ipBytes, 0, 4);
                    return new IPAddress(ipBytes);
                }

                Logger.Log("ScreamSettings", $"No A record found in DNS response (type: {answerType}, length: {dataLength})");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Log("ScreamSettings", $"Error parsing DNS response for IP: {ex.Message}");
                return null;
            }
        }

        // Helper method to parse a DNS response for a hostname
        private string? ParseDnsResponseForHostname(byte[] responseData)
        {
            try
            {
                // This is a simplified parser and may not work for all DNS responses
                // In a real implementation, you would need to properly parse the DNS packet structure

                // Skip the header (12 bytes)
                int position = 12;

                // Skip the question section
                // First skip the name
                while (position < responseData.Length)
                {
                    byte length = responseData[position];

                    // Check for compression pointer or end of name
                    if (length == 0 || (length & 0xC0) == 0xC0)
                    {
                        if ((length & 0xC0) == 0xC0)
                            position += 2; // Skip compression pointer (2 bytes)
                        else
                            position += 1; // Skip zero length byte
                        break;
                    }

                    position += length + 1; // Skip label
                }

                // Skip the question type and class (4 bytes)
                position += 4;

                // Now we're at the answer section
                // Check if we have enough data for at least one answer
                if (position + 12 > responseData.Length)
                {
                    Logger.Log("ScreamSettings", "DNS response too short for answers");
                    return null;
                }

                // Skip the answer name (usually a compression pointer, 2 bytes)
                position += 2;

                // Read the answer type
                ushort answerType = (ushort)((responseData[position] << 8) | responseData[position + 1]);
                position += 2;

                // Skip the answer class (2 bytes)
                position += 2;

                // Skip the TTL (4 bytes)
                position += 4;

                // Read the data length
                ushort dataLength = (ushort)((responseData[position] << 8) | responseData[position + 1]);
                position += 2;

                // If this is a PTR record (type 12)
                if (answerType == 12 && position + dataLength <= responseData.Length)
                {
                    // Extract the hostname
                    StringBuilder hostname = new StringBuilder();
                    int endPosition = position + dataLength;

                    while (position < endPosition)
                    {
                        byte length = responseData[position++];

                        // Check for compression pointer or end of name
                        if (length == 0)
                            break;

                        if ((length & 0xC0) == 0xC0)
                        {
                            // This is a compression pointer, which we don't handle in this simplified parser
                            Logger.Log("ScreamSettings", "Compression pointer found in hostname, not handled");
                            break;
                        }

                        if (hostname.Length > 0)
                            hostname.Append('.');

                        for (int i = 0; i < length && position < endPosition; i++)
                        {
                            hostname.Append((char)responseData[position++]);
                        }
                    }

                    return hostname.ToString();
                }

                Logger.Log("ScreamSettings", $"No PTR record found in DNS response (type: {answerType}, length: {dataLength})");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Log("ScreamSettings", $"Error parsing DNS response for hostname: {ex.Message}");
                return null;
            }
        }
    }
}
