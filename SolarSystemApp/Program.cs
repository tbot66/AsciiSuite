using System;
using AsciiEngine;
using SolarSystemApp;

internal static class Program
{
    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8; // ✅ Spectre box chars render correctly
        AsciiRunner.Run(new SolarSystemScene(), fpsCap: 30);
    }
}
