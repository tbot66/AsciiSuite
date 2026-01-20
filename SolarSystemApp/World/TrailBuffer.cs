using System;
using System.Collections.Generic;

namespace SolarSystemApp.World
{
    public sealed class TrailBuffer
    {
        private readonly int _max;
        private readonly Queue<(double x, double y)> _pts;

        public TrailBuffer(int maxPoints)
        {
            _max = (maxPoints < 0) ? 0 : maxPoints;
            _pts = new Queue<(double x, double y)>(_max);
        }

        public void Clear() => _pts.Clear();

        public void Add(double x, double y)
        {
            if (_max <= 0) return;

            if (_pts.Count >= _max)
                _pts.Dequeue();

            _pts.Enqueue((x, y));
        }

        /// <summary>
        /// Enumerates points newest -> oldest.
        /// age = 0 is newest, age = count-1 is oldest.
        /// </summary>
        public void ForEachNewest(Action<(double X, double Y), int, int> visitor)
        {
            if (visitor == null) return;

            // Queue enumerates oldest -> newest; copy so we can walk backwards.
            var arr = _pts.ToArray(); // arr[0]=oldest, arr[^1]=newest
            int count = arr.Length;

            for (int age = 0; age < count; age++)
            {
                int idx = count - 1 - age;
                var p = arr[idx];
                visitor((p.x, p.y), age, count); // tuple elements named X/Y for pt.X pt.Y
            }
        }

        /// <summary>
        /// Oldest -> newest enumeration (original order).
        /// </summary>
        public IEnumerable<(double x, double y)> Points => _pts;
    }
}
