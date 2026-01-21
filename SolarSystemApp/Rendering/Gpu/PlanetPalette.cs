namespace SolarSystemApp.Rendering.Gpu
{
    public readonly struct PlanetPalette
    {
        public readonly float DarkR;
        public readonly float DarkG;
        public readonly float DarkB;
        public readonly float MidR;
        public readonly float MidG;
        public readonly float MidB;
        public readonly float LightR;
        public readonly float LightG;
        public readonly float LightB;

        public PlanetPalette(
            float darkR, float darkG, float darkB,
            float midR, float midG, float midB,
            float lightR, float lightG, float lightB)
        {
            DarkR = darkR;
            DarkG = darkG;
            DarkB = darkB;
            MidR = midR;
            MidG = midG;
            MidB = midB;
            LightR = lightR;
            LightG = lightG;
            LightB = lightB;
        }
    }
}
