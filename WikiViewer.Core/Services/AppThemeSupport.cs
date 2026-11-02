namespace WikiViewer.Core.Services
{
    public static class AppThemeSupport
    {
        public static AppAppearance FromLegacyValue(int value) => value == 2 ? AppAppearance.Windows11SunValley :
            value == 3 ? AppAppearance.Windows10Fluent : value == 1 ? AppAppearance.Windows10 : AppAppearance.Automatic;
        public static int MinimumBuild(AppAppearance theme)
        {
            switch (theme)
            {
                case AppAppearance.Windows10: return 15063;
                case AppAppearance.Windows10Fluent: return 16299;
                case AppAppearance.Windows11SunValley: return 22000;
                default: return 0;
            }
        }

        public static bool IsSupported(AppAppearance theme, int build) =>
            (theme == AppAppearance.Automatic || theme == AppAppearance.Windows10 || theme == AppAppearance.Windows10Fluent || theme == AppAppearance.Windows11SunValley) && build >= MinimumBuild(theme);

        public static AppAppearance Resolve(AppAppearance requested, int build)
        {
            if (requested != AppAppearance.Automatic && IsSupported(requested, build)) return requested;
            if (build >= 22000) return AppAppearance.Windows11SunValley;
            if (build >= 16299) return AppAppearance.Windows10Fluent;
            return AppAppearance.Windows10;
        }
    }
}
