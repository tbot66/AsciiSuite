using System;
using System.Reflection;
using AsciiEngine;

namespace SolarSystemApp.Core
{
    public sealed class InputUtil
    {
        private MethodInfo _miIsDown;

        public void BindIsDownIfExists(EngineContext ctx)
        {
            if (ctx?.Input == null) return;
            var t = ctx.Input.GetType();
            _miIsDown = t.GetMethod("IsDown", new[] { typeof(ConsoleKey) });
        }

        public bool HasIsDown => _miIsDown != null;

        public bool IsDown(EngineContext ctx, ConsoleKey key)
        {
            if (_miIsDown == null) return false;
            try
            {
                object result = _miIsDown.Invoke(ctx.Input, new object[] { key });
                return (result is bool b) && b;
            }
            catch { return false; }
        }
    }
}
