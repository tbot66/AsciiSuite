using Spectre.Console;

namespace SolarSystemApp.UI.Settings
{
    internal enum CameraSpeed { Slow, Medium, Fast }
    internal enum SmoothingLevel { Low, Medium, High }
    internal enum ThemePreset { Classic, Monochrome, HighContrast, CoolBlue }
    internal enum UIBorderStyle { Single, Double, Rounded, Heavy }

    internal sealed class GameSettings
    {
        public bool ShowOrbits { get; set; } = true;
        public bool ShowLabels { get; set; } = true;
        public bool ShowStarfield { get; set; } = true;
        public bool ShowBelts { get; set; } = true;
        public bool ShowRings { get; set; } = true;
        public bool ShowDebris { get; set; } = true;
        public bool EnableBloom { get; set; } = true;
        public bool EnableLensFlare { get; set; } = true;

        public CameraSpeed ZoomSpeed { get; set; } = CameraSpeed.Medium;
        public CameraSpeed PanSpeed { get; set; } = CameraSpeed.Medium;
        public SmoothingLevel CameraSmoothing { get; set; } = SmoothingLevel.Medium;
        public double OrbitYScale { get; set; } = 0.55;

        public double DefaultTimeScale { get; set; } = 0.25;
        public bool PauseOnSystemEntry { get; set; } = false;

        public ThemePreset ColorTheme { get; set; } = ThemePreset.Classic;
        public UIBorderStyle BorderStyle { get; set; } = UIBorderStyle.Rounded;

        public static double GetZoomResponsiveness(CameraSpeed speed) => speed switch
        {
            CameraSpeed.Slow => 8.0,
            CameraSpeed.Medium => 18.0,
            CameraSpeed.Fast => 30.0,
            _ => 18.0
        };

        public static double GetPanResponsiveness(CameraSpeed speed) => speed switch
        {
            CameraSpeed.Slow => 6.0,
            CameraSpeed.Medium => 14.0,
            CameraSpeed.Fast => 24.0,
            _ => 14.0
        };

        public static readonly double[] OrbitYScalePresets = { 0.3, 0.45, 0.55, 0.7, 0.85 };
        public static readonly double[] TimeScalePresets = { 0.25, 0.5, 1.0, 2.0, 4.0 };

        public static BoxBorder GetBoxBorder(UIBorderStyle style) => style switch
        {
            UIBorderStyle.Single => BoxBorder.Square,
            UIBorderStyle.Double => BoxBorder.Double,
            UIBorderStyle.Rounded => BoxBorder.Rounded,
            UIBorderStyle.Heavy => BoxBorder.Heavy,
            _ => BoxBorder.Rounded
        };
    }
}
