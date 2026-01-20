using SolarSystemApp.World;
using System;

namespace SolarSystemApp
{
    internal static class PlanetTextures
    {
        // ============================================================
        // Palette system (NEW)
        // Keep textures as 0..1 albedo; palettes map albedo -> RGB.
        // ============================================================

        internal enum PaletteId
        {
            Default = 0,
            Variant1 = 1,
            Variant2 = 2,
            Variant3 = 3,
            Variant4 = 4
        }

        // How many variants each texture supports (EarthLike excluded)
     internal static int PaletteVariantCountFor(PlanetDrawer.PlanetTexture tex)
        {
            // EarthLike: you said exclude it (keep its current special handling)
            if (tex == PlanetDrawer.PlanetTexture.EarthLike || tex == PlanetDrawer.PlanetTexture.Continents)
                return 1;

            // More variants where it helps visually
            return tex switch
            {
                PlanetDrawer.PlanetTexture.Rocky => 5,
                PlanetDrawer.PlanetTexture.Barren => 5,
                PlanetDrawer.PlanetTexture.Cratered => 5,
                PlanetDrawer.PlanetTexture.Metallic => 5,
                PlanetDrawer.PlanetTexture.Desert => 5,
                PlanetDrawer.PlanetTexture.Jungle => 4,
                PlanetDrawer.PlanetTexture.Oceanic => 4,
                PlanetDrawer.PlanetTexture.IceWorld => 4,
                PlanetDrawer.PlanetTexture.IceCracked => 4,
                PlanetDrawer.PlanetTexture.Lava => 4,
                PlanetDrawer.PlanetTexture.Toxic => 4,
                PlanetDrawer.PlanetTexture.GasBands => 5,
                PlanetDrawer.PlanetTexture.GasSwirl => 5,
                PlanetDrawer.PlanetTexture.GasStorm => 5,
                _ => 3
            };
        }

        // Deterministic: same planet always gets same palette variant.
        // You can feed pSeed or (seed ^ namehash) from PlanetDrawer.
        internal static PaletteId PickPaletteVariant(int planetSeed, PlanetDrawer.PlanetTexture tex)
        {
            int count = PaletteVariantCountFor(tex);
            if (count <= 1) return PaletteId.Default;

            // hash -> [0..count-1]
            uint x = (uint)planetSeed;
            x ^= 0x9E3779B9u;
            x *= 0x85EBCA6Bu;
            x ^= x >> 13;
            x *= 0xC2B2AE35u;
            x ^= x >> 16;

            int idx = (int)(x % (uint)count);

            return idx switch
            {
                0 => PaletteId.Default,
                1 => PaletteId.Variant1,
                2 => PaletteId.Variant2,
                3 => PaletteId.Variant3,
                _ => PaletteId.Variant4
            };
        }

        // A simple 3-stop palette: dark/mid/light.
        // PlanetDrawer can lerp based on albedo and lighting.
        internal static void GetPalette(
            PlanetDrawer.PlanetTexture tex,
            PaletteId variant,
            out (byte r, byte g, byte b) dark,
            out (byte r, byte g, byte b) mid,
            out (byte r, byte g, byte b) light)
        {
            // NOTE: These are intentionally “reasonable” palette sets.
            // Tweak numbers freely; structure is what matters.

            switch (tex)
            {
                // -------------------------
                // ROCKY / BARREN / CRATERED
                // -------------------------
                case PlanetDrawer.PlanetTexture.Rocky:
                    switch (variant)
                    {
                        default:
                            dark = (55, 52, 50); mid = (95, 90, 86); light = (140, 132, 125); return; // gray stone
                        case PaletteId.Variant1:
                            dark = (60, 48, 38); mid = (112, 86, 62); light = (170, 134, 95); return; // warm sandstone
                        case PaletteId.Variant2:
                            dark = (34, 42, 52); mid = (65, 78, 95); light = (115, 130, 150); return; // slate blue
                        case PaletteId.Variant3:
                            dark = (48, 40, 55); mid = (86, 70, 98); light = (135, 110, 155); return; // purple rock
                        case PaletteId.Variant4:
                            dark = (38, 50, 36); mid = (68, 92, 62); light = (110, 140, 95); return; // mossy stone
                    }

                case PlanetDrawer.PlanetTexture.Barren:
                    switch (variant)
                    {
                        default:
                            dark = (38, 38, 42); mid = (78, 78, 86); light = (125, 125, 138); return; // cold gray
                        case PaletteId.Variant1:
                            dark = (50, 40, 34); mid = (92, 74, 62); light = (145, 118, 98); return; // dusty brown
                        case PaletteId.Variant2:
                            dark = (34, 46, 44); mid = (62, 88, 84); light = (100, 140, 130); return; // oxidized teal
                        case PaletteId.Variant3:
                            dark = (52, 44, 28); mid = (112, 94, 56); light = (175, 150, 90); return; // ochre
                        case PaletteId.Variant4:
                            dark = (30, 30, 30); mid = (60, 60, 60); light = (95, 95, 95); return; // charcoal
                    }

                case PlanetDrawer.PlanetTexture.Cratered:
                    switch (variant)
                    {
                        default:
                            dark = (70, 70, 74); mid = (120, 120, 125); light = (195, 195, 205); return; // moon gray
                        case PaletteId.Variant1:
                            dark = (62, 58, 52); mid = (112, 104, 92); light = (190, 175, 155); return; // dusty tan
                        case PaletteId.Variant2:
                            dark = (52, 58, 70); mid = (95, 105, 125); light = (165, 175, 205); return; // blue-gray
                        case PaletteId.Variant3:
                            dark = (65, 52, 60); mid = (118, 94, 108); light = (200, 160, 185); return; // mauve
                        case PaletteId.Variant4:
                            dark = (54, 48, 40); mid = (96, 86, 72); light = (160, 145, 120); return; // warm neutral
                    }

                // -------------------------
                // METALLIC
                // -------------------------
                case PlanetDrawer.PlanetTexture.Metallic:
                    switch (variant)
                    {
                        default:
                            dark = (60, 70, 78); mid = (120, 140, 155); light = (200, 225, 235); return; // steel
                        case PaletteId.Variant1:
                            dark = (70, 52, 35); mid = (140, 110, 70); light = (230, 190, 120); return; // bronze
                        case PaletteId.Variant2:
                            dark = (55, 60, 72); mid = (105, 120, 150); light = (160, 190, 235); return; // chrome-blue
                        case PaletteId.Variant3:
                            dark = (52, 58, 55); mid = (95, 115, 105); light = (170, 205, 185); return; // verdigris metal
                        case PaletteId.Variant4:
                            dark = (65, 65, 70); mid = (135, 135, 145); light = (240, 240, 248); return; // silver
                    }

                // -------------------------
                // DESERT / JUNGLE / OCEANIC
                // -------------------------
                case PlanetDrawer.PlanetTexture.Desert:
                    switch (variant)
                    {
                        default:
                            dark = (110, 90, 45); mid = (190, 165, 95); light = (240, 225, 155); return; // sand
                        case PaletteId.Variant1:
                            dark = (95, 70, 40); mid = (165, 120, 70); light = (230, 190, 120); return; // red desert
                        case PaletteId.Variant2:
                            dark = (90, 90, 65); mid = (150, 150, 110); light = (210, 210, 160); return; // pale dust
                        case PaletteId.Variant3:
                            dark = (85, 60, 35); mid = (145, 105, 65); light = (210, 170, 110); return; // ochre
                        case PaletteId.Variant4:
                            dark = (80, 72, 50); mid = (140, 128, 90); light = (205, 190, 140); return; // khaki
                    }

                case PlanetDrawer.PlanetTexture.Jungle:
                    switch (variant)
                    {
                        default:
                            dark = (18, 70, 38); mid = (35, 125, 65); light = (90, 190, 105); return; // green
                        case PaletteId.Variant1:
                            dark = (20, 60, 55); mid = (40, 110, 100); light = (95, 175, 160); return; // teal jungle
                        case PaletteId.Variant2:
                            dark = (35, 65, 25); mid = (70, 120, 40); light = (140, 190, 80); return; // olive
                        case PaletteId.Variant3:
                            dark = (35, 55, 45); mid = (70, 105, 85); light = (130, 175, 150); return; // muted
                    }

                case PlanetDrawer.PlanetTexture.Oceanic:
                    switch (variant)
                    {
                        default:
                            dark = (10, 45, 110); mid = (18, 85, 165); light = (85, 170, 195); return; // blue
                        case PaletteId.Variant1:
                            dark = (5, 70, 75); mid = (15, 125, 125); light = (95, 200, 185); return; // turquoise
                        case PaletteId.Variant2:
                            dark = (30, 30, 90); mid = (65, 65, 150); light = (120, 120, 210); return; // deep indigo
                        case PaletteId.Variant3:
                            dark = (15, 55, 60); mid = (30, 105, 110); light = (85, 175, 170); return; // blue-green
                    }

                // -------------------------
                // ICE
                // -------------------------
                case PlanetDrawer.PlanetTexture.IceWorld:
                    switch (variant)
                    {
                        default:
                            dark = (90, 140, 155); mid = (155, 215, 235); light = (235, 245, 255); return; // bright ice
                        case PaletteId.Variant1:
                            dark = (75, 110, 140); mid = (135, 185, 220); light = (220, 240, 255); return; // blue ice
                        case PaletteId.Variant2:
                            dark = (110, 130, 125); mid = (180, 205, 195); light = (245, 250, 245); return; // gray ice
                        case PaletteId.Variant3:
                            dark = (85, 130, 120); mid = (145, 210, 200); light = (230, 250, 250); return; // sea-ice
                    }

                case PlanetDrawer.PlanetTexture.IceCracked:
                    switch (variant)
                    {
                        default:
                            dark = (70, 120, 135); mid = (150, 215, 235); light = (240, 250, 255); return;
                        case PaletteId.Variant1:
                            dark = (80, 105, 120); mid = (165, 200, 215); light = (245, 245, 250); return;
                        case PaletteId.Variant2:
                            dark = (60, 120, 120); mid = (120, 205, 195); light = (225, 250, 245); return;
                        case PaletteId.Variant3:
                            dark = (85, 120, 160); mid = (145, 190, 235); light = (230, 245, 255); return;
                    }

                // -------------------------
                // LAVA / TOXIC
                // -------------------------
                case PlanetDrawer.PlanetTexture.Lava:
                    switch (variant)
                    {
                        default:
                            dark = (55, 20, 15); mid = (165, 55, 25); light = (255, 145, 60); return; // orange lava
                        case PaletteId.Variant1:
                            dark = (40, 15, 25); mid = (120, 40, 95); light = (255, 110, 220); return; // magenta lava
                        case PaletteId.Variant2:
                            dark = (25, 20, 10); mid = (110, 95, 25); light = (255, 235, 80); return; // sulfur
                        case PaletteId.Variant3:
                            dark = (50, 25, 10); mid = (150, 70, 20); light = (255, 120, 40); return; // classic
                    }

                case PlanetDrawer.PlanetTexture.Toxic:
                    switch (variant)
                    {
                        default:
                            dark = (40, 70, 40); mid = (85, 165, 85); light = (170, 235, 170); return; // green toxic
                        case PaletteId.Variant1:
                            dark = (65, 35, 80); mid = (125, 80, 160); light = (200, 160, 240); return; // purple toxic
                        case PaletteId.Variant2:
                            dark = (65, 70, 25); mid = (140, 165, 60); light = (225, 240, 120); return; // yellow-green
                        case PaletteId.Variant3:
                            dark = (30, 60, 70); mid = (55, 130, 150); light = (130, 210, 225); return; // cyan haze
                    }

                // -------------------------
                // GAS GIANTS
                // -------------------------
                case PlanetDrawer.PlanetTexture.GasBands:
                    switch (variant)
                    {
                        default:
                            dark = (160, 140, 95); mid = (210, 190, 130); light = (245, 235, 210); return; // tan
                        case PaletteId.Variant1:
                            dark = (150, 115, 90); mid = (210, 165, 125); light = (255, 220, 175); return; // peach
                        case PaletteId.Variant2:
                            dark = (140, 130, 120); mid = (200, 190, 180); light = (245, 240, 235); return; // warm gray
                        case PaletteId.Variant3:
                            dark = (115, 105, 70); mid = (175, 155, 105); light = (240, 225, 175); return; // dusty ochre
                        case PaletteId.Variant4:
                            dark = (95, 120, 140); mid = (145, 185, 210); light = (220, 245, 255); return; // pale sky
                    }

                case PlanetDrawer.PlanetTexture.GasSwirl:
                    switch (variant)
                    {
                        default:
                            dark = (125, 120, 160); mid = (175, 170, 220); light = (235, 235, 255); return; // lavender
                        case PaletteId.Variant1:
                            dark = (105, 125, 90); mid = (155, 180, 125); light = (220, 235, 200); return; // pale green
                        case PaletteId.Variant2:
                            dark = (120, 150, 165); mid = (165, 200, 210); light = (235, 250, 250); return; // icy blue
                        case PaletteId.Variant3:
                            dark = (140, 100, 150); mid = (200, 150, 220); light = (250, 230, 255); return; // magenta haze
                        case PaletteId.Variant4:
                            dark = (90, 120, 150); mid = (145, 185, 235); light = (235, 250, 255); return; // cyan swirl
                    }

                case PlanetDrawer.PlanetTexture.GasStorm:
                    switch (variant)
                    {
                        default:
                            dark = (80, 90, 110); mid = (130, 145, 170); light = (210, 220, 235); return; // steel blue/gray
                        case PaletteId.Variant1:
                            dark = (85, 80, 95); mid = (145, 135, 160); light = (230, 225, 245); return; // storm lavender-gray
                        case PaletteId.Variant2:
                            dark = (70, 95, 90); mid = (120, 160, 150); light = (210, 235, 230); return; // teal storm
                        case PaletteId.Variant3:
                            dark = (110, 80, 70); mid = (175, 130, 115); light = (245, 220, 210); return; // dusty red storm
                        case PaletteId.Variant4:
                            dark = (70, 70, 70); mid = (135, 135, 135); light = (225, 225, 225); return; // monochrome
                    }

                // -------------------------
                // Fallback
                // -------------------------
                default:
                    dark = (60, 60, 60); mid = (140, 140, 140); light = (230, 230, 230);
                    return;
            }
        }

        // Helper: lerp 3-stop palette by albedo 0..1
        public static (byte r, byte g, byte b) PaletteSample3(
            (byte r, byte g, byte b) dark,
            (byte r, byte g, byte b) mid,
            (byte r, byte g, byte b) light,
            double a01)
        {
            a01 = Clamp01(a01);

            if (a01 < 0.5)
            {
                double t = a01 / 0.5;
                return LerpRgb(dark, mid, t);
            }
            else
            {
                double t = (a01 - 0.5) / 0.5;
                return LerpRgb(mid, light, t);
            }
        }

        private static (byte r, byte g, byte b) LerpRgb((byte r, byte g, byte b) a, (byte r, byte g, byte b) b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;

            int rr = (int)Math.Round(a.r + (b.r - a.r) * t);
            int gg = (int)Math.Round(a.g + (b.g - a.g) * t);
            int bb = (int)Math.Round(a.b + (b.b - a.b) * t);

            return ((byte)rr, (byte)gg, (byte)bb);
        }

        // ============================================================
        // Existing textures (kept exactly)
        // ============================================================

        public static double GasBands(double u, double v)
        {
            double bands = 0.5 + 0.5 * Math.Sin(v * Math.PI * 14.0);
            bands *= 0.85 + 0.15 * Math.Sin(u * Math.PI * 2.0);
            return Clamp01(bands);
        }

        public static double Continents(double u, double v)
        {
            double a = Math.Sin(u * Math.PI * 10.0) * Math.Sin(v * Math.PI * 6.0);
            double b = Math.Sin(u * Math.PI * 4.0 + 1.2) * Math.Sin(v * Math.PI * 9.0 + 0.7);
            double land = a * 0.6 + b * 0.4;

            bool isLand = land > 0.15;
            return isLand ? 0.90 : 0.35;
        }

        public static double IceWorld(double u, double v)
        {
            double cracks = 0.5 + 0.5 * Math.Sin(u * Math.PI * 18.0 + v * Math.PI * 7.0);
            double albedo = 0.75 + 0.25 * cracks;
            return Clamp01(albedo);
        }

        public static double Rocky(double u, double v)
        {
            double s = Math.Sin(u * 37.0) * Math.Sin(v * 29.0);
            double albedo = 0.45 + 0.25 * s;
            return Clamp01(albedo);
        }

        // -------------------------
        // New textures (0..1 outputs) (kept)
        // -------------------------

        public static double Barren(double u, double v)
        {
            double s = 0.5 + 0.5 * Math.Sin(u * 9.0 + Math.Sin(v * 7.0));
            double albedo = 0.28 + 0.18 * s;
            return Clamp01(albedo);
        }

        public static double Cratered(double u, double v)
        {
            double baseRock = Rocky(u, v);

            double p = Math.Sin(u * 23.0) * Math.Sin(v * 17.0);
            double dents = 0.5 + 0.5 * Math.Sin((u * 31.0 + v * 29.0) + p * 2.0);

            double craterMask = Step(0.72, dents);
            double craterDarken = 1.0 - 0.35 * craterMask;

            return Clamp01(baseRock * craterDarken);
        }

        public static double Metallic(double u, double v)
        {
            double s = 0.5 + 0.5 * Math.Sin(u * 16.0 + v * 11.0);
            double fine = 0.5 + 0.5 * Math.Sin(u * 63.0) * Math.Sin(v * 59.0);

            double albedo = 0.45 + 0.35 * (s * 0.7 + fine * 0.3);
            return Clamp01(albedo);
        }

        public static double Desert(double u, double v)
        {
            double dunes = 0.5 + 0.5 * Math.Sin(v * Math.PI * 20.0 + Math.Sin(u * Math.PI * 2.0) * 2.0);
            double albedo = 0.55 + 0.25 * dunes;
            return Clamp01(albedo);
        }

        public static double Jungle(double u, double v)
        {
            double m1 = 0.5 + 0.5 * Math.Sin(u * 19.0) * Math.Sin(v * 13.0);
            double m2 = 0.5 + 0.5 * Math.Sin(u * 41.0 + 1.3) * Math.Sin(v * 37.0 + 0.9);
            double albedo = 0.42 + 0.28 * (m1 * 0.6 + m2 * 0.4);
            return Clamp01(albedo);
        }

        public static double Oceanic(double u, double v)
        {
            double waves = 0.5 + 0.5 * Math.Sin(u * Math.PI * 8.0 + v * Math.PI * 14.0);
            double albedo = 0.22 + 0.18 * waves;
            return Clamp01(albedo);
        }

        public static double EarthLike(double u, double v)
        {
            double landMask = Continents(u, v);
            double lat = Math.Abs(v - 0.5) * 2.0;
            double iceBoost = SmoothStep(0.78, 1.0, lat) * 0.15;
            return Clamp01(landMask + iceBoost);
        }

        public static double Lava(double u, double v)
        {
            double rock = 0.25 + 0.15 * Math.Sin(u * 17.0) * Math.Sin(v * 15.0);
            double cracks = 0.5 + 0.5 * Math.Sin(u * 33.0 + v * 27.0);
            double glowMask = SmoothStep(0.72, 0.95, cracks);
            double albedo = rock + 0.55 * glowMask;
            return Clamp01(albedo);
        }

        public static double Toxic(double u, double v)
        {
            double c1 = 0.5 + 0.5 * Math.Sin(u * 6.0 + Math.Sin(v * 4.0));
            double c2 = 0.5 + 0.5 * Math.Sin(u * 13.0 + v * 9.0);
            double albedo = 0.30 + 0.35 * (c1 * 0.6 + c2 * 0.4);
            return Clamp01(albedo);
        }

        public static double IceCracked(double u, double v)
        {
            double baseIce = 0.75;
            double cracks = 0.5 + 0.5 * Math.Sin(u * 28.0 + Math.Sin(v * 9.0) * 3.0);
            double crackMask = SmoothStep(0.70, 0.92, cracks);
            double albedo = baseIce - 0.22 * crackMask;
            return Clamp01(albedo);
        }

        public static double GasSwirl(double u, double v)
        {
            // Stronger twist so it reads as "curving" bands, not just stripes
            double twist = Math.Sin(u * Math.PI * 2.0) * 0.30; // was 0.15

            // Distorted latitude bands
            double bands = 0.5 + 0.5 * Math.Sin((v + twist) * Math.PI * 16.0);

            // Add a stronger cross-flow so it looks like rotation / turbulence
            double flow = 0.5 + 0.5 * Math.Sin(u * Math.PI * 8.0 + v * Math.PI * 5.0);

            // Push contrast slightly so it survives palette quantization
            double a = bands * (0.55 + 0.45 * flow);
            a = Math.Pow(Clamp01(a), 0.90);

            return Clamp01(a);
        }

        public static double GasStorm(double u, double v)
        {
            // Base stripe structure so it still reads as a gas giant
            double baseBands = GasBands(u, v);

            // Large-scale storm "cells" (big blobs)
            double cellX = 0.5 + 0.5 * Math.Sin(u * Math.PI * 3.0 + 1.1);
            double cellY = 0.5 + 0.5 * Math.Sin(v * Math.PI * 2.0 + 0.7);
            double cells = cellX * 0.6 + cellY * 0.4;

            // Mid-scale turbulence
            double turb = 0.5 + 0.5 * Math.Sin(u * Math.PI * 11.0 + Math.Sin(v * Math.PI * 2.0) * 3.0);

            // Storm mask: fewer, bigger features
            double stormMask = SmoothStep(0.62, 0.92, turb) * SmoothStep(0.40, 0.85, cells);

            // Make storms brighten the albedo noticeably (so they show up)
            double a = baseBands * 0.75 + 0.40 * stormMask;

            // Slightly higher contrast so storm cores pop
            a = Math.Pow(Clamp01(a), 1.10);

            return Clamp01(a);
        }

        // ============================================================
        // Helpers (kept)
        // ============================================================

        public static double Clamp01(double x)
        {
            if (x < 0) return 0;
            if (x > 1) return 1;
            return x;
        }

        private static double Step(double edge, double x) => x >= edge ? 1.0 : 0.0;

        private static double SmoothStep(double a, double b, double x)
        {
            if (x <= a) return 0.0;
            if (x >= b) return 1.0;
            double t = (x - a) / (b - a);
            return t * t * (3.0 - 2.0 * t);
        }
    }
}
