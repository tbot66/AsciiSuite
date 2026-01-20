using System;

namespace AsciiEngine
{
    public static class Diagnostics
    {
        public static readonly bool Enabled =
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASCII_DEBUG"));

        public static void Log(string message)
        {
            if (!Enabled) return;
            Console.Error.WriteLine(message);
        }
    }
}
