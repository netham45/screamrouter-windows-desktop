using System;
using System.Diagnostics;
using System.IO;

namespace ScreamRouterDesktop
{
    public static class Logger
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreamRouter",
            "screamrouter.log"
        );

        static Logger()
        {
            var logDir = Path.GetDirectoryName(LogFilePath);
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
        }

        public static void Log(string component, string message)
        {
            
            try
            {
                var logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{component}] {message}";
                Debug.WriteLine(logMessage);
                File.AppendAllText(LogFilePath, logMessage + Environment.NewLine);
            }
            catch
            {
                // Silently fail if logging fails
            }
        }
    }
}
