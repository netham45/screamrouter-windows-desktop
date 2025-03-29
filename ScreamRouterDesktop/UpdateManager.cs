using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Text.Json;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Linq;

namespace ScreamRouterDesktop
{
    public enum UpdateMode
    {
        DoNotCheck = 0,
        NotifyUser = 1,
        AutomaticUpdate = 2
    }

    public class UpdateManager
    {
        private const string RegistryKey = @"Software\ScreamRouter";
        private const string RegistryValue = "UpdateMode";
        private const string GithubApiUrl = "https://api.github.com/repos/netham45/screamrouter-windows-desktop/releases";
        private static readonly HttpClient client = new HttpClient();

        public UpdateMode CurrentMode
        {
            get
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegistryKey))
                {
                    var value = key.GetValue(RegistryValue);
                    if (value != null && Enum.TryParse<UpdateMode>(value.ToString(), out var mode))
                    {
                        return mode;
                    }
                    
                    // Show first-run dialog to choose update mode
                    using (var form = new UpdatePreferencesForm())
                    {
                        var result = form.ShowDialog();
                        var selectedMode = result == System.Windows.Forms.DialogResult.OK 
                            ? form.SelectedMode 
                            : UpdateMode.DoNotCheck; // If cancelled, don't check for updates
                        
                        key.SetValue(RegistryValue, (int)selectedMode);
                        return selectedMode;
                    }
                }
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegistryKey))
                {
                    key.SetValue(RegistryValue, (int)value);
                }
            }
        }

        public async Task CheckForUpdates()
        {
            Debug.WriteLine($"[UpdateManager] Starting update check. Current mode: {CurrentMode}");
            
            if (CurrentMode == UpdateMode.DoNotCheck)
            {
                Debug.WriteLine("[UpdateManager] Update mode is DoNotCheck, skipping update check");
                return;
            }

            try
            {
                Debug.WriteLine("[UpdateManager] Fetching releases from GitHub API");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ScreamRouter/1.0");
                var response = await client.GetStringAsync(GithubApiUrl);
                Debug.WriteLine($"[UpdateManager] GitHub API Response received: {response}");

                var releases = JsonSerializer.Deserialize<JsonElement>(response);
                Debug.WriteLine($"[UpdateManager] Found {releases.EnumerateArray().Count()} releases");

                // Find the latest release by published date
                var latestRelease = releases.EnumerateArray()
                    .OrderByDescending(r => DateTime.Parse(r.GetProperty("published_at").GetString()))
                    .First();

                Debug.WriteLine($"[UpdateManager] Latest release tag: {latestRelease.GetProperty("tag_name").GetString()}");
                Debug.WriteLine($"[UpdateManager] Latest release published: {latestRelease.GetProperty("published_at").GetString()}");

                var latestMsi = latestRelease.GetProperty("assets").EnumerateArray()
                    .FirstOrDefault(x => x.GetProperty("name").GetString().EndsWith(".msi"));

                if (latestMsi.ValueKind == JsonValueKind.Undefined)
                {
                    Debug.WriteLine("[UpdateManager] No MSI found in latest release");
                    return;
                }

                var currentExe = Process.GetCurrentProcess().MainModule.FileName;
                var currentBuildDate = File.GetLastWriteTime(currentExe);
                var latestBuildDate = DateTime.Parse(latestMsi.GetProperty("created_at").GetString());

                Debug.WriteLine($"[UpdateManager] Current build date: {currentBuildDate}");
                Debug.WriteLine($"[UpdateManager] Latest build date: {latestBuildDate}");

                // Only update if the new build is at least 15 minutes newer than current
                var minimumTimeDiff = TimeSpan.FromMinutes(15);
                var actualTimeDiff = latestBuildDate - currentBuildDate;
                Debug.WriteLine($"[UpdateManager] Time difference between builds: {actualTimeDiff.TotalMinutes:F2} minutes");
                
                if (latestBuildDate > currentBuildDate && actualTimeDiff >= minimumTimeDiff)
                {
                    var msiUrl = latestMsi.GetProperty("browser_download_url").GetString();
                    if (CurrentMode == UpdateMode.AutomaticUpdate)
                    {
                        await DownloadAndInstallUpdate(msiUrl);
                    }
                    else if (CurrentMode == UpdateMode.NotifyUser)
                    {
                        if (System.Windows.Forms.MessageBox.Show(
                            "A new version of ScreamRouter is available. Would you like to update?",
                            "Update Available",
                            System.Windows.Forms.MessageBoxButtons.YesNo,
                            System.Windows.Forms.MessageBoxIcon.Information) == System.Windows.Forms.DialogResult.Yes)
                        {
                            await DownloadAndInstallUpdate(msiUrl);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Error checking for updates: {ex.Message}",
                    "Update Error",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        private async Task DownloadAndInstallUpdate(string msiUrl)
        {
            Debug.WriteLine($"[UpdateManager] Starting download of update from: {msiUrl}");
            var tempPath = Path.Combine(Path.GetTempPath(), "ScreamRouterUpdate.msi");
            
            try
            {
                // Download the MSI
                Debug.WriteLine("[UpdateManager] Downloading MSI file");
                using (var response = await client.GetAsync(msiUrl))
                {
                    Debug.WriteLine($"[UpdateManager] Download status: {response.StatusCode}");
                    using (var fs = new FileStream(tempPath, FileMode.Create))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }
                Debug.WriteLine($"[UpdateManager] MSI downloaded to: {tempPath}");

                // Verify signature by thumbprint
                Debug.WriteLine("[UpdateManager] Starting signature verification");
                try
                {
                    // Ensure file exists and is accessible
                    if (!File.Exists(tempPath))
                    {
                        throw new Exception("Downloaded MSI file not found");
                    }

                    // Get file info to verify size and check if it's a valid MSI
                    var fileInfo = new FileInfo(tempPath);
                    Debug.WriteLine($"[UpdateManager] MSI file size: {fileInfo.Length} bytes");
                    if (fileInfo.Length == 0)
                    {
                        throw new Exception("Downloaded MSI file is empty");
                    }

                    // Read first few bytes to verify it's an MSI
                    byte[] header = new byte[8];
                    using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read))
                    {
                        fs.Read(header, 0, header.Length);
                    }
                    Debug.WriteLine($"[UpdateManager] File header: {BitConverter.ToString(header)}");

                    try
                    {
                        // Try to get the authenticode signature
                        Debug.WriteLine("[UpdateManager] Attempting to read certificate...");
                        var signer = X509Certificate.CreateFromSignedFile(tempPath);
                        Debug.WriteLine($"[UpdateManager] Successfully read certificate. Subject: {signer.Subject}");
                        
                        // Get full certificate for thumbprint
                        var cert = new X509Certificate2(signer);
                        Debug.WriteLine($"[UpdateManager] Certificate details:");
                        Debug.WriteLine($"  Subject: {cert.Subject}");
                        Debug.WriteLine($"  Thumbprint: {cert.Thumbprint}");
                        Debug.WriteLine($"  Valid from: {cert.NotBefore}");
                        Debug.WriteLine($"  Valid to: {cert.NotAfter}");
                        Debug.WriteLine($"  Serial number: {cert.SerialNumber}");
                        Debug.WriteLine($"  Version: {cert.Version}");
                        
                        const string expectedThumbprint = "42C97DE7E98EDC2D9B49A1F338BCDEE5D07689A1";
                        if (cert.Thumbprint?.ToLower() != expectedThumbprint.ToLower())
                        {
                            Debug.WriteLine("[UpdateManager] Invalid MSI signature - thumbprint mismatch");
                            throw new Exception($"Invalid MSI signature. Expected thumbprint: {expectedThumbprint}, Got: {cert.Thumbprint}");
                        }
                        Debug.WriteLine("[UpdateManager] MSI signature verified successfully");
                    }
                    catch (System.Security.Cryptography.CryptographicException ex)
                    {
                        Debug.WriteLine($"[UpdateManager] Cryptographic error details:");
                        Debug.WriteLine($"  Message: {ex.Message}");
                        Debug.WriteLine($"  Stack trace: {ex.StackTrace}");
                        if (ex.InnerException != null)
                        {
                            Debug.WriteLine($"  Inner exception: {ex.InnerException.Message}");
                            Debug.WriteLine($"  Inner stack trace: {ex.InnerException.StackTrace}");
                        }
                        throw new Exception($"MSI signature verification failed: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UpdateManager] Verification error: {ex.GetType().Name}: {ex.Message}");
                    Debug.WriteLine($"[UpdateManager] Stack trace: {ex.StackTrace}");
                    // Clean up the MSI file if signature verification fails
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                    throw;
                }

                // Install the update
                Debug.WriteLine("[UpdateManager] Starting MSI installation");
                var startInfo = new ProcessStartInfo
                {
                    FileName = "msiexec",
                    Arguments = $"/i \"{tempPath}\" /quiet",
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Debug.WriteLine($"[UpdateManager] Running command: msiexec {startInfo.Arguments}");

                using (var process = Process.Start(startInfo))
                {
                    Debug.WriteLine("[UpdateManager] Waiting for MSI installation to complete");
                    process.WaitForExit();
                    Debug.WriteLine($"[UpdateManager] MSI installation completed with exit code: {process.ExitCode}");
                    if (process.ExitCode != 0)
                    {
                        throw new Exception($"MSI installation failed with code {process.ExitCode}");
                    }
                }
                Debug.WriteLine("[UpdateManager] Update installed successfully, restarting application");

                // Get the path of the current executable
                var exePath = Process.GetCurrentProcess().MainModule.FileName;
                
                // Start the application in a new process
                Process.Start(exePath);
                
                // Exit the current process
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Error installing update: {ex.Message}",
                    "Update Error",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }
    }
}
