using System;

namespace Lumi;

/// Lines on stderr for the CI integration test (LUMI_LOG=1); silent otherwise.
public static class Log
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("LUMI_LOG") == "1";

    public static void Write(string line)
    {
        if (!Enabled) return;
        try
        {
            Console.Error.WriteLine("lumi: " + line);
            Console.Error.Flush();
        }
        catch
        {
        }
    }
}
