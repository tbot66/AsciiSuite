using System;
using AsciiEngine;
using SolarSystemApp.Util;

namespace SolarSystemApp.World
{
    internal static class PlanetDrawer
    {
        // IMPORTANT: keep existing entries, only add new ones.
        internal enum PlanetTexture
        {
            Rocky = 1,
            Continents = 2,
            GasBands = 3,
            IceWorld = 4,

            Barren = 10,
            Cratered = 11,
            Metallic = 12,
            Desert = 13,
            Jungle = 14,
            Oceanic = 15,
            EarthLike = 16,
            Lava = 17,
            Toxic = 18,
            IceCracked = 19,

            GasSwirl = 30,
            GasStorm = 31
        }

        internal static int SunRadiusChars = 8;
        internal static string Ramp = " .:-=+*#";
        internal static bool PlanetColorOnlyShading = true;
        internal static bool ForceRampShading = false;
        private const char PlanetSolidGlyph = '█';

        private const int PlanetBlock2Threshold = 18;
        private const int PlanetBlock3Threshold = 36;
        private const int MoonBlock2Threshold = 14;
        private const int MoonBlock3Threshold = 28;

        private const int ShadowSkipThreshold = 14;

        private const double PlanetZBase = -0.50;
        private const double MoonZBase = -0.52;
        private const double ZDepthScale = 0.002;

        private const double AtmosphereWidth = 0.10;
        private const double AtmosphereMax = 0.35;
        private static readonly Dictionary<int, int> _planetStepCache = new();
        private static readonly Dictionary<int, int> _moonStepCache = new();

        private static double ClampZ(double z)
        {
            if (z < -5.0) return -5.0;
            if (z > 5.0) return 5.0;
            return z;
        }

        internal static double SpinTurns(double simTime, double spinSpeed)
        {
            return (simTime * spinSpeed) / (Math.PI * 2.0);
        }

        private static double MoonSpinTurns(double simTime, Moon moon, int seed)
        {
            if (Math.Abs(moon.SpinSpeed) > 1e-6)
                return SpinTurns(simTime, moon.SpinSpeed);

            double period = Math.Max(0.001, moon.LocalPeriod);
            if (period <= 0.0001)
            {
                double fallback = 0.6 + 1.4 * HashNoise.Hash01(seed, 73, 91);
                return SpinTurns(simTime, fallback);
            }

            double ang = moon.LocalPhase + (simTime * (Math.PI * 2.0) / period);
            return ang / (Math.PI * 2.0);
        }

        private static Color BlendRgb(Color a, Color b, double t)
        {
            t = MathUtil.Clamp(t, 0.0, 1.0);
            if (a.Mode != ColorMode.Rgb24 || b.Mode != ColorMode.Rgb24) return a;

            int av = a.Value, bv = b.Value;
            int ar = (av >> 16) & 255, ag = (av >> 8) & 255, ab = av & 255;
            int br = (bv >> 16) & 255, bg = (bv >> 8) & 255, bb = bv & 255;

            int rr = ClampToByte((int)Math.Round(ar + (br - ar) * t));
            int gg = ClampToByte((int)Math.Round(ag + (bg - ag) * t));
            int bb2 = ClampToByte((int)Math.Round(ab + (bb - ab) * t));

            return Color.FromRgb((byte)rr, (byte)gg, (byte)bb2);
        }

        private static Color AddTint(Color baseRgb, Color tintRgb, double amt)
        {
            amt = MathUtil.Clamp(amt, 0.0, 1.0);
            if (amt <= 0.00001) return baseRgb;
            if (baseRgb.Mode != ColorMode.Rgb24 || tintRgb.Mode != ColorMode.Rgb24) return baseRgb;
            return BlendRgb(baseRgb, tintRgb, amt);
        }

        private static Color AtmosphereTint(PlanetTexture tex)
        {
            return tex switch
            {
                PlanetTexture.EarthLike or PlanetTexture.Continents or PlanetTexture.Oceanic => Color.FromRgb(120, 190, 255),
                PlanetTexture.Jungle => Color.FromRgb(140, 220, 170),
                PlanetTexture.Toxic => Color.FromRgb(140, 255, 150),
                PlanetTexture.IceWorld or PlanetTexture.IceCracked => Color.FromRgb(190, 245, 255),
                PlanetTexture.Lava => Color.FromRgb(255, 175, 90),
                PlanetTexture.GasBands or PlanetTexture.GasSwirl or PlanetTexture.GasStorm => Color.FromRgb(200, 220, 255),
                _ => Color.FromRgb(200, 220, 255),
            };
        }

        // -------------------------
        // Occluders for shadows
        // -------------------------
        internal readonly struct Occluder
        {
            public readonly int X;
            public readonly int Y;
            public readonly int R;
            public Occluder(int x, int y, int r) { X = x; Y = y; R = r; }
        }

        public static void DrawPlanet(
            ConsoleRenderer r,
            int cx, int cy,
            int radChars,
            int seed,
            Planet p,
            double t,
            int sunX, int sunY)
        {
            DrawPlanet(r, cx, cy, radChars, seed, p, t, sunX, sunY, ReadOnlySpan<Occluder>.Empty, allowLod: false);
        }

        public static void DrawPlanet(
            ConsoleRenderer r,
            int cx, int cy,
            int radChars,
            int seed,
            Planet p,
            double t,
            int sunX, int sunY,
            ReadOnlySpan<Occluder> occluders,
            bool allowLod)
        {
            if (radChars <= 0) return;

            double planetZ = ClampZ(PlanetZBase + (p.WZ * ZDepthScale));
            double atmosphereZ = ClampZ(planetZ - 0.005);

            ComputeLightDir(cx, cy, sunX, sunY, out double lx, out double ly);

            int pSeed = seed ^ (p.Name?.GetHashCode() ?? 0);
            int ditherOx = pSeed & 3;
            int ditherOy = (pSeed >> 2) & 3;

            PlanetTextures.PaletteId pal = default;
            bool useVariantPalette = (p.Texture != PlanetTexture.EarthLike && p.Texture != PlanetTexture.Continents);
            if (useVariantPalette)
                pal = PlanetTextures.PickPaletteVariant(pSeed, p.Texture);

            double spinTurns = SpinTurns(t, p.SpinSpeed);
            double tilt = p.AxisTilt;

            int stepSize = 1;

            if (allowLod)
            {
                _planetStepCache.TryGetValue(pSeed, out int last);
                stepSize = ApplyLodHysteresis(radChars, last, PlanetBlock2Threshold, PlanetBlock3Threshold, hysteresisChars: 2);
                _planetStepCache[pSeed] = stepSize;
            }
            else
            {
                stepSize = 1;
                _planetStepCache[pSeed] = 1;
            }

            const double PixelAspectY = 2.0;

            for (int y = -radChars; y <= radChars; y += stepSize)
            {
                for (int x = -radChars; x <= radChars; x += stepSize)
                {
                    int sx0 = x + (stepSize / 2);
                    int sy0 = y + (stepSize / 2);

                    double nx = sx0 / (double)radChars;
                    double ny = sy0 / (double)radChars;
                    double nyA = ny * PixelAspectY;

                    double rrA = nx * nx + nyA * nyA;
                    if (rrA > 1.0) continue;

                    double nz = Math.Sqrt(Math.Max(0.0, 1.0 - rrA));

                    double invLen = 1.0 / Math.Sqrt(nx * nx + nyA * nyA + nz * nz + 1e-9);
                    double nxN = nx * invLen;
                    double nyN = nyA * invLen;
                    double nzN = nz * invLen;

                    double txN = nxN;
                    double tyN = nyN;
                    double tzN = nzN;

                    if (Math.Abs(tilt) > 1e-6)
                    {
                        double ct = Math.Cos(tilt);
                        double st = Math.Sin(tilt);

                        double x2 = txN * ct - tyN * st;
                        double y2 = txN * st + tyN * ct;
                        txN = x2;
                        tyN = y2;
                    }

                    double ndotlRaw = nxN * lx + nyN * ly + nzN * 0.72;
                    double ndotl01 = SmoothStep(-0.18, 0.88, ndotlRaw);

                    double limb = 0.78 + 0.22 * nzN;
                    ndotl01 *= limb;

                    int px = cx + sx0;
                    int py = cy + sy0;

                    double shadowMul = ShadowFactor(px, py, lx, ly, occluders,
                        selfCx: cx, selfCy: cy, selfR: radChars,
                        sunX: sunX, sunY: sunY);

                    double lit = MathUtil.Clamp(ndotl01 * shadowMul, 0.0, 1.0);

                    SamplePlanetEx(pSeed, p.Texture, txN, tyN, tzN, spinTurns,
                        out Color fg, out char texGlyph,
                        out double emissive01, out Color emissiveColor);

                    double rim = Math.Pow(MathUtil.Clamp(1.0 - nzN, 0.0, 1.0), 1.8);
                    double rimBoost = (ndotlRaw > -0.05) ? (0.10 * rim) : (0.04 * rim);

                    fg = ShadeColorForLight(fg, lit, stronger: true);
                    fg = AddRimLift(fg, rimBoost);

                    if (emissive01 > 0.00001)
                    {
                        double night = MathUtil.Clamp((0.22 - ndotlRaw) / 0.70, 0.0, 1.0);
                        double e = emissive01 * night * (0.55 + 0.45 * shadowMul);

                        if (e > 0.00001)
                        {
                            fg = AddTint(fg, emissiveColor, MathUtil.Clamp(e, 0.0, 0.85));
                            fg = AddRimLift(fg, e * 0.25);
                            lit = MathUtil.Clamp(Math.Max(lit, e * 0.70), 0.0, 1.0);
                        }
                    }

                    char glyph = PlanetGlyph(texGlyph, sx0, sy0, ditherOx, ditherOy, lit);

                    for (int by = 0; by < stepSize; by++)
                    {
                        for (int bx = 0; bx < stepSize; bx++)
                        {
                            int lxCell = x + bx;
                            int lyCell = y + by;

                            double nnx = lxCell / (double)radChars;
                            double nny = lyCell / (double)radChars;
                            double nnyA2 = nny * PixelAspectY;

                            if (nnx * nnx + nnyA2 * nnyA2 > 1.0) continue;

                            int wx = cx + lxCell;
                            int wy = cy + lyCell;

                            r.Set(wx, wy, glyph, fg, Colors.Black, z: planetZ);
                        }
                    }
                }
            }

            DrawAtmosphereHalo(
                r,
                cx, cy,
                radChars,
                p.Texture,
                atmosphereZ,
                lx, ly,
                sunX, sunY,
                occluders);

            if (p.HasRings)
            {
                double ringZ = ClampZ(planetZ - 0.05);
                DrawRings(r, cx, cy, radChars, pSeed, p.Texture, pal, t, z: ringZ, lx, ly, sunX, sunY, occluders);
            }
        }

        // ------------------------------------------------------------
        // Moon rendering (kept, but now uses the same patch behavior)
        // ------------------------------------------------------------
        public static void DrawMoon(
            ConsoleRenderer r,
            int cx, int cy,
            int radChars,
            int seed,
            Moon m,
            double t,
            int sunX, int sunY,
            ReadOnlySpan<Occluder> occluders,
            bool allowLod)
        {
            if (radChars <= 0) return;

            double moonZ = ClampZ(MoonZBase + (m.WZ * ZDepthScale));

            ComputeLightDir(cx, cy, sunX, sunY, out double lx, out double ly);

            int mSeed = seed ^ (m.Name?.GetHashCode() ?? 0);
            int ditherOx = mSeed & 3;
            int ditherOy = (mSeed >> 2) & 3;
            double spinTurns = MoonSpinTurns(t, m, mSeed);

            int stepSize = 1;

            if (allowLod)
            {
                _moonStepCache.TryGetValue(mSeed, out int last);
                stepSize = ApplyLodHysteresis(radChars, last, MoonBlock2Threshold, MoonBlock3Threshold, hysteresisChars: 2);
                _moonStepCache[mSeed] = stepSize;
            }
            else
            {
                stepSize = 1;
                _moonStepCache[mSeed] = 1;
            }

            for (int y = -radChars; y <= radChars; y += stepSize)
            {
                for (int x = -radChars; x <= radChars; x += stepSize)
                {
                    int sx0 = x + (stepSize / 2);
                    int sy0 = y + (stepSize / 2);

                    double nx = sx0 / (double)radChars;
                    double ny = sy0 / (double)radChars;
                    double rr = nx * nx + ny * ny;
                    if (rr > 1.0) continue;

                    double nz = Math.Sqrt(Math.Max(0.0, 1.0 - rr));

                    double ndotlRaw = nx * lx + ny * ly + nz * 0.72;
                    double ndotl01 = SmoothStep(-0.18, 0.88, ndotlRaw);
                    double limb = 0.80 + 0.20 * nz;
                    ndotl01 *= limb;

                    int px = cx + sx0;
                    int py = cy + sy0;

                    double shadowMul = ShadowFactor(px, py, lx, ly, occluders,
                        selfCx: cx, selfCy: cy, selfR: radChars,
                        sunX: sunX, sunY: sunY);

                    double lit = MathUtil.Clamp(ndotl01 * shadowMul, 0.0, 1.0);

                    SamplePlanet(mSeed, m.Texture, nx, ny, nz, spinTurns, out Color fg, out char texGlyph);

                    fg = ShadeColorForLight(fg, lit, stronger: true);

                    char glyph = PlanetGlyph(texGlyph, sx0, sy0, ditherOx, ditherOy, lit);

                    for (int by = 0; by < stepSize; by++)
                    {
                        for (int bx = 0; bx < stepSize; bx++)
                        {
                            int lxCell = x + bx;
                            int lyCell = y + by;

                            double nnx = lxCell / (double)radChars;
                            double nny = lyCell / (double)radChars;
                            if (nnx * nnx + nny * nny > 1.0) continue;

                            int wx = cx + lxCell;
                            int wy = cy + lyCell;

                            r.Set(wx, wy, glyph, fg, Colors.Black, z: moonZ);
                        }
                    }
                }
            }
        }

        // -------------------------
        // Rings (deterministic palettes) + subtle rotation
        // -------------------------
        private static void DrawRings(
            ConsoleRenderer r,
            int cx, int cy,
            int radChars,
            int pSeed,
            PlanetTexture planetTex,
            PlanetTextures.PaletteId pal,
            double t,
            double z,
            double lx, double ly,
            int sunX, int sunY,
            ReadOnlySpan<Occluder> occluders)
        {
            double ang = HashNoise.Hash01(pSeed, 777, 999) * Math.PI;
            double ca = Math.Cos(ang);
            double sa = Math.Sin(ang);

            const double InnerMin = 1.15;
            const double InnerMax = 1.35;
            const double OuterMin = 1.35;
            const double OuterMax = 1.75;

            double rIn = HashNoise.Hash01(pSeed, 10, 20);
            double rOut = HashNoise.Hash01(pSeed, 30, 40);

            double innerMul = InnerMin + (InnerMax - InnerMin) * rIn;
            double outerMul = OuterMin + (OuterMax - OuterMin) * rOut;

            if (outerMul < innerMul + 0.20) outerMul = innerMul + 0.20;

            double inner = radChars * innerMul;
            double outer = radChars * outerMul;

            // ring plane tilt (visual thickness)
            double tilt = 0.22 + 0.55 * HashNoise.Hash01(pSeed, 50, 60);

            int box = (int)Math.Ceiling(outer / Math.Max(0.15, tilt)) + 2;
            double invSpan = 1.0 / Math.Max(1e-6, (outer - inner));
            double invOuter = 1.0 / Math.Max(1.0, outer);

            // Build ring colors from the planet's palette variant, then "dustify" them
            PlanetTextures.GetPalette(planetTex, pal, out var dark, out var mid, out var light);

            Color c2 = Dustify(Color.FromRgb(dark.r, dark.g, dark.b), amount: 0.55);   // darkest grains
            Color c1 = Dustify(Color.FromRgb(mid.r, mid.g, mid.b), amount: 0.45);
            Color c0 = Dustify(Color.FromRgb(light.r, light.g, light.b), amount: 0.35); // brightest grains

            // Subtle pattern rotation (pattern moves, geometry doesn't)
            double rot = (t * 0.05) + HashNoise.Hash01(pSeed, 888, 111) * (Math.PI * 2.0);
            double cr = Math.Cos(rot);
            double sr = Math.Sin(rot);

            // Deterministic “major gap” location (Cassini-ish) + a few smaller gaps
            // All expressed in normalized ring band space 0..1
            double gapMajor = 0.55 + 0.05 * (HashNoise.Hash01(pSeed, 901, 902) - 0.5); // ~middle
            double gapMajorW = 0.030 + 0.010 * HashNoise.Hash01(pSeed, 903, 904);

            double gap2 = 0.18 + 0.04 * (HashNoise.Hash01(pSeed, 905, 906) - 0.5);
            double gap2W = 0.018 + 0.008 * HashNoise.Hash01(pSeed, 907, 908);

            double gap3 = 0.82 + 0.04 * (HashNoise.Hash01(pSeed, 909, 910) - 0.5);
            double gap3W = 0.020 + 0.010 * HashNoise.Hash01(pSeed, 911, 912);

            for (int y = -box; y <= box; y++)
            {
                for (int x = -box; x <= box; x++)
                {
                    // Rotate ring plane
                    double rx = x * ca + y * sa;
                    double ry = -x * sa + y * ca;

                    ry *= tilt;

                    double d = Math.Sqrt(rx * rx + ry * ry);
                    if (d < inner || d > outer) continue;

                    // normalized radius across ring span
                    double band = (d - inner) * invSpan; // 0..1
                    band = MathUtil.Clamp(band, 0.0, 1.0);

                    // base density profile: stronger inner rings, dustier outer rings
                    double dens = 1.0 - 0.55 * band; // fade outward
                    dens = MathUtil.Clamp(dens, 0.0, 1.0);

                    // pattern lookup uses rotated coords
                    double sx = rx * cr - ry * sr;
                    double sy = rx * sr + ry * cr;

                    // A few frequencies layered (keeps “photographic” feeling)
                    double f1 = Math.Abs(Math.Sin((band * 18.0 + 0.7 * sx * 0.10) * Math.PI));
                    double f2 = Math.Abs(Math.Sin((band * 41.0 + 0.9 * sy * 0.08) * Math.PI));
                    double f3 = HashNoise.FBm(pSeed + 2020, sx * 0.06, sy * 0.06, 2);

                    dens *= (0.65 + 0.25 * f1 + 0.18 * f2);
                    dens *= (0.82 + 0.20 * (f3 - 0.5));

                    // major & minor gaps (soft, not perfectly empty)
                    double gMajor = Math.Abs(band - gapMajor);
                    if (gMajor < gapMajorW) dens *= 0.12 + 0.35 * (gMajor / gapMajorW);

                    double g2 = Math.Abs(band - gap2);
                    if (g2 < gap2W) dens *= 0.25 + 0.55 * (g2 / gap2W);

                    double g3 = Math.Abs(band - gap3);
                    if (g3 < gap3W) dens *= 0.25 + 0.55 * (g3 / gap3W);

                    dens = MathUtil.Clamp(dens, 0.0, 1.0);

                    // particulate “holes”: rings are not solid
                    int nnx = (int)Math.Round(sx * 2.0);
                    int nny = (int)Math.Round(sy * 2.0);
                    double rnd = HashNoise.Hash01(pSeed, nnx + 1000, nny + 2000);

                    double hole = 0.12 + 0.40 * band;
                    double keepProb = MathUtil.Clamp(dens * (1.0 - hole), 0.0, 1.0);

                    // faint dust floor so gaps don't look like hard cutouts
                    double dustFloor = 0.04 + 0.05 * (1.0 - band);

                    if (rnd > (keepProb + dustFloor))
                        continue;

                    // lighting: brighter on lit side
                    double dot = (x * lx + y * ly) * invOuter; // -1..1 approx
                    double ringLight = MathUtil.Clamp(0.55 + 0.40 * dot, 0.15, 1.0);

                    int px = cx + x;
                    int py = cy + y;

                    // IMPORTANT: do NOT shadow rings themselves using occluders.
                    // This avoids ring->planet / eclipse artifacts from rings inheriting shadowing.
                    // Planet->ring shadow still happens because the planet is an occluder in the ring pattern itself.
                    double b = (0.10 + 0.90 * dens) * ringLight;
                    b = MathUtil.Clamp(b, 0.0, 1.0);

                    // palette pick from density (denser = “brighter/whiter” grains)
                    Color fg = (dens < 0.33) ? c2 : (dens < 0.66 ? c1 : c0);
                    fg = ShadeColorForLight(fg, MathUtil.Clamp(0.35 + 0.65 * ringLight, 0.0, 1.0), stronger: true);

                    // glyph selection: airy ASCII for rings
                    char ch = (b < 0.18) ? '.' :
                              (b < 0.45) ? '-' :
                              (b < 0.72) ? '=' : '#';

                    r.Set(px, py, ch, fg, (Color)AnsiColor.Black, z);
                }
            }
        }

        private static void ComputeLightDir(int cx, int cy, int sunX, int sunY, out double lx, out double ly)
        {
            // Fallback direction (used when sun is too close to the body on-screen)
            const double fx = -0.65;
            const double fy = -0.35;

            double dx = sunX - cx;
            double dy = sunY - cy;
            double len = Math.Sqrt(dx * dx + dy * dy);

            if (len < 1e-9)
            {
                lx = fx; ly = fy;
                return;
            }

            // True normalized direction
            double tx = dx / len;
            double ty = dy / len;

            // Blend range in SCREEN pixels: below this, rounding jitter causes popping.
            // 3-6 is a good range for ASCII; use 4 as a default.
            const double blendMax = 4.0;

            double w = MathUtil.Clamp(len / blendMax, 0.0, 1.0);
            // smoothstep
            w = w * w * (3.0 - 2.0 * w);

            // Blend fallback -> true direction
            lx = fx + (tx - fx) * w;
            ly = fy + (ty - fy) * w;

            // Renormalize blended vector
            double n = Math.Sqrt(lx * lx + ly * ly);
            if (n > 1e-9) { lx /= n; ly /= n; }
        }

        // Keeps LOD stable when radChars jitters around thresholds.
        // hysteresisChars = 2 means we need to cross threshold by 2 chars before switching back.
        private static int ApplyLodHysteresis(
            int radChars,
            int lastStep,
            int th2,
            int th3,
            int hysteresisChars)
        {
            // sanitize
            if (lastStep != 1 && lastStep != 2 && lastStep != 3) lastStep = 1;
            if (hysteresisChars < 0) hysteresisChars = 0;

            if (lastStep == 1)
            {
                // Upgrade to 2 only when clearly above th2
                if (radChars >= th2 + hysteresisChars) return 2;
                return 1;
            }

            if (lastStep == 2)
            {
                // Upgrade to 3 only when clearly above th3
                if (radChars >= th3 + hysteresisChars) return 3;

                // Drop to 1 only when clearly below th2
                if (radChars <= th2 - hysteresisChars) return 1;

                return 2;
            }

            // lastStep == 3
            {
                // Drop to 2 only when clearly below th3
                if (radChars <= th3 - hysteresisChars) return 2;
                return 3;
            }
        }


        // -------------------------
        // Atmosphere halo (outside-limb pass)
        // -------------------------
        private static void DrawAtmosphereHalo(
            ConsoleRenderer r,
            int cx, int cy,
            int radChars,
            PlanetTexture tex,
            double z,
            double lx, double ly,
            int sunX, int sunY,
            ReadOnlySpan<Occluder> occluders)
        {
            // If disabled by tuning, skip
            if (AtmosphereWidth <= 0.00001 || AtmosphereMax <= 0.00001) return;

            const double PixelAspectY = 2.0;

            // Bounding box: planet + halo thickness
            int pad = (int)Math.Ceiling(radChars * AtmosphereWidth) + 2;
            int box = radChars + pad;

            double outerNorm = 1.0 + AtmosphereWidth;
            double outer2 = outerNorm * outerNorm;

            Color tint = AtmosphereTint(tex);

            for (int y = -box; y <= box; y++)
            {
                for (int x = -box; x <= box; x++)
                {
                    double nx = x / (double)radChars;
                    double ny = y / (double)radChars;
                    double nyA = ny * PixelAspectY;

                    double rrA = nx * nx + nyA * nyA;

                    // Only outside the limb, but within halo radius.
                    if (rrA <= 1.0 || rrA > outer2) continue;

                    double dist = Math.Sqrt(rrA);
                    double d = dist - 1.0; // 0..AtmosphereWidth

                    double a = 1.0 - (d / Math.Max(1e-6, AtmosphereWidth));
                    a = MathUtil.Clamp(a, 0.0, 1.0);

                    // soften curve (avoid hard band)
                    a = a * a * (3.0 - 2.0 * a);

                    // day-side bias (simple scattering feel)
                    double invLen = 1.0 / Math.Sqrt(nx * nx + nyA * nyA + 1e-9);
                    double nxN = nx * invLen;
                    double nyN = nyA * invLen;
                    double day = MathUtil.Clamp(nxN * lx + nyN * ly + 0.20, 0.0, 1.0);

                    double intensity = AtmosphereMax * a * (0.35 + 0.65 * day);

                    int px = cx + x;
                    int py = cy + y;

                    // Optionally allow eclipses to dim the halo slightly
                    double shadowMul = ShadowFactor(px, py, lx, ly, occluders,
                        selfCx: cx, selfCy: cy, selfR: radChars,
                        sunX: sunX, sunY: sunY);

                    intensity *= (0.70 + 0.30 * shadowMul);

                    if (intensity <= 0.02) continue;

                    // pick a faint glyph (keep airy)
                    char ch =
                        (intensity < 0.08) ? '.' :
                        (intensity < 0.16) ? ':' :
                        (intensity < 0.26) ? '+' : '*';

                    // shade tint by intensity (RGB path)
                    Color fg = ShadeColorForLight(tint, MathUtil.Clamp(intensity / AtmosphereMax, 0.0, 1.0), stronger: false);

                    r.Set(px, py, ch, fg, (Color)AnsiColor.Black, z);
                }
            }
        }

        // Backwards-compatible sampler (moons call this)
        private static void SamplePlanet(
            int seed,
            PlanetTexture tex,
            double nx, double ny, double nz,
            double spinTurns,
            out Color fg,
            out char glyph)
        {
            SamplePlanetEx(seed, tex, nx, ny, nz, spinTurns, out fg, out glyph, out _, out _);
        }

        internal static void SamplePlanetSurface(
            int seed,
            PlanetTexture tex,
            double nx, double ny, double nz,
            double spinTurns,
            out Color fg,
            out double emissive01,
            out Color emissiveColor)
        {
            SamplePlanetEx(seed, tex, nx, ny, nz, spinTurns, out fg, out _, out emissive01, out emissiveColor);
        }

        private static void SamplePlanetEx(
            int seed,
            PlanetTexture tex,
            double nx, double ny, double nz,
            double spinTurns,
            out Color fg,
            out char glyph,
            out double emissive01,
            out Color emissiveColor)
        {
            emissive01 = 0.0;
            emissiveColor = Color.FromRgb(255, 205, 120); // warm city lights

            double lon = Math.Atan2(nx, nz) + (spinTurns * Math.PI * 2.0);
            double u = lon / (Math.PI * 2.0) + 0.5;
            u = Frac(u);

            double v = Math.Asin(MathUtil.Clamp(ny, -1.0, 1.0)) / Math.PI + 0.5;

            double n1 = SeamlessFbm(seed, u, v, 3.0, 4);
            double n2 = SeamlessFbm(seed ^ 0x51f2, u, v, 6.0, 3);

            double lat = Math.Abs(ny);

            PlanetTextures.PaletteId pal = default;
            bool useVariantPalette = (tex != PlanetTexture.EarthLike && tex != PlanetTexture.Continents);
            if (useVariantPalette)
                pal = PlanetTextures.PickPaletteVariant(seed, tex);

            switch (tex)
            {
                case PlanetTexture.Rocky:
                    {
                        PlanetTextures.GetPalette(tex, pal, out var dark, out var mid, out var light);

                        double tone01 = MathUtil.Clamp(0.30 + 0.55 * n1, 0.0, 1.0);
                        fg = Palette3(dark, mid, light, tone01);

                        glyph = (n2 < 0.5) ? '.' : ':';
                        return;
                    }

                case PlanetTexture.Cratered:
                    {
                        PlanetTextures.GetPalette(tex, pal, out var dark, out var mid, out var light);

                        bool rim = n2 < 0.22;
                        double tone01 = rim ? 0.85 : MathUtil.Clamp(0.25 + 0.55 * n1, 0.0, 1.0);

                        fg = Palette3(dark, mid, light, tone01);
                        glyph = (n2 < 0.28) ? 'o' : (n2 < 0.65 ? '.' : ':');
                        return;
                    }

                case PlanetTexture.Metallic:
                    {
                        PlanetTextures.GetPalette(tex, pal, out var dark, out var mid, out var light);

                        bool sheen = n2 > 0.62;
                        double tone01 = sheen ? 0.90 : MathUtil.Clamp(0.35 + 0.50 * n1, 0.0, 1.0);

                        fg = Palette3(dark, mid, light, tone01);
                        glyph = (n2 < 0.4) ? '*' : '+';
                        return;
                    }

                case PlanetTexture.Barren:
                    {
                        PlanetTextures.GetPalette(tex, pal, out var dark, out var mid, out var light);

                        double tone01 = MathUtil.Clamp(0.20 + 0.50 * n1, 0.0, 1.0);
                        fg = Palette3(dark, mid, light, tone01);

                        glyph = (n2 < 0.5) ? '.' : '\'';
                        return;
                    }

                case PlanetTexture.Desert:
                    {
                        PlanetTextures.GetPalette(tex, pal, out var dark, out var mid, out var light);

                        bool dune = n2 > 0.58;

                        double tone01 = dune
                            ? MathUtil.Clamp(0.55 + 0.40 * n1, 0.0, 1.0)
                            : MathUtil.Clamp(0.40 + 0.35 * n1, 0.0, 1.0);

                        fg = Palette3(dark, mid, light, tone01);
                        glyph = (n2 < 0.25) ? '.' : ':';
                        return;
                    }

                case PlanetTexture.Jungle:
                    {
                        PlanetTextures.GetPalette(tex, pal, out var dark, out var mid, out var light);

                        bool clearing = n2 > 0.70;

                        double tone01 = clearing
                            ? MathUtil.Clamp(0.55 + 0.35 * n1, 0.0, 1.0)
                            : MathUtil.Clamp(0.25 + 0.45 * n1, 0.0, 1.0);

                        fg = Palette3(dark, mid, light, tone01);
                        glyph = (n2 < 0.30) ? '.' : ':';
                        return;
                    }

                case PlanetTexture.Oceanic:
                    {
                        PlanetTextures.GetPalette(tex, pal, out var dark, out var mid, out var light);

                        bool shallow = n1 > 0.58;

                        double tone01 = shallow
                            ? MathUtil.Clamp(0.45 + 0.35 * n2, 0.0, 1.0)
                            : MathUtil.Clamp(0.20 + 0.25 * n2, 0.0, 1.0);

                        fg = Palette3(dark, mid, light, tone01);
                        glyph = (n2 < 0.30) ? '~' : '-';
                        return;
                    }

                case PlanetTexture.Continents:
                case PlanetTexture.EarthLike:
                    {
                        double land = SeamlessFbm(seed + 333, u, v, 2.6, 5);
                        double detail = SeamlessFbm(seed + 777, u, v, 8.0, 3);

                        bool isLand = land > 0.52;
                        bool isMountain = isLand && (detail > 0.64);
                        bool isShallow = !isLand && (land > 0.50);

                        bool ice = lat > 0.82 && (HashNoise.FBm(seed + 999, u * 6.0, v * 6.0, 2) > 0.35);

                        if (ice)
                        {
                            fg = Color.FromRgb(235, 245, 255);
                            glyph = (detail < 0.5) ? '.' : '*';
                        }
                        else if (!isLand)
                        {
                            fg = isShallow
                                ? Color.FromRgb(40, 160, 170)
                                : Color.FromRgb(20, 70, 160);

                            glyph = (detail < 0.5) ? '~' : '-';
                        }
                        else if (isMountain)
                        {
                            fg = (detail > 0.78)
                                ? Color.FromRgb(220, 220, 230)
                                : Color.FromRgb(140, 140, 150);

                            glyph = (detail > 0.75) ? '^' : 'n';
                        }
                        else
                        {
                            bool deserty = detail > 0.60;
                            fg = deserty
                                ? Color.FromRgb(205, 190, 110)
                                : Color.FromRgb(40, 160, 80);

                            glyph = (detail < 0.35) ? '.' : ':';
                        }

                        // city lights emissive (EarthLike only)
                        if (tex == PlanetTexture.EarthLike && isLand && !ice && !isMountain)
                        {
                            double city = SeamlessFbm(seed + 4242, u, v, 12.0, 2); // 0..1-ish
                            double pop = MathUtil.Clamp((city - 0.62) / 0.38, 0.0, 1.0);

                            double damp = 1.0;
                            if (glyph == ':') damp = 0.55; // desert-ish
                            emissive01 = pop * pop * 0.95 * damp;

                            double tint = HashNoise.Hash01(seed + 88, (int)(u * 1000), (int)(v * 1000));
                            emissiveColor = (tint > 0.75)
                                ? Color.FromRgb(255, 225, 170)
                                : Color.FromRgb(255, 200, 110);
                        }

                        return;
                    }

                case PlanetTexture.IceWorld:
                    {
                        PlanetTextures.GetPalette(tex, pal, out var dark, out var mid, out var light);

                        bool highlight = n2 > 0.62;
                        double tone01 = highlight ? 0.90 : MathUtil.Clamp(0.55 + 0.30 * n1, 0.0, 1.0);

                        fg = Palette3(dark, mid, light, tone01);
                        glyph = (n2 < 0.5) ? '.' : '*';
                        return;
                    }

                case PlanetTexture.IceCracked:
                    {
                        PlanetTextures.GetPalette(tex, pal, out var dark, out var mid, out var light);

                        bool crack = n2 > 0.62;

                        double tone01 = crack
                            ? MathUtil.Clamp(0.35 + 0.35 * n1, 0.0, 1.0)
                            : MathUtil.Clamp(0.65 + 0.25 * n1, 0.0, 1.0);

                        fg = Palette3(dark, mid, light, tone01);
                        glyph = (n2 < 0.45) ? '.' : (n2 < 0.8 ? '/' : '\\');
                        return;
                    }

                case PlanetTexture.Lava:
                    {
                        PlanetTextures.GetPalette(tex, pal, out var dark, out var mid, out var light);

                        bool glow = n1 > 0.58;

                        double tone01 = glow
                            ? MathUtil.Clamp(0.70 + 0.30 * n2, 0.0, 1.0)
                            : MathUtil.Clamp(0.20 + 0.35 * n2, 0.0, 1.0);

                        fg = Palette3(dark, mid, light, tone01);
                        glyph = (n2 < 0.4) ? '~' : '*';
                        return;
                    }

                case PlanetTexture.Toxic:
                    {
                        PlanetTextures.GetPalette(tex, pal, out var dark, out var mid, out var light);

                        bool haze = n2 > 0.66;

                        double tone01 = haze
                            ? MathUtil.Clamp(0.55 + 0.35 * n1, 0.0, 1.0)
                            : MathUtil.Clamp(0.35 + 0.45 * n1, 0.0, 1.0);

                        fg = Palette3(dark, mid, light, tone01);
                        glyph = (n2 < 0.5) ? ':' : ';';
                        return;
                    }

                case PlanetTexture.GasBands:
                case PlanetTexture.GasSwirl:
                case PlanetTexture.GasStorm:
                    {
                        PlanetTextures.GetPalette(tex, pal, out var dark, out var mid, out var light);

                        double tone01 = MathUtil.Clamp(0.25 + 0.70 * n1, 0.0, 1.0);
                        fg = Palette3(dark, mid, light, tone01);

                        if (tex == PlanetTexture.GasBands)
                        {
                            double bands = 0.5 + 0.5 * Math.Sin((v * 14.0 + 0.8 * (n2 - 0.5)) * Math.PI * 2.0);
                            glyph = (bands < 0.42) ? '-' : (bands < 0.68) ? '=' : '~';
                        }
                        else if (tex == PlanetTexture.GasSwirl)
                            glyph = (n2 < 0.5) ? ')' : '(';
                        else
                            glyph = (n2 < 0.2) ? '@' : (n2 < 0.6 ? 'o' : '~');

                        return;
                    }

                default:
                    fg = (Color)AnsiColor.White;
                    glyph = '.';
                    return;
            }
        }

        private static Color LerpRgb((byte r, byte g, byte b) a, (byte r, byte g, byte b) b, double t)
        {
            t = MathUtil.Clamp(t, 0.0, 1.0);
            int r = (int)Math.Round(a.r + (b.r - a.r) * t);
            int g = (int)Math.Round(a.g + (b.g - a.g) * t);
            int bl = (int)Math.Round(a.b + (b.b - a.b) * t);
            return Color.FromRgb((byte)ClampToByte(r), (byte)ClampToByte(g), (byte)ClampToByte(bl));
        }

        /// <summary>
        /// Maps a 0..1 tone into a 3-point palette (dark/mid/light).
        /// </summary>
        private static Color Palette3((byte r, byte g, byte b) dark, (byte r, byte g, byte b) mid, (byte r, byte g, byte b) light, double tone01)
        {
            tone01 = MathUtil.Clamp(tone01, 0.0, 1.0);

            if (tone01 <= 0.5)
            {
                double t = tone01 / 0.5;
                return LerpRgb(dark, mid, t);
            }
            else
            {
                double t = (tone01 - 0.5) / 0.5;
                return LerpRgb(mid, light, t);
            }
        }

        private static double Frac(double x) => x - Math.Floor(x);

        private static char PlanetGlyph(char texGlyph, int localX, int localY, int ox, int oy, double light01)
        {
            if (PlanetColorOnlyShading)
                return PlanetSolidGlyph;

            // NEW: if we're forcing ramp shading (e.g., RampBlocks), always shade from the ramp
            if (ForceRampShading)
            {
                string ramp = string.IsNullOrEmpty(Ramp) ? " .:-=+*#" : Ramp;
                return RampShade(ramp, localX + ox, localY + oy, light01);
            }

            // Existing behavior (best for RampLong / RampShort non-block ramps)
            return ShadeGlyphLocalAt(localX, localY, ox, oy, texGlyph, light01);
        }

        // Sun glyph selection that respects the SAME 4-mode toggle.
        // Sun should always use ramp shading when not in solid mode.
        internal static char SunGlyph(int localX, int localY, int ox, int oy, double light01)
        {
            if (PlanetColorOnlyShading)
                return PlanetSolidGlyph;

            string ramp = string.IsNullOrEmpty(Ramp) ? " .:-=+*#" : Ramp;
            return RampShade(ramp, localX + ox, localY + oy, light01);
        }


        // -------------------------
        // Shading helpers
        // -------------------------
        private static Color ShadeColorForLight(Color baseColor, double light01, bool stronger)
        {
            light01 = MathUtil.Clamp(light01, 0.0, 1.0);

            if (baseColor.Mode == ColorMode.Rgb24)
                return ShadeRgb(baseColor, light01, stronger);

            int delta =
                (light01 < 0.10) ? -7 :
                (light01 < 0.22) ? -5 :
                (light01 < 0.38) ? -3 :
                (light01 < 0.58) ? 0 :
                (light01 < 0.78) ? 1 :
                (light01 < 0.90) ? 2 :
                                   3;

            return ShadeColorByDelta(baseColor, delta);
        }

        private static Color ShadeColorByDelta(Color baseColor, int delta256)
        {
            if (baseColor.Mode == ColorMode.Ansi16)
            {
                var ansi = (AnsiColor)baseColor.Value;
                return ColorUtils.Shade(ansi, delta256);
            }

            if (baseColor.Mode == ColorMode.Ansi256)
            {
                byte idx = (byte)baseColor.Value;
                return ColorUtils.Shade256(idx, delta256);
            }

            return baseColor;
        }

        private static Color ShadeRgb(Color rgb, double light01, bool stronger)
        {
            int v = rgb.Value;
            int r = (v >> 16) & 255;
            int g = (v >> 8) & 255;
            int b = v & 255;

            // Existing:
            // double ambient = stronger ? 0.06 : 0.20;

            // NEW: darker nights only in SolidColorOnly mode
            double ambient;
            if (PlanetColorOnlyShading)
                ambient = stronger ? 0.015 : 0.12;   // tweak: lower = darker backside
            else
                ambient = stronger ? 0.06 : 0.20;    // unchanged for other modes

            double k = ambient + (1.0 - ambient) * light01;

            int rr = ClampToByte((int)Math.Round(r * k));
            int gg = ClampToByte((int)Math.Round(g * k));
            int bb2 = ClampToByte((int)Math.Round(b * k));

            return Color.FromRgb((byte)rr, (byte)gg, (byte)bb2);
        }

        private static Color AddRimLift(Color c, double lift01)
        {
            if (lift01 <= 0.00001) return c;
            if (c.Mode != ColorMode.Rgb24) return c;

            int v = c.Value;
            int r = (v >> 16) & 255;
            int g = (v >> 8) & 255;
            int b = v & 255;

            int add = ClampToByte((int)Math.Round(255.0 * lift01));
            int rr = ClampToByte(r + add);
            int gg = ClampToByte(g + add);
            int bb = ClampToByte(b + (int)Math.Round(add * 0.7));

            return Color.FromRgb((byte)rr, (byte)gg, (byte)bb);
        }

        private static int ClampToByte(int v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return v;
        }

        // Desaturate + slightly brighten toward a dusty/icy ring material look.
        // amount: 0..1 (higher = more dust-like / less saturated)
        private static Color Dustify(Color c, double amount)
        {
            amount = MathUtil.Clamp(amount, 0.0, 1.0);
            if (c.Mode != ColorMode.Rgb24) return c;

            int v = c.Value;
            double r = (v >> 16) & 255;
            double g = (v >> 8) & 255;
            double b = v & 255;

            double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;

            r = r + (lum - r) * amount;
            g = g + (lum - g) * amount;
            b = b + (lum - b) * amount;

            double lift = 18.0 + 22.0 * amount;
            r = Math.Min(255.0, r + lift);
            g = Math.Min(255.0, g + lift);
            b = Math.Min(255.0, b + lift * 0.85);

            return Color.FromRgb(
                (byte)ClampToByte((int)Math.Round(r)),
                (byte)ClampToByte((int)Math.Round(g)),
                (byte)ClampToByte((int)Math.Round(b))
            );
        }

        // 4x4 Bayer ordered dithering (0..15)
        private static readonly int[] _bayer4 =
        {
            0,  8,  2, 10,
            12,  4, 14,  6,
            3, 11,  1,  9,
            15,  7, 13,  5
        };

        private static bool IsBlockRamp(string ramp)
        {
            if (string.IsNullOrEmpty(ramp)) return false;
            return ramp.IndexOf('█') >= 0 || ramp.IndexOf('▓') >= 0 ||
                   ramp.IndexOf('▒') >= 0 || ramp.IndexOf('░') >= 0;
        }

        private static char RampShade(string ramp, int sx, int sy, double light01)
        {
            if (string.IsNullOrEmpty(ramp)) return '#';
            light01 = MathUtil.Clamp(light01, 0.0, 1.0);

            // --- Fix: highlight lock (prevents isolated '▓' inside bright '█' areas) ---
            if (IsBlockRamp(ramp) && light01 >= 0.92)
                return ramp[ramp.Length - 1];

            // Non-block ramps: crisp indexing
            if (!IsBlockRamp(ramp))
            {
                int idx = (int)Math.Round(light01 * (ramp.Length - 1));
                if (idx < 0) idx = 0;
                if (idx >= ramp.Length) idx = ramp.Length - 1;
                return ramp[idx];
            }

            // Block ramp: dither between levels
            double t = light01 * (ramp.Length - 1);
            int baseIdx = (int)Math.Floor(t);
            double frac = t - baseIdx;

            if (baseIdx < 0) baseIdx = 0;
            if (baseIdx >= ramp.Length - 1) return ramp[ramp.Length - 1];

            double thresh = _bayer4[(sx & 3) + ((sy & 3) << 2)] / 16.0;
            if (frac > thresh) baseIdx++;

            return ramp[baseIdx];
        }

        // Screen-aware glyph shading:
        // - Block ramps: always ramp shading with dithering
        // - Non-block ramps: preserve the texture glyph except at extremes (so shading still reads)
        private static char ShadeGlyphAt(int sx, int sy, char baseGlyph, double light01)
        {
            string ramp = string.IsNullOrEmpty(Ramp) ? " .:-=+*#" : Ramp;
            light01 = MathUtil.Clamp(light01, 0.0, 1.0);

            bool blockRamp = IsBlockRamp(ramp);
            if (blockRamp)
                return RampShade(ramp, sx, sy, light01);

            // Non-block: keep texture glyph, but use ramp at extreme dark/bright so it still “shades”.
            if (baseGlyph == '\0' || baseGlyph == ' ') baseGlyph = '.';

            if (light01 <= 0.10) return ramp[0];
          //if (light01 >= 0.95) return ramp[ramp.Length - 1]; // was 0.90
            return baseGlyph;
        }

        // Planet-local glyph shading:
        // - Block ramps: ramp shading with dithering in LOCAL coords (stable under zoom/pan)
        // - Non-block ramps: preserve texture glyph except at extremes
        private static char ShadeGlyphLocalAt(int localX, int localY, int ditherOx, int ditherOy, char baseGlyph, double light01)
        {
            string ramp = string.IsNullOrEmpty(Ramp) ? " .:-=+*#" : Ramp;
            light01 = MathUtil.Clamp(light01, 0.0, 1.0);

            bool blockRamp = IsBlockRamp(ramp);
            if (blockRamp)
            {
                // Dither in LOCAL object space (NOT screen space)
                return RampShade(ramp, localX + ditherOx, localY + ditherOy, light01);
            }

            // Non-block: keep texture glyph, but use ramp at extreme dark/bright so it still “shades”.
            if (baseGlyph == '\0' || baseGlyph == ' ') baseGlyph = '.';

            if (light01 <= 0.10) return ramp[0];
          //if (light01 >= 0.95) return ramp[ramp.Length - 1]; // was 0.90
            return baseGlyph;
        }


        // Kept for any legacy callers inside this file (now just forwards to ShadeGlyphAt).
        private static char ShadeGlyph(char baseGlyph, double light01, int radChars)
        {
            // radChars intentionally unused; kept to avoid breaking internal call patterns.
            return ShadeGlyphAt(0, 0, baseGlyph, light01);
        }

        // -------------------------
        // Shadows (occlusion) helpers
        // -------------------------
        private static double ShadowFactor(
            int px, int py,
            double lx, double ly,
            ReadOnlySpan<Occluder> occ,
            int selfCx, int selfCy, int selfR,
            int sunX, int sunY)
        {
            if (occ.Length == 0) return 1.0;

            // Use the faster analytic penumbra model for big bodies; raymarch for small ones.
            if (selfR >= ShadowSkipThreshold)
            {
                return ShadowFactorFromOccluders(px, py, lx, ly, occ, selfCx, selfCy, selfR, sunX, sunY);
            }

            return ShadowFactorRaymarch(px, py, lx, ly, occ, selfCx, selfCy, selfR);
        }

        private static double ShadowFactorFromOccluders(
            int px, int py,
            double lx, double ly,
            ReadOnlySpan<Occluder> occ,
            int selfCx, int selfCy, int selfR,
            int sunX, int sunY)
        {
            if (occ.Length == 0) return 1.0;

            double Lx = lx, Ly = ly;
            double n = Math.Sqrt(Lx * Lx + Ly * Ly);
            if (n < 1e-9) return 1.0;
            Lx /= n; Ly /= n;

            double sunDx = sunX - px;
            double sunDy = sunY - py;
            double tSun = sunDx * Lx + sunDy * Ly;
            if (tSun <= 1.0) return 1.0;

            double Rs = Math.Max(1.0, SunRadiusChars);
            const double MinLit = 0.15;

            double best = 1.0;

            for (int i = 0; i < occ.Length; i++)
            {
                int ocx = occ[i].X;
                int ocy = occ[i].Y;
                int R = occ[i].R;

                if (ocx == selfCx && ocy == selfCy) continue;

                double dx = ocx - px;
                double dy = ocy - py;

                double t = dx * Lx + dy * Ly;
                if (t <= 0.5 || t >= tSun - 0.5) continue;

                double pxr = dx - t * Lx;
                double pyr = dy - t * Ly;
                double dist = Math.Sqrt(pxr * pxr + pyr * pyr);

                double dsx = sunX - ocx;
                double dsy = sunY - ocy;
                double dSun = Math.Sqrt(dsx * dsx + dsy * dsy);
                if (dSun < 1.0) dSun = 1.0;

                double penW = Rs * (t / dSun);

                double umbra = Math.Max(0.0, R - penW);
                double penOuter = R + penW;

                if (dist >= penOuter) continue;

                double lit;
                if (dist <= umbra)
                {
                    lit = MinLit;
                }
                else
                {
                    double k = (dist - umbra) / Math.Max(1e-6, (penOuter - umbra));
                    double s = k * k * (3.0 - 2.0 * k);
                    lit = MinLit + (1.0 - MinLit) * s;
                }

                if (lit < best) best = lit;
                if (best <= MinLit + 1e-4) return MinLit;
            }

            return best;
        }

        // Old raymarch fallback (kept)
        private static double ShadowFactorRaymarch(
            int px, int py,
            double lx, double ly,
            ReadOnlySpan<Occluder> occ,
            int selfCx, int selfCy, int selfR)
        {
            const double Step = 1.0;

            int maxSteps = selfR * 6 + 40;
            if (maxSteps < 60) maxSteps = 60;
            if (maxSteps > 240) maxSteps = 240;

            double x = px;
            double y = py;

            for (int step = 0; step < maxSteps; step++)
            {
                x += lx * Step;
                y += ly * Step;

                int ix = (int)Math.Round(x);
                int iy = (int)Math.Round(y);

                int dxs = ix - selfCx;
                int dys = iy - selfCy;
                if (dxs * dxs + dys * dys <= selfR * selfR)
                    continue;

                for (int i = 0; i < occ.Length; i++)
                {
                    int dx = ix - occ[i].X;
                    int dy = iy - occ[i].Y;

                    if (dx * dx + dy * dy <= occ[i].R * occ[i].R)
                        return 0.15;
                }
            }

            return 1.0;
        }

        // -------------------------
        // Seamless noise
        // -------------------------
        private static double SeamlessFbm(int seed, double u01, double v01, double scale, int octaves)
        {
            double ang = u01 * Math.PI * 2.0;
            double cx = Math.Cos(ang) * scale;
            double sx = Math.Sin(ang) * scale;

            double z = (v01 - 0.5) * scale;

            double a = HashNoise.FBm(seed, cx, z, octaves);

            int seed2 = unchecked(seed ^ (int)0x9E3779B9);
            double b = HashNoise.FBm(seed2, sx, z, octaves);

            return 0.5 * (a + b);
        }

        private static double SmoothStep(double a, double b, double x)
        {
            if (x <= a) return 0.0;
            if (x >= b) return 1.0;
            double t = (x - a) / (b - a);
            return t * t * (3.0 - 2.0 * t);
        }
    }
}
