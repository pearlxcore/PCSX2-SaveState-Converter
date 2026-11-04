using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SaveStateConverter.Gui;

/// <summary>Asks Windows for a dark title bar to match the dark window (Windows 10 20H1 and later).</summary>
internal static class DarkTitleBar
{
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var enabled = 1;
            if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        };
    }
}
