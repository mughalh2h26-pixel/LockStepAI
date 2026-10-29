using System.Windows.Media;

namespace LockStepAI.Services
{
    public static class ThemeService
    {
        public static bool IsDarkMode() => true;

        public static Color GetBackgroundColor() => Color.FromRgb(10, 10, 10);
        public static Color GetHeaderColor() => Color.FromRgb(18, 18, 18);
        public static Color GetTextColor() => Colors.White;
        public static Color GetUserBubbleColor() => Color.FromRgb(40, 40, 40);
        public static Color GetAssistantBubbleColor() => Color.FromRgb(25, 25, 25);
        public static Color GetBorderColor() => Color.FromRgb(50, 50, 50);

        public static SolidColorBrush GetBackgroundBrush() => new(GetBackgroundColor());
        public static SolidColorBrush GetTextBrush() => new(GetTextColor());
    }
}