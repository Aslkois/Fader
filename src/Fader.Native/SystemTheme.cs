using Microsoft.Win32;

namespace Fader.Native;

public static class SystemTheme
{
    /// <summary>True when Windows is set to dark mode for apps.</summary>
    public static bool AppsUseDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
