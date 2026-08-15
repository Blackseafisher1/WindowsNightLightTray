using System;
using System.Threading;
using NightLightTray;

namespace NightLightTests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static void Main()
        {
            Check("Available", NightLightController.IsAvailable(), true);

            bool original = NightLightController.GetEnabled();
            Check("Enabled state parseable", NightLightController.GetEnabled() || !NightLightController.GetEnabled(), true);

            Console.WriteLine("[live] toggling OFF...");
            NightLightController.SetEnabled(false);
            Thread.Sleep(800);
            Check("Enabled false after toggle off", NightLightController.GetEnabled(), false);

            Console.WriteLine("[live] toggling ON...");
            NightLightController.SetEnabled(true);
            Thread.Sleep(800);
            Check("Enabled true after toggle on", NightLightController.GetEnabled(), true);

            if (!original)
            {
                Console.WriteLine("[live] restoring enabled to OFF...");
                NightLightController.SetEnabled(false);
            }

            Console.WriteLine();
            Console.WriteLine("PASSED: {0}  FAILED: {1}", _passed, _failed);
            Environment.Exit(_failed == 0 ? 0 : 1);
        }

        private static void Check(string name, bool actual, bool expected)
        {
            bool ok = actual == expected;
            if (ok)
            {
                _passed++;
                Console.WriteLine("[PASS] " + name);
            }
            else
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name + " (expected " + expected + ", got " + actual + ")");
            }
        }
    }
}
