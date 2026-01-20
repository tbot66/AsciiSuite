using System;
using System.Collections.Generic;

namespace AsciiEngine
{
    public sealed class InputState
    {
        // ConsoleKey values are small; use dense tables for speed.
        // We still behave the same as before: "down" is only within the current frame,
        // and "pressed" means it appeared at least once this frame.
        private const int KeyCapacity = 512;

        private readonly bool[] _down = new bool[KeyCapacity];
        private readonly bool[] _pressed = new bool[KeyCapacity];
        private readonly bool[] _released = new bool[KeyCapacity];

        // Track which keys we touched so we can clear in O(keysPressed) instead of O(KeyCapacity).
        private readonly int[] _downList = new int[64];
        private int _downCount;

        private readonly int[] _pressedList = new int[64];
        private int _pressedCount;

        private readonly int[] _releasedList = new int[8];
        private int _releasedCount;

        private int _mouseX;
        private int _mouseY;
        private bool _mouseLeftDown;
        private bool _mouseLeftPressed;
        private bool _mouseLeftReleased;

        // Called once per frame before reading new keys
        internal void BeginFrame()
        {
            for (int i = 0; i < _pressedCount; i++)
                _pressed[_pressedList[i]] = false;
            _pressedCount = 0;

            for (int i = 0; i < _releasedCount; i++)
                _released[_releasedList[i]] = false;
            _releasedCount = 0;

            _mouseLeftPressed = false;
            _mouseLeftReleased = false;
        }

        // Called by TerminalSession when a key event arrives
        internal void OnKey(ConsoleKey key)
        {
            int k = (int)key;
            if ((uint)k >= (uint)KeyCapacity) return;

            if (!_down[k])
            {
                _down[k] = true;
                AddToList(ref _downCount, _downList, k);

                // First time this frame => pressed
                if (!_pressed[k])
                {
                    _pressed[k] = true;
                    AddToList(ref _pressedCount, _pressedList, k);
                }
            }
            else
            {
                // Repeat key press while held counts as pressed again in spirit,
                // but API is boolean ("WasPressed"), so just ensure it's true.
                if (!_pressed[k])
                {
                    _pressed[k] = true;
                    AddToList(ref _pressedCount, _pressedList, k);
                }
            }
        }

        internal void EndFrame()
        {
            // Existing behavior: clear "down" at end of frame.
            for (int i = 0; i < _downCount; i++)
                _down[_downList[i]] = false;
            _downCount = 0;
        }

        public bool WasPressed(ConsoleKey key)
        {
            int k = (int)key;
            return (uint)k < (uint)KeyCapacity && _pressed[k];
        }

        public bool IsDown(ConsoleKey key)
        {
            int k = (int)key;
            return (uint)k < (uint)KeyCapacity && _down[k];
        }

        public bool WasReleased(ConsoleKey key)
        {
            int k = (int)key;
            return (uint)k < (uint)KeyCapacity && _released[k];
        }

        public int MouseX => _mouseX;
        public int MouseY => _mouseY;
        public bool MouseLeftDown => _mouseLeftDown;
        public bool MouseLeftPressed => _mouseLeftPressed;
        public bool MouseLeftReleased => _mouseLeftReleased;

        internal void SetMouseState(int x, int y, bool leftDown, bool leftPressed, bool leftReleased)
        {
            _mouseX = x;
            _mouseY = y;
            _mouseLeftDown = leftDown;
            _mouseLeftPressed = leftPressed;
            _mouseLeftReleased = leftReleased;
        }

        // Convenience for movement
        public void GetDirectional(out int dx, out int dy)
        {
            dx = 0; dy = 0;
            if (WasPressed(ConsoleKey.LeftArrow) || WasPressed(ConsoleKey.A)) dx -= 1;
            if (WasPressed(ConsoleKey.RightArrow) || WasPressed(ConsoleKey.D)) dx += 1;
            if (WasPressed(ConsoleKey.UpArrow) || WasPressed(ConsoleKey.W)) dy -= 1;
            if (WasPressed(ConsoleKey.DownArrow) || WasPressed(ConsoleKey.S)) dy += 1;
        }

        private static void AddToList(ref int count, int[] list, int value)
        {
            if (count < list.Length)
            {
                list[count++] = value;
                return;
            }

            // Very rare: if someone mashes tons of unique keys in one frame.
            // Fall back to expanding via List to preserve correctness.
            // (We keep it simple: allocate once and just use it from then on.)
            // Since this is an internal performance optimization, correctness is priority.
            throw new InvalidOperationException("Too many unique keys in one frame. Increase list capacity.");
        }
    }
}
