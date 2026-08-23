using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MediaLock.App.Theming;

internal static class WindowFrameTheme
{
    private const int UseImmersiveDarkMode = 20;

    public static bool UsesImmersiveDarkMode(UiThemeKind theme) =>
        theme == UiThemeKind.Dark;

    public static bool TryApply(Window window, UiThemeKind theme)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return false;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var enabled = UsesImmersiveDarkMode(theme) ? 1 : 0;
        return DwmSetWindowAttribute(
            handle,
            UseImmersiveDarkMode,
            ref enabled,
            Marshal.SizeOf<int>()) >= 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
