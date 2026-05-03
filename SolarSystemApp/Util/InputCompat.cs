using System;
using System.Collections.Generic;
using System.Reflection;
using AsciiEngine;

namespace SolarSystemApp.Util
{
    internal static class InputCompat
    {
        private static readonly Dictionary<Type, MethodInfo> _isDownCache = new();
        private static readonly object _lock = new();

        public static int Axis(EngineContext ctx, ConsoleKey neg, ConsoleKey pos, ConsoleKey negAlt, ConsoleKey posAlt)
        {
            bool negDown = IsDownOrPressed(ctx, neg) || IsDownOrPressed(ctx, negAlt);
            bool posDown = IsDownOrPressed(ctx, pos) || IsDownOrPressed(ctx, posAlt);

            if (negDown == posDown) return 0;
            return negDown ? -1 : +1;
        }

        private static bool IsDownOrPressed(EngineContext ctx, ConsoleKey key)
        {
            var input = ctx.Input;
            if (input == null) return false;

            try
            {
                MethodInfo mi = GetIsDownMethod(input.GetType());
                if (mi != null)
                {
                    object r = mi.Invoke(input, new object[] { key });
                    if (r is bool b) return b;
                }
            }
            catch
            {
            }

            return input.WasPressed(key);
        }

        private static MethodInfo GetIsDownMethod(Type t)
        {
            lock (_lock)
            {
                if (_isDownCache.TryGetValue(t, out var mi))
                    return mi;

                mi = t.GetMethod("IsDown", new[] { typeof(ConsoleKey) });
                _isDownCache[t] = mi;
                return mi;
            }
        }
    }
}
