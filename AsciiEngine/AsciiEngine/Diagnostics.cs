using System;

namespace AsciiEngine
{
    internal static class Diagnostics
    {
        internal static readonly bool Enabled =
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASCII_DEBUG"));

        internal static void Log(string message)
        {
            if (!Enabled) return;
            Console.Error.WriteLine(message);
        }
    }
}
