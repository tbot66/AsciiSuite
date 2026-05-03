using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SolarSystemApp.Util;
using SolarSystemApp.World;

namespace SolarSystemApp.Rendering
{
    internal static class OccluderBuilder
    {
        private static readonly Dictionary<Type, PropertyInfo> _moonsPropCache = new();
        private static readonly Dictionary<Type, PropertyInfo> _wxPropCache = new();
        private static readonly Dictionary<Type, PropertyInfo> _wyPropCache = new();
        private static readonly Dictionary<Type, PropertyInfo> _radPropCache = new();
        private static readonly object _cacheLock = new();

        public static List<PlanetDrawer.Occluder> BuildForSystem(
            StarSystem sys,
            Func<double, int> worldToScreenX,
            Func<double, int> worldToScreenY,
            Func<double, int> worldRadiusToChars)
        {
            var occ = new List<PlanetDrawer.Occluder>(64);
            if (sys == null || sys.Planets == null) return occ;

            for (int i = 0; i < sys.Planets.Count; i++)
            {
                var p = sys.Planets[i];
                if (p == null) continue;

                int cx = worldToScreenX(p.WX);
                int cy = worldToScreenY(p.WY);
                int r = MathUtil.ClampInt(worldRadiusToChars(p.RadiusWorld), 1, 200);

                occ.Add(new PlanetDrawer.Occluder(cx, cy, r));

                TryAddMoons(occ, p, worldToScreenX, worldToScreenY, worldRadiusToChars);
            }

            return occ;
        }

        private static void TryAddMoons(
            List<PlanetDrawer.Occluder> occ,
            object planetObj,
            Func<double, int> worldToScreenX,
            Func<double, int> worldToScreenY,
            Func<double, int> worldRadiusToChars)
        {
            if (planetObj == null) return;

            PropertyInfo moonsProp = GetCachedProperty(_moonsPropCache, planetObj.GetType(), "Moons");
            if (moonsProp == null) return;

            object moonsObj = moonsProp.GetValue(planetObj);
            if (moonsObj is not IList moons) return;

            for (int i = 0; i < moons.Count; i++)
            {
                object m = moons[i];
                if (m == null) continue;

                double wx = ReadDoubleCached(m, "WX", _wxPropCache);
                double wy = ReadDoubleCached(m, "WY", _wyPropCache);
                double rr = ReadDoubleCached(m, "RadiusWorld", _radPropCache);
                if (rr <= 0.0) rr = 0.15;

                int cx = worldToScreenX(wx);
                int cy = worldToScreenY(wy);
                int r = MathUtil.ClampInt(worldRadiusToChars(rr), 1, 120);

                occ.Add(new PlanetDrawer.Occluder(cx, cy, r));
            }
        }

        private static PropertyInfo GetCachedProperty(Dictionary<Type, PropertyInfo> cache, Type t, string propName)
        {
            lock (_cacheLock)
            {
                if (cache.TryGetValue(t, out var pi))
                    return pi;

                pi = t.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                cache[t] = pi;
                return pi;
            }
        }

        private static double ReadDoubleCached(object obj, string propName, Dictionary<Type, PropertyInfo> cache)
        {
            if (obj == null) return 0.0;

            Type t = obj.GetType();
            PropertyInfo p = GetCachedProperty(cache, t, propName);
            if (p == null) return 0.0;

            object v = p.GetValue(obj);
            if (v == null) return 0.0;

            if (v is double d) return d;
            if (v is float f) return f;
            if (v is int i) return i;
            if (v is long l) return l;

            return Convert.ToDouble(v);
        }
    }
}
