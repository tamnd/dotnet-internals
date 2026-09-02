using System.Diagnostics;

namespace ClrXray;

/// <summary>
/// Runs the SDK and hands back what it printed.
/// </summary>
/// <remarks>
/// Every process this tool starts goes through here, so the environment a lesson runs in is
/// stated in one place rather than being whatever the machine happened to have set. A lesson that
/// prints a different number on somebody's laptop than it does in CI is a lesson that teaches the
/// wrong thing, and the usual cause is an environment variable nobody wrote down.
/// </remarks>
internal static class Runner
{
    internal static (int Exit, string Out, string Error) Dotnet(string directory, string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        // A lesson's output is compared byte for byte across four platforms, so anything that
        // formats a number differently on one of them has to be pinned here rather than argued
        // about later. A lesson that is actually about culture will need a way to turn this off,
        // and it can have one when it is written.
        process.StartInfo.Environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1";
        process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";
        process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        return (process.ExitCode, Generated.Normalise(stdout.GetAwaiter().GetResult()), Generated.Normalise(stderr.GetAwaiter().GetResult()));
    }
}
