using System;
using System.Collections.Generic;
using SolarSystemApp.Util;

namespace SolarSystemApp.Rendering
{
    internal sealed class EventLog
    {
        private readonly string[] _buf;
        private int _head = 0;
        private int _count = 0;

        public EventLog(int capacity = 12)
        {
            capacity = MathUtil.ClampInt(capacity, 4, 64);
            _buf = new string[capacity];
        }

        public void Add(double simTime, string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return;

            string line = $"[{simTime,6:0.0}] {msg}";
            _buf[_head] = line;
            _head = (_head + 1) % _buf.Length;
            _count = Math.Min(_count + 1, _buf.Length);
        }

        public IEnumerable<string> GetNewestFirst(int maxLines)
        {
            maxLines = MathUtil.ClampInt(maxLines, 1, _buf.Length);
            int n = Math.Min(maxLines, _count);

            for (int i = 0; i < n; i++)
            {
                int idx = _head - 1 - i;
                if (idx < 0) idx += _buf.Length;
                yield return _buf[idx];
            }
        }
    }
}
