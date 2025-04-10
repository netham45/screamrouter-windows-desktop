using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;

using System.Collections.ObjectModel;
using System.Diagnostics;
using Makaretu.Dns;
using Message = Makaretu.Dns.Message;

namespace ScreamRouterDesktop
{
    public class ZeroconfService : IDisposable
    {
        private const int MDNS_PORT = 5353;
        private const string MDNS_SERVICE_NAME = "_sink._scream._udp.local";
        private const string MDNS_SETTINGS_SERVICE_NAME = "_settings._sink._scream._udp.local";
        private const string MDNS_SOURCE_SERVICE_NAME = "_source._scream._udp.local";
        private const string MDNS_SOURCE_SETTINGS_SERVICE_NAME = "_settings._source._scream._udp.local";
        private MulticastService? mdnsService;
        private DomainName serviceName;
        private UdpClient? queryClient;
        private CancellationTokenSource? cancellationTokenSource;
        private bool isRunning = false;
        private string receiverID = string.Empty; // Field to store the ReceiverID

        // Windows Core Audio API imports for getting audio device information
        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid iid, out IntPtr ppv);

        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorClass { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int NotImpl1();
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);
            int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
            int GetId(out string ppstrId);
        }

        [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            int GetCount(out int cProps);
            int GetAt(int iProp, out PropertyKey pkey);
            int GetValue(ref PropertyKey key, out PropVariant pv);
        }

        [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            int Initialize(int ShareMode, uint StreamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr AudioSessionGuid);
            int GetBufferSize(out uint pNumBufferFrames);
            int GetStreamLatency(out long phnsLatency);
            int GetCurrentPadding(out uint pNumPaddingFrames);
            int IsFormatSupported(int ShareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
            int GetMixFormat(out IntPtr ppDeviceFormat);
            int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
            int Start();
            int Stop();
            int Reset();
            int SetEventHandle(IntPtr eventHandle);
            int GetService(ref Guid riid, out IntPtr ppv);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid fmtid;
            public int pid;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr pointerValue;
            [FieldOffset(8)] public byte byteValue;
            [FieldOffset(8)] public short shortValue;
            [FieldOffset(8)] public int intValue;
            [FieldOffset(8)] public long longValue;
            [FieldOffset(8)] public float floatValue;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        private enum EDataFlow
        {
            eRender = 0,
            eCapture = 1,
            eAll = 2
        }

        private enum ERole
        {
            eConsole = 0,
            eMultimedia = 1,
            eCommunications = 2
        }

        // DNS record types and flags - matching Python implementation
        private const int DNS_TYPE_A = 1;    // A record type (IPv4 address)
        private const int DNS_TYPE_TXT = 16; // TXT record type
        private const ushort DNS_FLAGS_QR_QUERY = 0x0000;  // Standard query
        private const ushort DNS_FLAGS_QR_RESPONSE = 0x8400;  // Response with AA flag set

        public ZeroconfService()
        {
            serviceName = new DomainName(MDNS_SERVICE_NAME);
        }

        // Method to set the ReceiverID from ScreamSettings
        public void SetReceiverID(string id)
        {
            receiverID = id;
            Trace.WriteLine($"ZeroconfService: ReceiverID set to {receiverID}");
        }

        public void Start()
        {
            if (isRunning)
                return;

            try
            {
                // Initialize mDNS service if needed
                if (mdnsService == null)
                {
                    mdnsService = new MulticastService();
                    // Set up event handler for mDNS queries
                    mdnsService.QueryReceived += OnQueryReceived;
                    Trace.WriteLine("mDNS service initialized");
                }

                // Create new cancellation token source
                cancellationTokenSource = new CancellationTokenSource();

                // Start the mDNS service
                mdnsService.Start();

                // Start a dedicated listener for ANY direct query
                Task.Run(() => ListenForAllQueries(cancellationTokenSource.Token));
                
                // Start a dedicated listener for settings TXT queries
                Task.Run(() => ListenForSettingsTxtQueries(cancellationTokenSource.Token));

                isRunning = true;
                Trace.WriteLine("ZeroconfService started");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error starting ZeroconfService: {ex.Message}");
                Stop();
            }
        }

        // This method specifically handles settings TXT queries for both sink and source domains
        private async Task ListenForSettingsTxtQueries(CancellationToken cancellationToken)
        {
            try
            {
                // Create a dedicated socket for settings.scream.local TXT queries
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    // Set socket options
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    
                    // CRITICAL: Bind to port 5353 - this ensures we can receive and respond on the same port
                    socket.Bind(new IPEndPoint(IPAddress.Any, MDNS_PORT));
                    
                    Trace.WriteLine("SETTINGS.SCREAM.LOCAL HANDLER: Started on port 5353");
                    
                    // Buffer for receiving data
                    byte[] buffer = new byte[4096];
                    
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            // Set up endpoint for receiving
                            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                            
                            // Receive data
                            int bytesRead = socket.ReceiveFrom(buffer, ref remoteEP);
                            IPEndPoint senderEP = (IPEndPoint)remoteEP;
                            
                            Trace.WriteLine($"SETTINGS.SCREAM.LOCAL HANDLER: Received {bytesRead} bytes from {senderEP}");
                            
                            // Try to parse as DNS message
                            try
                            {
                                var message = new Message();
                                message.Read(buffer, 0, bytesRead);
                                
                                // Process each question
                                foreach (var question in message.Questions)
                                {
                                    string questionName = question.Name.ToString();
                                    int questionType = (int)question.Type;
                                    
                                    Trace.WriteLine($"SETTINGS.SCREAM.LOCAL HANDLER: Question - Name: {questionName}, Type: {questionType}");
                                    
                                    // Check if this is a TXT query for sink.settings.screamrouter.local or source.settings.screamrouter.local
                                    bool isSinkSettingsLocalTxtQuery = 
                                        (questionName.Equals("sink.settings.screamrouter.local", StringComparison.OrdinalIgnoreCase) ||
                                         questionName.Equals("sink.settings.screamrouter.local.", StringComparison.OrdinalIgnoreCase)) &&
                                        questionType == DNS_TYPE_TXT;
                                    
                                    bool isSourceSettingsLocalTxtQuery = 
                                        (questionName.Equals("source.settings.screamrouter.local", StringComparison.OrdinalIgnoreCase) ||
                                         questionName.Equals("source.settings.screamrouter.local.", StringComparison.OrdinalIgnoreCase)) &&
                                        questionType == DNS_TYPE_TXT;
                                    
                                    // For backward compatibility
                                    bool isLegacySettingsLocalTxtQuery = 
                                        (questionName.Equals("settings.scream.local", StringComparison.OrdinalIgnoreCase) ||
                                         questionName.Equals("settings.scream.local.", StringComparison.OrdinalIgnoreCase)) &&
                                        questionType == DNS_TYPE_TXT;
                                    
                                    if (isSinkSettingsLocalTxtQuery || isLegacySettingsLocalTxtQuery)
                                    {
                                        Trace.WriteLine($"SETTINGS HANDLER: Found sink.settings.screamrouter.local TXT query from {senderEP}");
                                        
                                        // Get current audio settings
                                        var audioSettings = GetCurrentAudioSettings();
                                        if (audioSettings == null)
                                        {
                                            Trace.WriteLine("SETTINGS HANDLER: Could not retrieve audio settings");
                                            continue;
                                        }
                                        
                                        // Format settings as key=value pairs separated by semicolons
                                        string settingsText = string.Join(";",
                                            $"bit_depth={audioSettings.BitDepth}",
                                            $"sample_rate={audioSettings.SampleRate}",
                                            $"channels={audioSettings.Channels}",
                                            $"channel_layout={audioSettings.ChannelLayout}",
                                            $"id={this.receiverID}",
                                            $"ip={GetLocalIPForRemote(senderEP.Address)}"
                                        );
                                        
                                        // Create a response message
                                        var response = new Message
                                        {
                                            Id = message.Id,
                                            QR = true,     // This is a response
                                            Opcode = 0,    // Standard query
                                            AA = true,     // Authoritative answer
                                            TC = false,    // Not truncated
                                            RD = false,    // Recursion not desired
                                            RA = false,    // Recursion not available
                                            Z = 0          // Reserved bits should be zero
                                        };
                                        
                                        // Copy the question
                                        response.Questions.Add(question);
                                        
                                        // Add a TXT record with our settings
                                        var txtRecord = new TXTRecord
                                        {
                                            Name = question.Name,
                                            Strings = new List<string> { settingsText },
                                            TTL = TimeSpan.FromMinutes(5) // 5 minute TTL
                                        };
                                        response.Answers.Add(txtRecord);
                                        
                                        // Send the response directly to the sender from port 5353
                                        byte[] responseData = response.ToByteArray();
                                        socket.SendTo(responseData, senderEP);
                                        
                                        Trace.WriteLine($"SETTINGS HANDLER: Sent sink TXT response to {senderEP} with settings: {settingsText}");
                                        Trace.WriteLine($"SETTINGS HANDLER: Response hex dump: {BitConverter.ToString(responseData)}");
                                    }
                                    else if (isSourceSettingsLocalTxtQuery)
                                    {
                                        Trace.WriteLine($"SETTINGS HANDLER: Found source.settings.screamrouter.local TXT query from {senderEP}");
                                        
                                        // For source settings, we provide information about the source configuration
                                        // Format settings as key=value pairs separated by semicolons
                                        string settingsText = string.Join(";",
                                            $"id={this.receiverID}",
                                            $"ip={GetLocalIPForRemote(senderEP.Address)}",
                                            $"type=source",
                                            $"version=1.0"
                                        );
                                        
                                        // Create a response message
                                        var response = new Message
                                        {
                                            Id = message.Id,
                                            QR = true,     // This is a response
                                            Opcode = 0,    // Standard query
                                            AA = true,     // Authoritative answer
                                            TC = false,    // Not truncated
                                            RD = false,    // Recursion not desired
                                            RA = false,    // Recursion not available
                                            Z = 0          // Reserved bits should be zero
                                        };
                                        
                                        // Copy the question
                                        response.Questions.Add(question);
                                        
                                        // Add a TXT record with our settings
                                        var txtRecord = new TXTRecord
                                        {
                                            Name = question.Name,
                                            Strings = new List<string> { settingsText },
                                            TTL = TimeSpan.FromMinutes(5) // 5 minute TTL
                                        };
                                        response.Answers.Add(txtRecord);
                                        
                                        // Send the response directly to the sender from port 5353
                                        byte[] responseData = response.ToByteArray();
                                        socket.SendTo(responseData, senderEP);
                                        
                                        Trace.WriteLine($"SETTINGS HANDLER: Sent source TXT response to {senderEP} with settings: {settingsText}");
                                        Trace.WriteLine($"SETTINGS HANDLER: Response hex dump: {BitConverter.ToString(responseData)}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine($"SETTINGS.SCREAM.LOCAL HANDLER: Error parsing DNS message: {ex.Message}");
                            }
                            
                            // Small delay to prevent CPU hogging
                            await Task.Delay(10, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"SETTINGS.SCREAM.LOCAL HANDLER: Error receiving packet: {ex.Message}");
                            await Task.Delay(100, cancellationToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error in settings.scream.local handler: {ex.Message}");
            }
        }

        // This method will respond to ALL DNS queries on ALL interfaces
        private async Task ListenForAllQueries(CancellationToken cancellationToken)
        {
            try
            {
                // Create a raw UDP socket
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    // Set socket options
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);

                    // Explicitly allow address reuse
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        const int SIO_UDP_CONNRESET = -1744830452;
                        socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null);
                    }

                    // CRITICAL: Bind to ANY address on port 5353
                    socket.Bind(new IPEndPoint(IPAddress.Any, MDNS_PORT));

                    // Join the multicast group on all network interfaces
                    foreach (var networkInterface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                        .Where(ni => ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                               ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback))
                    {
                        try
                        {
                            var ipProperties = networkInterface.GetIPProperties();
                            foreach (var unicastAddress in ipProperties.UnicastAddresses)
                            {
                                if (unicastAddress.Address.AddressFamily == AddressFamily.InterNetwork)
                                {
                                    try
                                    {
                                        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                                            new MulticastOption(IPAddress.Parse("224.0.0.251"), unicastAddress.Address));
                                        Trace.WriteLine($"Joined multicast group on interface {networkInterface.Name} with IP {unicastAddress.Address}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Trace.WriteLine($"Failed to join multicast group on interface {networkInterface.Name} with IP {unicastAddress.Address}: {ex.Message}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"Error processing network interface {networkInterface.Name}: {ex.Message}");
                        }
                    }

                    Trace.WriteLine("UNIVERSAL LISTENER: Started on port 5353 - will respond to ALL DNS queries");

                    // Buffer for receiving data
                    byte[] buffer = new byte[4096];

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            // Set up endpoint for receiving
                            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

                            // Receive data
                            int bytesRead = socket.ReceiveFrom(buffer, ref remoteEP);
                            IPEndPoint senderEP = (IPEndPoint)remoteEP;

                            Trace.WriteLine($"UNIVERSAL LISTENER: Received {bytesRead} bytes from {senderEP}");
                            Trace.WriteLine($"UNIVERSAL LISTENER: Hex dump: {BitConverter.ToString(buffer, 0, bytesRead)}");

                            // Get current audio settings
                            var audioSettings = GetCurrentAudioSettings();
                            if (audioSettings == null)
                            {
                                Trace.WriteLine("UNIVERSAL LISTENER: Could not retrieve audio settings");
                                continue;
                            }

                            // Format settings as key=value pairs separated by semicolons
                            string settingsText = string.Join(";",
                                $"bit_depth={audioSettings.BitDepth}",
                                $"sample_rate={audioSettings.SampleRate}",
                                $"channels={audioSettings.Channels}",
                                $"channel_layout={audioSettings.ChannelLayout}",
                                $"id={this.receiverID}",
                                $"ip={GetLocalIPForRemote(senderEP.Address)}"
                            );

                            // Try to parse as DNS message
                            try
                            {
                                var message = new Message();
                                message.Read(buffer, 0, bytesRead);

                                Trace.WriteLine($"UNIVERSAL LISTENER: Parsed as DNS message - ID: {message.Id}, Questions: {message.Questions.Count}");

                                // Create a response message
                                var response = new Message
                                {
                                    Id = message.Id,
                                    QR = true,     // This is a response
                                    Opcode = 0,    // Standard query
                                    AA = true,     // Authoritative answer
                                    TC = false,    // Not truncated
                                    RD = false,    // Recursion not desired
                                    RA = false,    // Recursion not available
                                    Z = 0          // Reserved bits should be zero
                                };

                                // Process each question
                                foreach (var question in message.Questions)
                                {
                                    Trace.WriteLine($"UNIVERSAL LISTENER: Question - Name: {question.Name}, Type: {question.Type}, Class: {question.Class}");

                                    // Process any query we receive
                                    string questionName = question.Name.ToString();
                                    int questionType = (int)question.Type;

                                    Trace.WriteLine($"UNIVERSAL LISTENER: Processing query for {questionName} (Type: {questionType}) from {senderEP}");

                                    // Get the IP address for the interface that would be used to reach the remote endpoint
                                    IPAddress localIp = GetLocalIPForRemote(senderEP.Address);

                                    // Check if this is a query for settings domains
                    bool isSettingsLocalQuery = questionName.Equals("settings.scream.local", StringComparison.OrdinalIgnoreCase) ||
                                               questionName.Equals("settings.scream.local.", StringComparison.OrdinalIgnoreCase);
                    
                    bool isSinkSettingsLocalQuery = questionName.Equals("sink.settings.screamrouter.local", StringComparison.OrdinalIgnoreCase) ||
                                                   questionName.Equals("sink.settings.screamrouter.local.", StringComparison.OrdinalIgnoreCase);
                    
                    bool isSourceSettingsLocalQuery = questionName.Equals("source.settings.screamrouter.local", StringComparison.OrdinalIgnoreCase) ||
                                                     questionName.Equals("source.settings.screamrouter.local.", StringComparison.OrdinalIgnoreCase);

                                    // Copy the question to the response
                                    response.Questions.Add(question);

                                    // Handle based on query type
                                    if (questionType == DNS_TYPE_A)
                                    {
                                        // Add an A record with our IP address
                                        var aRecord = new ARecord
                                        {
                                            Name = question.Name,
                                            Address = localIp,
                                            TTL = TimeSpan.FromHours(1) // 1 hour TTL
                                        };
                                        response.Answers.Add(aRecord);
                                        Trace.WriteLine($"UNIVERSAL LISTENER: Added A record with IP {localIp} for {question.Name}");
                                    }
                                    else if (questionType == DNS_TYPE_TXT)
                                    {
                                        string txtContent;
                                        
                                        if (isSourceSettingsLocalQuery)
                                        {
                                            // For source settings, provide source-specific information
                                            txtContent = string.Join(";",
                                                $"id={this.receiverID}",
                                                $"ip={GetLocalIPForRemote(senderEP.Address)}",
                                                $"type=source",
                                                $"version=1.0"
                                            );
                                            Trace.WriteLine($"UNIVERSAL LISTENER: Responding with source settings for {questionName}");
                                        }
                                        else
                                        {
                                            // For sink settings or legacy settings, provide audio settings
                                            txtContent = settingsText;
                                            Trace.WriteLine($"UNIVERSAL LISTENER: Responding with sink settings for {questionName}");
                                        }
                                        
                                        // Add a TXT record with our settings
                                        var txtRecord = new TXTRecord
                                        {
                                            Name = question.Name,
                                            Strings = new List<string> { txtContent },
                                            TTL = TimeSpan.FromMinutes(5) // 5 minute TTL
                                        };
                                        response.Answers.Add(txtRecord);
                                        Trace.WriteLine($"UNIVERSAL LISTENER: Added TXT record for {question.Name} with settings: {txtContent}");
                                    }
                                    else if (questionType == 12) // PTR record
                                    {
                                        // Add a PTR record pointing to our service
                                        var ptrRecord = new PTRRecord
                                        {
                                            Name = question.Name,
                                            DomainName = new DomainName(MDNS_SERVICE_NAME),
                                            TTL = TimeSpan.FromHours(1) // 1 hour TTL
                                        };
                                        response.Answers.Add(ptrRecord);
                                        Trace.WriteLine($"UNIVERSAL LISTENER: Added PTR record for {question.Name} pointing to {MDNS_SERVICE_NAME}");
                                    }
                                }

                                // Only send a response if we added any answers
                                if (response.Answers.Count > 0)
                                {
                                    // Send the response directly to the sender
                                    byte[] responseData = response.ToByteArray();
                                    socket.SendTo(responseData, senderEP);
                                    Trace.WriteLine($"UNIVERSAL LISTENER: Sent response to {senderEP}");
                                    Trace.WriteLine($"UNIVERSAL LISTENER: Response hex dump: {BitConverter.ToString(responseData)}");

                                    // If this was a multicast query, also send a multicast response
                                    if (senderEP.Address.Equals(IPAddress.Parse("224.0.0.251")) || senderEP.Port == MDNS_PORT)
                                    {
                                        socket.SendTo(responseData, new IPEndPoint(IPAddress.Parse("224.0.0.251"), MDNS_PORT));
                                        Trace.WriteLine($"UNIVERSAL LISTENER: Also sent multicast response to 224.0.0.251:{MDNS_PORT}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine($"UNIVERSAL LISTENER: Error parsing DNS message: {ex.Message}");

                                // Even if parsing fails, try to send a hardcoded response for settings.scream.local TXT query
                                try
                                {
                                    // Check if the query might be for source.settings.screamrouter.local
                                    bool mightBeSourceQuery = buffer.Length > 20 && 
                                                             Encoding.ASCII.GetString(buffer).Contains("source.settings");
                                    
                                    // Create a hardcoded response
                                    var hardcodedResponse = new Message
                                    {
                                        Id = 0, // Use a default ID
                                        QR = true,
                                        Opcode = 0,
                                        AA = true,
                                        TC = false,
                                        RD = false,
                                        RA = false,
                                        Z = 0
                                    };

                                    string domainName;
                                    string responseContent;
                                    
                                    if (mightBeSourceQuery)
                                    {
                                        // For source settings
                                        domainName = "source.settings.screamrouter.local";
                                        responseContent = string.Join(";",
                                            $"id={this.receiverID}",
                                            $"ip={GetLocalIPForRemote(senderEP.Address)}",
                                            $"type=source",
                                            $"version=1.0"
                                        );
                                        Trace.WriteLine("UNIVERSAL LISTENER: Sending hardcoded source settings response");
                                    }
                                    else
                                    {
                                        // Default to sink settings
                                        domainName = "settings.scream.local";
                                        responseContent = settingsText;
                                        Trace.WriteLine("UNIVERSAL LISTENER: Sending hardcoded sink settings response");
                                    }

                                    // Add a question for the appropriate domain
                                    var question = new Question
                                    {
                                        Name = new DomainName(domainName),
                                        Type = DnsType.TXT,
                                        Class = DnsClass.IN
                                    };
                                    hardcodedResponse.Questions.Add(question);

                                    // Add a TXT record with our settings
                                    var txtRecord = new TXTRecord
                                    {
                                        Name = new DomainName(domainName),
                                        Strings = new List<string> { responseContent },
                                        TTL = TimeSpan.FromMinutes(5)
                                    };
                                    hardcodedResponse.Answers.Add(txtRecord);

                                    // Send the hardcoded response
                                    byte[] responseData = hardcodedResponse.ToByteArray();
                                    socket.SendTo(responseData, senderEP);

                                    Trace.WriteLine($"UNIVERSAL LISTENER: Sent hardcoded response to {senderEP}");
                                }
                                catch (Exception innerEx)
                                {
                                    Trace.WriteLine($"UNIVERSAL LISTENER: Error sending hardcoded response: {innerEx.Message}");
                                }
                            }

                            // Small delay to prevent CPU hogging
                            await Task.Delay(10, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"UNIVERSAL LISTENER: Error receiving packet: {ex.Message}");
                            await Task.Delay(100, cancellationToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error in universal listener: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (!isRunning)
                return;

            // Cancel any running tasks
            cancellationTokenSource?.Cancel();
            cancellationTokenSource = null;

            // Stop the mDNS service and remove event handlers
            if (mdnsService != null)
            {
                // Remove the event handler so it won't respond to queries
                mdnsService.QueryReceived -= OnQueryReceived;
                mdnsService.Stop();
            }

            // Close the query client to stop responding to UDP queries
            if (queryClient != null)
            {
                queryClient.Close();
                queryClient = null;
            }

            isRunning = false;
            Trace.WriteLine("ZeroconfService stopped - all network listeners removed");
        }

        private void OnQueryReceived(object sender, MessageEventArgs e)
        {
            try
            {
                // Process each question in the query
                foreach (var question in e.Message.Questions)
                {
                    Trace.WriteLine($"DEBUG: Received query for {question.Name} (Type: {question.Type}, TypeValue: {(int)question.Type})");

                    // Check if this is a query for our hostname or settings
                    string questionName = question.Name.ToString();
                    int questionType = (int)question.Type;

                    Trace.WriteLine($"Detailed query info - Name: {questionName}, Type: {questionType}, DNS_TYPE_TXT: {DNS_TYPE_TXT}");
                    Trace.WriteLine($"Raw question data: {BitConverter.ToString(question.ToByteArray())}");
                    Trace.WriteLine($"Message ID: {e.Message.Id}, Flags: {e.Message.QR},{e.Message.Opcode},{e.Message.AA},{e.Message.TC},{e.Message.RD},{e.Message.RA},{e.Message.Z}");
                    Trace.WriteLine($"REMOTE ENDPOINT: {e.RemoteEndPoint.Address}:{e.RemoteEndPoint.Port}");

                    bool isHostnameQuery = questionName.Equals(MDNS_SERVICE_NAME, StringComparison.OrdinalIgnoreCase) ||
                                           questionName.Equals($"{MDNS_SERVICE_NAME}.", StringComparison.OrdinalIgnoreCase);
                    bool isSettingsQuery = questionName.Equals(MDNS_SETTINGS_SERVICE_NAME, StringComparison.OrdinalIgnoreCase) ||
                                           questionName.Equals($"{MDNS_SETTINGS_SERVICE_NAME}.", StringComparison.OrdinalIgnoreCase);
                    bool isSettingsLocalQuery = questionName.Equals("settings.scream.local", StringComparison.OrdinalIgnoreCase) ||
                                               questionName.Equals("settings.scream.local.", StringComparison.OrdinalIgnoreCase);
                    
                    // New domain queries
                    bool isSinkQuery = questionName.Equals("sink.screamrouter.local", StringComparison.OrdinalIgnoreCase) ||
                                      questionName.Equals("sink.screamrouter.local.", StringComparison.OrdinalIgnoreCase);
                    bool isSourceQuery = questionName.Equals("source.screamrouter.local", StringComparison.OrdinalIgnoreCase) ||
                                        questionName.Equals("source.screamrouter.local.", StringComparison.OrdinalIgnoreCase);
                    
                    // Check for sink and source settings queries
                    bool isSinkSettingsLocalQuery = questionName.Equals("sink.settings.screamrouter.local", StringComparison.OrdinalIgnoreCase) ||
                                                   questionName.Equals("sink.settings.screamrouter.local.", StringComparison.OrdinalIgnoreCase);
                    
                    bool isSourceSettingsLocalQuery = questionName.Equals("source.settings.screamrouter.local", StringComparison.OrdinalIgnoreCase) ||
                                                     questionName.Equals("source.settings.screamrouter.local.", StringComparison.OrdinalIgnoreCase);
                    
                    // CRITICAL FIX: Always respond to settings TXT queries regardless of other conditions
                    if ((isSettingsLocalQuery || isSinkSettingsLocalQuery || isSourceSettingsLocalQuery) && questionType == DNS_TYPE_TXT)
                    {
                        Trace.WriteLine($"CRITICAL: Detected settings.scream.local TXT query from {e.RemoteEndPoint.Address}");
                        
                        // Get current audio settings
                        var audioSettings = GetCurrentAudioSettings();
                        if (audioSettings == null)
                        {
                            Trace.WriteLine("Could not retrieve audio settings");
                            return;
                        }
                        
                        // Format settings as key=value pairs separated by semicolons
                        string settingsText;
                        
                        if (isSourceSettingsLocalQuery)
                        {
                            // For source settings, provide source-specific information
                            settingsText = string.Join(";",
                                $"id={this.receiverID}",
                                $"ip={GetLocalIPForRemote(e.RemoteEndPoint.Address)}",
                                $"type=source",
                                $"version=1.0"
                            );
                            Trace.WriteLine($"Responding with source settings for {questionName}");
                        }
                        else
                        {
                            // For sink settings or legacy settings, provide audio settings
                            settingsText = string.Join(";",
                                $"bit_depth={audioSettings.BitDepth}",
                                $"sample_rate={audioSettings.SampleRate}",
                                $"channels={audioSettings.Channels}",
                                $"channel_layout={audioSettings.ChannelLayout}",
                                $"id={this.receiverID}",
                                $"ip={GetLocalIPForRemote(e.RemoteEndPoint.Address)}"
                            );
                            Trace.WriteLine($"Responding with sink settings for {questionName}");
                        }
                        
                        // Create a response message
                        var response = new Message
                        {
                            Id = e.Message.Id,
                            QR = true,     // This is a response
                            Opcode = 0,    // Standard query
                            AA = true,     // Authoritative answer
                            TC = false,    // Not truncated
                            RD = false,    // Recursion not desired
                            RA = false,    // Recursion not available
                            Z = 0          // Reserved bits should be zero
                        };
                        
                        // Copy the question
                        response.Questions.Add(question);
                        
                        // Add a TXT record with our settings
                        var txtRecord = new TXTRecord
                        {
                            Name = question.Name,
                            Strings = new List<string> { settingsText },
                            TTL = TimeSpan.FromMinutes(5) // 5 minute TTL
                        };
                        Trace.WriteLine(txtRecord);
                        response.Answers.Add(txtRecord);
                        
                        // Send via multicast service
                        mdnsService?.SendAnswer(response);
                        
                        // CRITICAL FIX: Always send a direct unicast response for settings.scream.local TXT queries
                        // This bypasses all the normal logic and ensures a direct response
                        try
                        {
                            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                            {
                                // Get the IP address for the interface that would be used to reach the remote endpoint
                                IPAddress localIp = GetLocalIPForRemote(e.RemoteEndPoint.Address);
                                
                                // Bind to ANY address (critical for Windows UDP sockets)
                                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                                
                                // Set the outgoing interface
                                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, 
                                    localIp.GetAddressBytes());
                                
                                // Convert to bytes
                                byte[] buffer = response.ToByteArray();
                                
                                // CRITICAL FIX: Send directly to the requester's original source port
                                socket.SendTo(buffer, new IPEndPoint(e.RemoteEndPoint.Address, e.RemoteEndPoint.Port));
                                
                                Trace.WriteLine($"CRITICAL FIX: Direct response sent to {e.RemoteEndPoint.Address}:{MDNS_PORT} from {localIp}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"CRITICAL FIX: Error sending direct response: {ex.Message}");
                        }
                    }

                    // Handle A record queries for hostname, settings service, settings.scream.local, sink.screamrouter.local, and source.screamrouter.local
                    if ((isHostnameQuery || isSettingsQuery || isSettingsLocalQuery || isSinkQuery || isSourceQuery) && questionType == DNS_TYPE_A)
                    {
                        IPEndPoint remoteEndPoint = e.RemoteEndPoint;
                        Trace.WriteLine($"Received hostname query from {remoteEndPoint.Address} for {question.Name}");

                        // Get the IP address for the interface that would be used to reach the remote endpoint
                        IPAddress localIp = GetLocalIPForRemote(remoteEndPoint.Address);
                        Trace.WriteLine($"Responding with local IP: {localIp}");

                        // Create a response message with flags matching Python implementation (0x8400)
                        var response = new Message
                        {
                            Id = e.Message.Id,
                            QR = true,     // This is a response (bit 15)
                            Opcode = 0,    // Standard query (bits 11-14)
                            AA = true,     // Authoritative answer (bit 10)
                            TC = false,    // Not truncated (bit 9)
                            RD = false,    // Recursion not desired (bit 8)
                            RA = false,    // Recursion not available (bit 7)
                            Z = 0          // Reserved bits (4-6) should be zero
                        };

                        // Copy the question
                        response.Questions.Add(question);

                        // Add an A record with our IP address
                        var aRecord = new ARecord
                        {
                            Name = question.Name,
                            Address = localIp,
                            TTL = TimeSpan.FromHours(1) // 1 hour TTL
                        };
                        response.Answers.Add(aRecord);

                        // Send via multicast service
                        mdnsService?.SendAnswer(response);

                        // Also send directly to the requester using a raw UDP socket
                        SendDirectResponse(response, remoteEndPoint.Address, localIp, remoteEndPoint.Port);
                    }
                    else if ((isSettingsQuery || isSettingsLocalQuery) && ((int)question.Type) == DNS_TYPE_TXT)
                    {
                        IPEndPoint remoteEndPoint = e.RemoteEndPoint;
                        Trace.WriteLine($"Received settings query from {remoteEndPoint.Address} for {question.Name}");

                        // Get the IP address for the interface that would be used to reach the remote endpoint
                        IPAddress localIp = GetLocalIPForRemote(remoteEndPoint.Address);

                        // Get current audio settings
                        var audioSettings = GetCurrentAudioSettings();
                        if (audioSettings == null)
                        {
                            Trace.WriteLine("Could not retrieve audio settings");
                            return;
                        }

                        Trace.WriteLine($"Responding with audio settings: bit_depth={audioSettings.BitDepth}, " +
                                       $"sample_rate={audioSettings.SampleRate}, channels={audioSettings.Channels}, " +
                                       $"channel_layout={audioSettings.ChannelLayout}, id={this.receiverID}");

                        // Create a response message
                        var response = new Message
                        {
                            Id = e.Message.Id,
                            QR = true,     // This is a response
                            Opcode = 0,    // Standard query
                            AA = true,     // Authoritative answer
                            TC = false,    // Not truncated
                            RD = false,    // Recursion not desired
                            RA = false,    // Recursion not available
                            Z = 0          // Reserved bits should be zero
                        };

                        // Copy the question
                        response.Questions.Add(question);

                        // Format settings as key=value pairs separated by semicolons
                        // This matches exactly what the Python code expects
                        string settingsText = string.Join(";",
                            $"bit_depth={audioSettings.BitDepth}",
                            $"sample_rate={audioSettings.SampleRate}",
                            $"channels={audioSettings.Channels}",
                            $"channel_layout={audioSettings.ChannelLayout}",
                            $"id={this.receiverID}",
                            $"ip={GetLocalIPForRemote(remoteEndPoint.Address)}"
                        );

                        // Log what we're sending
                        Trace.WriteLine($"Sending TXT record with settings: {settingsText}");

                        // Add a TXT record with our settings
                        // The Python code expects a single string that it will parse
                        var txtRecord = new TXTRecord
                        {
                            Name = question.Name,
                            Strings = new List<string> { settingsText },
                            TTL = TimeSpan.FromMinutes(5) // 5 minute TTL
                        };

                        // Debug the TXT record format
                        Trace.WriteLine($"TXT record details - Name: {txtRecord.Name}, TTL: {txtRecord.TTL}, Strings count: {txtRecord.Strings.Count}");
                        foreach (var str in txtRecord.Strings)
                        {
                            Trace.WriteLine($"TXT string: '{str}' (Length: {str.Length})");
                        }
                        response.Answers.Add(txtRecord);

                        // For settings.scream.local, ensure we send both multicast and unicast responses
                        if (isSettingsLocalQuery)
                        {
                            // Send via multicast service
                            mdnsService?.SendAnswer(response);

                            // Also send an explicit multicast response
                            SendMulticastResponse(response, localIp);

                            // And send a direct unicast response to the requester
                            SendDirectResponse(response, remoteEndPoint.Address, localIp, remoteEndPoint.Port);

                            Trace.WriteLine("Sent both multicast and unicast responses for settings.scream.local");
                        }
                        else
                        {
                            // For other queries, use the standard approach
                            mdnsService?.SendAnswer(response);
                            SendDirectResponse(response, remoteEndPoint.Address, localIp, remoteEndPoint.Port);
                        }
                    }
                    // Handle PTR record queries
                    else if (questionType == 12) // PTR record
                    {
                        IPEndPoint remoteEndPoint = e.RemoteEndPoint;
                        Trace.WriteLine($"Received PTR query from {remoteEndPoint.Address} for {question.Name}");

                        // Get the IP address for the interface that would be used to reach the remote endpoint
                        IPAddress localIp = GetLocalIPForRemote(remoteEndPoint.Address);

                        // Create a response message
                        var response = new Message
                        {
                            Id = e.Message.Id,
                            QR = true,     // This is a response
                            Opcode = 0,    // Standard query
                            AA = true,     // Authoritative answer
                            TC = false,    // Not truncated
                            RD = false,    // Recursion not desired
                            RA = false,    // Recursion not available
                            Z = 0          // Reserved bits should be zero
                        };

                        // Copy the question
                        response.Questions.Add(question);

                        // Add a PTR record pointing to our service
                        var ptrRecord = new PTRRecord
                        {
                            Name = question.Name,
                            DomainName = new DomainName(MDNS_SERVICE_NAME),
                            TTL = TimeSpan.FromHours(1) // 1 hour TTL
                        };
                        response.Answers.Add(ptrRecord);

                        // Send via multicast service
                        mdnsService?.SendAnswer(response);

                        // Also send an explicit multicast response
                        SendMulticastResponse(response, localIp);

                        // And send a direct unicast response to the requester
                        SendDirectResponse(response, remoteEndPoint.Address, localIp, remoteEndPoint.Port);

                        Trace.WriteLine($"Sent PTR record response to {remoteEndPoint.Address}");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error handling mDNS query: {ex.Message}");
            }
        }

        // Send a response directly to the multicast address
        private void SendMulticastResponse(Message response, IPAddress localIp)
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    // Set socket options for multicast
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, localIp.GetAddressBytes());

                    // Bind to the mDNS port on the local IP
                    socket.Bind(new IPEndPoint(IPAddress.Any, 0));

                    // Join the multicast group
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                        new MulticastOption(IPAddress.Parse("224.0.0.251"), localIp));

                    // Convert to bytes
                    byte[] buffer = response.ToByteArray();

                    // Send to the multicast address on the mDNS port
                    socket.SendTo(buffer, new IPEndPoint(IPAddress.Parse("224.0.0.251"), MDNS_PORT));
                    Trace.WriteLine($"Explicit multicast response sent to 224.0.0.251:{MDNS_PORT} from {localIp}");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error sending multicast response: {ex.Message}");
            }
        }

        // Always send a direct unicast response regardless of query type
        private void SendDirectResponse(Message response, IPAddress remoteAddress, IPAddress localIp, int clientPort = MDNS_PORT)
        {
            try
            {
                // Send a direct unicast response to the requester
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    // Set socket options
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);

                    // CRITICAL FIX: Bind to port 5353 - this ensures responses come FROM port 5353
                    // Standard DNS clients expect responses to come from the same port they sent to
                    socket.Bind(new IPEndPoint(IPAddress.Any, MDNS_PORT));
                    
                    // Set the outgoing interface
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, 
                        localIp.GetAddressBytes());

                    // Convert to bytes
                    byte[] buffer = response.ToByteArray();

                    // Send directly to the requester's port (either the client's source port or mDNS port)
                    socket.SendTo(buffer, new IPEndPoint(remoteAddress, clientPort));
                    Trace.WriteLine($"Direct unicast response sent to {remoteAddress}:{clientPort} from {localIp}:{MDNS_PORT}");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error sending direct UDP response: {ex.Message}");
            }
        }

        private IPAddress GetLocalIPForRemote(IPAddress remoteAddress)
        {
            try
            {
                // Use the socket method to determine the local IP that would be used to reach the remote address
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect(remoteAddress, 65530);
                    IPEndPoint? endPoint = socket.LocalEndPoint as IPEndPoint;
                    return endPoint?.Address ?? IPAddress.Loopback;
                }
            }
            catch
            {
                return IPAddress.Loopback;
            }
        }

        public class AudioSettings
        {
            public int BitDepth { get; set; } = 16;
            public int SampleRate { get; set; } = 44100;
            public int Channels { get; set; } = 2;
            public string ChannelLayout { get; set; } = "stereo";
        }

        public AudioSettings? GetCurrentAudioSettings()
        {
            try
            {
                // Get default audio endpoint
                Guid IID_IMMDeviceEnumerator = typeof(IMMDeviceEnumerator).GUID;
                Guid CLSID_MMDeviceEnumerator = typeof(MMDeviceEnumeratorClass).GUID;

                int hr = CoCreateInstance(ref CLSID_MMDeviceEnumerator, IntPtr.Zero, 1, ref IID_IMMDeviceEnumerator, out IntPtr pEnumerator);
                if (hr != 0)
                {
                    return null;
                }

                IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(pEnumerator);
                Marshal.Release(pEnumerator);

                hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice device);
                if (hr != 0)
                {
                    return null;
                }

                // Get audio client to query format
                Guid IID_IAudioClient = typeof(IAudioClient).GUID;
                hr = device.Activate(ref IID_IAudioClient, 1, IntPtr.Zero, out IntPtr pAudioClient);
                if (hr != 0)
                {
                    return null;
                }

                IAudioClient audioClient = (IAudioClient)Marshal.GetObjectForIUnknown(pAudioClient);
                Marshal.Release(pAudioClient);

                // Get mix format
                hr = audioClient.GetMixFormat(out IntPtr pFormat);
                if (hr != 0)
                {
                    return null;
                }

                WaveFormatEx format = Marshal.PtrToStructure<WaveFormatEx>(pFormat);
                Marshal.FreeCoTaskMem(pFormat);

                // Create audio settings from format
                var settings = new AudioSettings
                {
                    BitDepth = format.wBitsPerSample,
                    SampleRate = (int)format.nSamplesPerSec,
                    Channels = format.nChannels,
                    ChannelLayout = GetChannelLayoutName(format.nChannels)
                };

                return settings;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error getting audio settings: {ex.Message}");
                return null;
            }
        }

        private string GetChannelLayoutName(int channels)
        {
            return channels switch
            {
                1 => "mono",
                2 => "stereo",
                3 => "2.1",
                4 => "quad",
                5 => "4.1",
                6 => "5.1",
                7 => "6.1",
                8 => "7.1",
                _ => $"channels_{channels}"
            };
        }

        public void Dispose()
        {
            // First stop the service
            Stop();

            // Then clean up resources
            if (mdnsService != null)
            {
                mdnsService.QueryReceived -= OnQueryReceived;
                mdnsService.Dispose();
                mdnsService = null;
            }

            if (queryClient != null)
            {
                queryClient.Close();
                queryClient.Dispose();
                queryClient = null;
            }

            Trace.WriteLine("ZeroconfService disposed");
        }
    }
}
