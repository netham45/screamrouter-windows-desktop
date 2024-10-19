using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows.Forms;
using System.Text;

namespace ScreamRouterDesktop
{
    public static class StartMenuPinning
    {
        [DllImport("shell32.dll", SetLastError = true)]
        private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

        private const int SHCNE_ASSOCCHANGED = 0x8000000;
        private const int SHCNF_IDLIST = 0x0;

        public static void PinToStartMenu()
        {
            try
            {
                string appPath = Application.ExecutablePath;
                string shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ScreamRouter Desktop.lnk");

                IShellLink link = (IShellLink)new ShellLink();
                link.SetPath(appPath);
                link.SetWorkingDirectory(Path.GetDirectoryName(appPath) ?? string.Empty);
                link.SetDescription("ScreamRouter Desktop Application");

                IPersistFile file = (IPersistFile)link;
                file.Save(shortcutPath, false);

                // Pin the shortcut to Start Menu
                // Note: This method of pinning to Start Menu is not supported in Windows 10 and later
                // You may need to use alternative methods or inform the user to pin manually
                MessageBox.Show("The shortcut has been created. Please manually pin it to the Start Menu.", "Pin to Start Menu", MessageBoxButtons.OK, MessageBoxIcon.Information);

                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating shortcut: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    internal interface IPersistFile
    {
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    }
}