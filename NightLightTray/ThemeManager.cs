using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NightLightTray
{
    internal static class ThemeManager
    {
        private const string PersonalizePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public static readonly Color DefaultAccent = Color.FromArgb(0x4C, 0xC2, 0xFF);

        public static bool IsDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizePath))
                {
                    object value = key?.GetValue("AppsUseLightTheme");
                    if (value is int i)
                    {
                        return i == 0;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public static Color Accent()
        {
            uint color = 0;
            try
            {
                DwmGetColorizationColor(out color, out _);
                int r = (int)((color >> 16) & 0xFF);
                int g = (int)((color >> 8) & 0xFF);
                int b = (int)(color & 0xFF);
                if (r + g + b < 120)
                {
                    return DefaultAccent;
                }
                return Color.FromArgb(255, r, g, b);
            }
            catch
            {
                return DefaultAccent;
            }
        }

        public static Font UiFont(float size, FontStyle style)
        {
            try
            {
                return new Font("Segoe UI Variable", size, style);
            }
            catch
            {
                return new Font("Segoe UI", size, style);
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetColorizationColor(out uint pcrColorization, out bool pfOpaqueBlend);
    }
}
