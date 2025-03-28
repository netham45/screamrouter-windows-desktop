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
                    var result = System.Windows.Forms.MessageBox.Show(
                        "How would you like ScreamRouter to handle updates?\n\n" +
                        "Yes = Automatically install updates\n" +
                        "No = Notify me when updates are available\n" +
                        "Cancel = Never check for updates",
                        "ScreamRouter Update Settings",
                        System.Windows.Forms.MessageBoxButtons.YesNoCancel,
                        System.Windows.Forms.MessageBoxIcon.Question);

                    var selectedMode = result switch
                    {
                        System.Windows.Forms.DialogResult.Yes => UpdateMode.AutomaticUpdate,
                        System.Windows.Forms.DialogResult.No => UpdateMode.NotifyUser,
                        _ => UpdateMode.DoNotCheck
                    };

                    key.SetValue(RegistryValue, (int)selectedMode);
                    return selectedMode;
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
            if (CurrentMode == UpdateMode.DoNotCheck)
                return;

            try
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ScreamRouter/1.0");
                var response = await client.GetStringAsync(GithubApiUrl);
                var releases = JsonSerializer.Deserialize<JsonElement>(response);

                var latestRelease = releases.EnumerateArray().First();
                var latestMsi = latestRelease.GetProperty("assets").EnumerateArray()
                    .FirstOrDefault(x => x.GetProperty("name").GetString().EndsWith(".msi"));

                if (latestMsi.ValueKind == JsonValueKind.Undefined)
                    return;

                var currentExe = Process.GetCurrentProcess().MainModule.FileName;
                var currentBuildDate = File.GetLastWriteTime(currentExe);
                var latestBuildDate = DateTime.Parse(latestMsi.GetProperty("created_at").GetString());

                if (latestBuildDate > currentBuildDate)
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
            var tempPath = Path.Combine(Path.GetTempPath(), "ScreamRouterUpdate.msi");
            
            try
            {
                // Download the MSI
                using (var response = await client.GetAsync(msiUrl))
                using (var fs = new FileStream(tempPath, FileMode.Create))
                {
                    await response.Content.CopyToAsync(fs);
                }

                // Verify signature by thumbprint
                var cert = new X509Certificate2(tempPath);
                const string expectedThumbprint = "42C97DE7E98EDC2D9B49A1F338BCDEE5D07689A1";
                if (cert.Thumbprint?.ToLower() != expectedThumbprint.ToLower())
                {
                    throw new Exception($"Invalid MSI signature. Expected thumbprint: {expectedThumbprint}, Got: {cert.Thumbprint}");
                }

                // Install the update
                var startInfo = new ProcessStartInfo
                {
                    FileName = "msiexec",
                    Arguments = $"/i \"{tempPath}\" /quiet",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using (var process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        throw new Exception($"MSI installation failed with code {process.ExitCode}");
                    }
                }

                System.Windows.Forms.MessageBox.Show(
                    "Update installed successfully. Please restart the application.",
                    "Update Complete",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
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
