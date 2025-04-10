using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Makaretu.Dns;
using Message = Makaretu.Dns.Message;

namespace ScreamRouterDesktop
{
    public class DnsServer : IDisposable
    {
        private const int DnsPort = 5300;
        private const string SettingsDomainName = "settings.screamrouter.local";
        private UdpClient? listener;
        private CancellationTokenSource? cancellationTokenSource;
        private string receiverID = string.Empty;
        private Func<ZeroconfService.AudioSettings?> getAudioSettingsCallback; // Callback to get current settings

        // Constructor requires a callback to get audio settings
        public DnsServer(Func<ZeroconfService.AudioSettings?> audioSettingsCallback)
        {
            getAudioSettingsCallback = audioSettingsCallback ?? throw new ArgumentNullException(nameof(audioSettingsCallback));
        }

        // Method to update the ReceiverID
        public void SetReceiverID(string id)
        {
            receiverID = id;
            Trace.WriteLine($"DnsServer: ReceiverID updated to {receiverID}");
        }

        public void Start()
        {
            if (listener != null) return; // Already running

            try
            {
                listener = new UdpClient();
                listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Client.Bind(new IPEndPoint(IPAddress.Any, DnsPort));
                cancellationTokenSource = new CancellationTokenSource();

                Trace.WriteLine($"DNS Server started on port {DnsPort}");
                Task.Run(() => ListenLoop(cancellationTokenSource.Token), cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error starting DNS Server: {ex.Message}");
                Stop(); // Clean up if start failed
            }
        }

        private async Task ListenLoop(CancellationToken cancellationToken)
        {
            Trace.WriteLine("DNS Server listening loop started.");
            while (!cancellationToken.IsCancellationRequested && listener != null)
            {
                try
                {
                    UdpReceiveResult result = await listener.ReceiveAsync(cancellationToken);
                    Trace.WriteLine($"DNS Server: Received {result.Buffer.Length} bytes from {result.RemoteEndPoint}");
                    ProcessQuery(result.Buffer, result.RemoteEndPoint);
                }
                catch (OperationCanceledException)
                {
                    Trace.WriteLine("DNS Server listening loop cancelled.");
                    break; // Exit loop if cancelled
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"DNS Server error in listen loop: {ex.Message}");
                    await Task.Delay(100, cancellationToken); // Prevent tight loop on errors
                }
            }
            Trace.WriteLine("DNS Server listening loop finished.");
        }

        private void ProcessQuery(byte[] queryBytes, IPEndPoint remoteEndPoint)
        {
            try
            {
                var request = new Message();
                request.Read(queryBytes);

                if (request.Questions.Count == 0) return; // No questions

                var question = request.Questions[0];
                Trace.WriteLine($"DNS Query: Name={question.Name}, Type={question.Type}, Class={question.Class}");

                // We only care about TXT queries for our specific domain name
                if (question.Type == DnsType.TXT && question.Name.ToString().Equals(SettingsDomainName, StringComparison.OrdinalIgnoreCase))
                {
                    Trace.WriteLine($"DNS Server: Received TXT query for {SettingsDomainName} from {remoteEndPoint}");
                    var audioSettings = getAudioSettingsCallback(); // Get current settings via callback

                    if (audioSettings != null && !string.IsNullOrEmpty(receiverID))
                    {
                        // Format settings string (same as before)
                        string settingsText = string.Join(";",
                            $"bit_depth={audioSettings.BitDepth}",
                            $"sample_rate={audioSettings.SampleRate}",
                            $"channels={audioSettings.Channels}",
                            $"channel_layout={audioSettings.ChannelLayout}",
                            $"receiver_id={receiverID}"
                        );

                        Trace.WriteLine($"DNS Server: Responding with TXT: {settingsText}");

                        var response = new Message
                        {
                            Id = request.Id,
                            QR = true, // Response
                            AA = true, // Authoritative Answer
                            Opcode = request.Opcode,
                            RD = request.RD // Copy recursion desired flag
                        };
                        
                        // Add questions individually since Questions is read-only
                        foreach (var q in request.Questions)
                        {
                            response.Questions.Add(q);
                        }

                        response.Answers.Add(new TXTRecord
                        {
                            Name = question.Name,
                            Strings = new List<string> { settingsText },
                            TTL = TimeSpan.FromSeconds(60) // Short TTL for dynamic data
                        });

                        byte[] responseBytes = response.ToByteArray();
                        listener?.Send(responseBytes, responseBytes.Length, remoteEndPoint);
                        Trace.WriteLine($"DNS Server: Sent TXT response to {remoteEndPoint}");
                    }
                    else
                    {
                        Trace.WriteLine("DNS Server: Could not get settings or ReceiverID to build TXT response.");
                        // Use a numeric value instead of the enum since Rcode is not found
                        SendErrorResponse(request, remoteEndPoint, 2); // 2 = Server Failure
                    }
                }
                else
                {
                    // Respond with NXDOMAIN (Non-Existent Domain) or NOTIMP (Not Implemented) for other queries
                    Trace.WriteLine($"DNS Server: Query not supported (Name: {question.Name}, Type: {question.Type}). Sending NXDOMAIN.");
                    // Use a numeric value instead of the enum since Rcode is not found
                    SendErrorResponse(request, remoteEndPoint, 3); // 3 = Name Error (NXDomain)
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"DNS Server: Error processing query: {ex.Message}");
            }
        }

        private void SendErrorResponse(Message request, IPEndPoint remoteEndPoint, int rcode)
        {
            try
            {
                var response = new Message
                {
                    Id = request.Id,
                    QR = true, // Response
                    AA = true, // Authoritative (as we are the authority for this zone, even for errors)
                    Opcode = request.Opcode,
                    RD = request.RD,
                    Status = (MessageStatus)rcode // Cast the numeric value to MessageStatus
                };
                
                // Add questions individually since Questions is read-only
                foreach (var q in request.Questions)
                {
                    response.Questions.Add(q);
                }

                byte[] responseBytes = response.ToByteArray();
                listener?.Send(responseBytes, responseBytes.Length, remoteEndPoint);
                Trace.WriteLine($"DNS Server: Sent error response ({rcode}) to {remoteEndPoint}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"DNS Server: Error sending error response: {ex.Message}");
            }
        }


        public void Stop()
        {
            Trace.WriteLine("DNS Server stopping...");
            cancellationTokenSource?.Cancel();
            listener?.Close();
            listener = null;
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
            Trace.WriteLine("DNS Server stopped.");
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
