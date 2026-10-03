using UnityEngine;

namespace PSD.Preferences.Theming
{
#if UNITY_EDITOR
    public static class Theme
    {
        private static readonly Color Fallback = Color.magenta;

        public static Color WithAlpha(this Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        private static ThemeObject T => SettingsProvider.SelectedTheme;

        public static Color Background => T?.background ?? Fallback;
        public static Color ContentBackground => T?.contentBackground ?? Fallback;
        public static Color ErrorBackground => T?.errorBackground ?? Fallback;
        public static Color SuccessBackground => T?.successBackground ?? Fallback;

        public static Color TitleText => T?.titleText ?? Fallback;
        public static Color SubtitleText => T?.subtitleText ?? Fallback;
        public static Color ContentText => T?.contentText ?? Fallback;
        public static Color SubtleText => T?.subtleText ?? Fallback;

        public static Color Accent => T?.accent ?? Fallback;
        public static Color AccentBorder => T?.accentBorder ?? Fallback;
        public static Color AccentSubtle => T?.accentSubtle ?? Fallback;
        public static Color AccentBorderSubtle => T?.accentBorderSubtle ?? Fallback;

        public static Color ErrorAccent => T?.errorAccent ?? Fallback;
        public static Color ErrorStrip => T?.errorStrip ?? Fallback;
        public static Color ErrorText => T?.errorText ?? Fallback;
        public static Color ErrorFieldText => T?.errorFieldText ?? Fallback;

        public static Color SuccessAccent => T?.successAccent ?? Fallback;
        public static Color SuccessText => T?.successText ?? Fallback;

        public static Color Shadow => T?.shadow ?? Fallback;
        public static Color ButtonShadowHeavy => T?.buttonShadowHeavy ?? Fallback;
        public static Color ButtonShadowMid => T?.buttonShadowMid ?? Fallback;
        public static Color ButtonShadowLight => T?.buttonShadowLight ?? Fallback;

        public static Color BtnGradientLeftNormal => T?.btnGradientLeftNormal ?? Fallback;
        public static Color BtnGradientLeftHover => T?.btnGradientLeftHover ?? Fallback;
        public static Color BtnGradientLeftActive => T?.btnGradientLeftActive ?? Fallback;
        public static Color BtnGradientRightNormal => T?.btnGradientRightNormal ?? Fallback;
        public static Color BtnGradientRightHover => T?.btnGradientRightHover ?? Fallback;
        public static Color BtnGradientRightActive => T?.btnGradientRightActive ?? Fallback;

        public static Color NavigationSeparator => T?.navigationSeparator ?? Fallback;
        public static Color Separator => T?.separator ?? Fallback;
        public static Color CategoryTabSelected => T?.categoryTabSelected ?? Fallback;
        public static Color CategoryUnderline => T?.categoryUnderline ?? Fallback;
        public static Color CategoryTabText => T?.categoryTabText ?? Fallback;

        public static Color PillActive => T?.pillActive ?? Fallback;
        public static Color PillActiveInvalid => T?.pillActiveInvalid ?? Fallback;
        public static Color PillInvalid => T?.pillInvalid ?? Fallback;
        public static Color PillDisabledContent => T?.pillDisabledContent ?? Fallback;

        public static Color BadgeText => T?.badgeText ?? Fallback;
        public static Color BadgeBackground => T?.badgeBackground ?? Fallback;
    }
#endif
}