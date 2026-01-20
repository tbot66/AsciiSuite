using System;
using OpenTK;
using SDL2;

namespace AsciiEngine
{
    internal sealed class SdlBindingsContext : IBindingsContext
    {
        public IntPtr GetProcAddress(string procName)
            => SDL.SDL_GL_GetProcAddress(procName);
    }
}
