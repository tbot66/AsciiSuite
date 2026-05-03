namespace SolarSystemApp.Core
{
    // Higher Z draws first (behind). Lower Z draws last (front).
    public static class ZLayers
    {
        public const double Starfield = 300;

        public const double Orbits = 230;
        public const double Belts = 210;

        public const double Trails = 170;

        public const double SunCorona = 160;
        public const double SunDisc = 140;

        public const double RingsBack = 120;
        public const double Bodies = 100;
        public const double RingsFront = 80;

        public const double Labels = 20;
        public const double UI = 0;
    }
}
