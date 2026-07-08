using System.Runtime.InteropServices;

namespace BPlayer.ThumbnailCore;

internal static class NativeMethods
{
    // Win32
    internal const uint WS_POPUP = 0x80000000;
    internal const uint WS_EX_NOACTIVATE = 0x08000000;
    internal const uint WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName,
        string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr hWnd);

    // libvlc
    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr libvlc_new(int argc,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)]
        string[] argv);

    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr libvlc_media_new_path(IntPtr instance,
        [MarshalAs(UnmanagedType.LPStr)] string path);

    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void libvlc_media_release(IntPtr media);

    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr libvlc_media_player_new_from_media(IntPtr media);

    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void libvlc_media_player_release(IntPtr mp);

    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void libvlc_media_player_set_hwnd(IntPtr mp, IntPtr hwnd);

    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int libvlc_media_player_play(IntPtr mp);

    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int libvlc_media_player_is_playing(IntPtr mp);

    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void libvlc_media_player_set_position(IntPtr mp, float position);

    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int libvlc_video_take_snapshot(IntPtr mp, uint num,
        [MarshalAs(UnmanagedType.LPStr)] string path, uint width, uint height);

    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void libvlc_media_player_stop(IntPtr mp);

    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void libvlc_release(IntPtr instance);
}
