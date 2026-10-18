using Microsoft.Win32;
using System;
using System.Windows.Media;

namespace LockStepAI.Services
{
    public static class ThemeService
    {
        public static event Action? ThemeChanged;

        static ThemeService()
        {
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General) ThemeChanged?.Invoke();
            };
        }

        public static bool IsDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int i && i == 0;
            }
            catch { return true; }
        }

        // CLEAN DEEP BLACK & WHITE THEME
        public static Color GetBackgroundColor() => IsDarkMode() ? Color.FromRgb(10, 10, 10) : Color.FromRgb(255, 255, 255);
        public static Color GetHeaderColor() => IsDarkMode() ? Color.FromRgb(18, 18, 18) : Color.FromRgb(240, 240, 240);
        public static Color GetTextColor() => IsDarkMode() ? Colors.White : Color.FromRgb(20, 20, 20);
        public static Color GetUserBubbleColor() => IsDarkMode() ? Color.FromRgb(40, 40, 40) : Color.FromRgb(220, 220, 220);
        public static Color GetAssistantBubbleColor() => IsDarkMode() ? Color.FromRgb(25, 25, 25) : Color.FromRgb(245, 245, 245);
        public static Color GetBorderColor() => IsDarkMode() ? Color.FromRgb(50, 50, 50) : Color.FromRgb(200, 200, 200);

        public static SolidColorBrush GetBackgroundBrush() => new(GetBackgroundColor());
        public static SolidColorBrush GetTextBrush() => new(GetTextColor());
    }
}