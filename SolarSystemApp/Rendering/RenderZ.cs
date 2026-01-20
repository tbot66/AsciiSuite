namespace SolarSystemApp.Rendering
{
    internal static class RenderZ
    {
        // Smaller z = closer (wins)

        public const double UI_BORDER = 0;

        public const double SUN_CORE = 180;
        public const double SUN_CORONA = 210;
        public const double BELT = 110;

        public const double PLANET = 160;
        public const double PLANET_SURFACE = -0.5; // PlanetDrawer uses negative for closer-than-most

        public const double RINGS = -0.55;

        public const double STATION = 140;
        public const double SHIP = 120;
        public const double SHIP_TRAIL = 170;

        public const double ORBITS = 260;
        public const double STARFIELD = 320;
    }
}
