using System;
using System.Collections.Generic;

namespace SolarSystemApp.Rendering.Modern
{
    internal sealed class RenderQueue
    {
        public readonly List<RenderCommand> Commands = new List<RenderCommand>(128);

        public void Clear() => Commands.Clear();

        public void Add(RenderCommand command) => Commands.Add(command);

        public void Sort()
        {
            Commands.Sort(static (a, b) => a.SortKey.CompareTo(b.SortKey));
        }
    }

    internal readonly struct RenderCommand
    {
        public readonly int SortKey;
        public readonly Action Action;

        public RenderCommand(int sortKey, Action action)
        {
            SortKey = sortKey;
            Action = action;
        }
    }
}
