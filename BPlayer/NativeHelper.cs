using System.Runtime.InteropServices;

namespace BPlayer;

internal static class NativeHelper
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const int SM_CYSCREEN = 1;

    public static int PrimaryScreenHeight => GetSystemMetrics(SM_CYSCREEN);

    public static (int X, int Y) GetCursorPosition()
    {
        GetCursorPos(out var pt);
        return (pt.X, pt.Y);
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance([MarshalAs(UnmanagedType.LPStruct)] Guid rclsid, nint pUnkOuter, uint dwClsContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out nint ppv);

    private static readonly Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static readonly Guid IID_IFileOpenDialog = new("D57C7288-D4AD-4768-BE02-9D969532D960");

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    private interface IFileOpenDialog
    {
        void Show(nint parent);
        void SetFileTypes(uint cFileTypes, nint rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        uint GetFileTypeIndex();
        uint Advise(nint pfde);
        void Unadvise(uint dwCookie);
        void SetOptions(uint options);
        uint GetOptions();
        void SetDefaultFolder(nint psi);
        void SetFolder(nint psi);
        nint GetFolder();
        nint GetCurrentSelection();
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetFileName();
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        nint GetResult();
        void AddPlace(nint psi, uint fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(uint hr);
        void SetClientGuid([MarshalAs(UnmanagedType.LPStruct)] Guid guid);
        void ClearClientData();
        void SetFilter(nint pFilter);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    private interface IShellItem
    {
        void BindToHandler(nint pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out nint ppv);
        nint GetParent();
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        uint GetAttributes(uint sfgaoMask);
        int Compare(nint psi, uint hint);
    }

    public static string? ShowFolderPicker(nint parentHandle)
    {
        try
        {
            var hr = CoCreateInstance(CLSID_FileOpenDialog, nint.Zero, 1, IID_IFileOpenDialog, out var ppv);
            if (hr != 0) return null;

            var dialog = (IFileOpenDialog)Marshal.GetObjectForIUnknown(ppv);
            dialog.SetOptions(0x00000020); // FOS_PICKFOLDERS
            dialog.Show(parentHandle);
            var shellItemPtr = dialog.GetResult();

            var shellItem = (IShellItem)Marshal.GetObjectForIUnknown(shellItemPtr);
            shellItem.GetDisplayName(0x80028000, out var path); // SIGDN_FILESYSPATH
            Marshal.Release(shellItemPtr);
            Marshal.Release(ppv);
            return path;
        }
        catch
        {
            return null;
        }
    }
}
