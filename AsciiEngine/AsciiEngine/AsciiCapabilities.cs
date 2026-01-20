using System;
using System.Text;

namespace AsciiEngine
{
    public static class AsciiCapabilities
    {
        public static bool UnicodeOk { get; private set; }
        public static bool SanitizerActive => !UnicodeOk;
        public static int OutputCodePage { get; private set; }
        public static string OutputEncodingName { get; private set; } = "unknown";

        public static void Initialize()
        {
            bool forceAscii = EnvFlag("ASCII_FORCE_ASCII");
            bool forceUtf8 = EnvFlag("ASCII_FORCE_UTF8");

            // Ensure the console is configured for UTF-8 as early as possible.
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.InputEncoding = Encoding.UTF8;
            }
            catch
            {
                // Best-effort only; redirected output can throw.
            }

            try
            {
                OutputCodePage = Console.OutputEncoding.CodePage;
                OutputEncodingName = Console.OutputEncoding.WebName;
            }
            catch
            {
                OutputCodePage = 0;
                OutputEncodingName = "unknown";
            }

            bool isUtf8 = OutputCodePage == Encoding.UTF8.CodePage ||
                          string.Equals(OutputEncodingName, "utf-8", StringComparison.OrdinalIgnoreCase);

            // Commit-note: UnicodeOk honors env overrides so the renderer can force ASCII/UTF-8 behavior.
            UnicodeOk = (forceUtf8 || isUtf8) && !forceAscii;
        }

        internal static string DescribePresenter(string presenterName, int width, int height)
        {
            return $"[AsciiEngine] Presenter: {presenterName}. Size={width}x{height}, UnicodeOk={UnicodeOk}, sanitizer={SanitizerActive}, OutputEncoding={OutputEncodingName} ({OutputCodePage}).";
        }

        private static bool EnvFlag(string name)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value)) return false;
            return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("on", StringComparison.OrdinalIgnoreCase);
        }
    }
}
