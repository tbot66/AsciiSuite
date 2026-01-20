using System;

namespace AsciiEngine
{
    public enum MouseButton
    {
        Left = 1,
        Middle = 2,
        Right = 3,
        X1 = 4,
        X2 = 5
    }

    public sealed class InputState
    {
        private const int KeyCapacity = 512;
        private const int MouseButtonCapacity = 8;

        private readonly bool[] _down = new bool[KeyCapacity];
        private readonly bool[] _pressed = new bool[KeyCapacity];
        private readonly bool[] _released = new bool[KeyCapacity];

        private int[] _pressedList = new int[64];
        private int _pressedCount;

        private int[] _releasedList = new int[64];
        private int _releasedCount;

        private int[] _autoReleaseList = new int[32];
        private int _autoReleaseCount;

        private readonly bool[] _mouseDown = new bool[MouseButtonCapacity];
        private readonly bool[] _mousePressed = new bool[MouseButtonCapacity];
        private readonly bool[] _mouseReleased = new bool[MouseButtonCapacity];

        private int[] _mousePressedList = new int[8];
        private int _mousePressedCount;

        private int[] _mouseReleasedList = new int[8];
        private int _mouseReleasedCount;

        public int MouseX { get; private set; }
        public int MouseY { get; private set; }
        public int MouseDeltaX { get; private set; }
        public int MouseDeltaY { get; private set; }

        public bool QuitRequested { get; private set; }
        public bool Shift { get; private set; }
        public bool Alt { get; private set; }
        public bool Control { get; private set; }

        internal void BeginFrame()
        {
            for (int i = 0; i < _pressedCount; i++)
                _pressed[_pressedList[i]] = false;
            _pressedCount = 0;

            for (int i = 0; i < _releasedCount; i++)
                _released[_releasedList[i]] = false;
            _releasedCount = 0;

            for (int i = 0; i < _mousePressedCount; i++)
                _mousePressed[_mousePressedList[i]] = false;
            _mousePressedCount = 0;

            for (int i = 0; i < _mouseReleasedCount; i++)
                _mouseReleased[_mouseReleasedList[i]] = false;
            _mouseReleasedCount = 0;

            MouseDeltaX = 0;
            MouseDeltaY = 0;

            if (_autoReleaseCount > 0)
            {
                for (int i = 0; i < _autoReleaseCount; i++)
                {
                    int k = _autoReleaseList[i];
                    if ((uint)k >= (uint)KeyCapacity) continue;
                    if (_down[k])
                    {
                        _down[k] = false;
                        if (!_released[k])
                        {
                            _released[k] = true;
                            AddToList(ref _releasedCount, ref _releasedList, k);
                        }
                    }
                }

                _autoReleaseCount = 0;
            }
        }

        internal void EndFrame()
        {
        }

        internal void OnKeyDown(ConsoleKey key)
        {
            SetKeyDown(key, autoRelease: false);
        }

        internal void OnKeyUp(ConsoleKey key)
        {
            int k = (int)key;
            if ((uint)k >= (uint)KeyCapacity) return;

            if (_down[k])
                _down[k] = false;

            if (!_released[k])
            {
                _released[k] = true;
                AddToList(ref _releasedCount, ref _releasedList, k);
            }
        }

        internal void OnKeyPressed(ConsoleKey key)
        {
            SetKeyDown(key, autoRelease: true);
        }

        internal void RequestQuit()
        {
            QuitRequested = true;
        }

        internal void SetModifiers(bool shift, bool alt, bool control)
        {
            Shift = shift;
            Alt = alt;
            Control = control;
        }

        internal void OnMouseMove(int x, int y, int dx, int dy)
        {
            MouseX = x;
            MouseY = y;
            MouseDeltaX += dx;
            MouseDeltaY += dy;
        }

        internal void OnMouseButtonDown(MouseButton button)
        {
            int index = (int)button;
            if ((uint)index >= (uint)MouseButtonCapacity) return;

            if (!_mouseDown[index])
                _mouseDown[index] = true;

            if (!_mousePressed[index])
            {
                _mousePressed[index] = true;
                AddToList(ref _mousePressedCount, ref _mousePressedList, index);
            }
        }

        internal void OnMouseButtonUp(MouseButton button)
        {
            int index = (int)button;
            if ((uint)index >= (uint)MouseButtonCapacity) return;

            if (_mouseDown[index])
                _mouseDown[index] = false;

            if (!_mouseReleased[index])
            {
                _mouseReleased[index] = true;
                AddToList(ref _mouseReleasedCount, ref _mouseReleasedList, index);
            }
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

        public bool WasMousePressed(MouseButton button)
        {
            int index = (int)button;
            return (uint)index < (uint)MouseButtonCapacity && _mousePressed[index];
        }

        public bool IsMouseDown(MouseButton button)
        {
            int index = (int)button;
            return (uint)index < (uint)MouseButtonCapacity && _mouseDown[index];
        }

        public bool WasMouseReleased(MouseButton button)
        {
            int index = (int)button;
            return (uint)index < (uint)MouseButtonCapacity && _mouseReleased[index];
        }

        public void GetDirectional(out int dx, out int dy)
        {
            dx = 0; dy = 0;
            if (WasPressed(ConsoleKey.LeftArrow) || WasPressed(ConsoleKey.A)) dx -= 1;
            if (WasPressed(ConsoleKey.RightArrow) || WasPressed(ConsoleKey.D)) dx += 1;
            if (WasPressed(ConsoleKey.UpArrow) || WasPressed(ConsoleKey.W)) dy -= 1;
            if (WasPressed(ConsoleKey.DownArrow) || WasPressed(ConsoleKey.S)) dy += 1;
        }

        private void SetKeyDown(ConsoleKey key, bool autoRelease)
        {
            int k = (int)key;
            if ((uint)k >= (uint)KeyCapacity) return;

            if (!_down[k])
                _down[k] = true;

            if (!_pressed[k])
            {
                _pressed[k] = true;
                AddToList(ref _pressedCount, ref _pressedList, k);
            }

            if (autoRelease)
                AddToList(ref _autoReleaseCount, ref _autoReleaseList, k);
        }

        private static void AddToList(ref int count, ref int[] list, int value)
        {
            if (count >= list.Length)
            {
                int newSize = Math.Max(list.Length * 2, 4);
                Array.Resize(ref list, newSize);
            }

            list[count++] = value;
        }
    }
}
