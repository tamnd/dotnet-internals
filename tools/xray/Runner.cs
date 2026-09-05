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
    /// <summary>
    /// The directory the file being run belongs to, as an absolute path, in the child's
    /// environment.
    /// </summary>
    /// <remarks>
    /// This exists because <c>dotnet run some/file.cs</c> does not promise what the working
    /// directory of the file will be, and the two SDKs in the range this repository accepts
    /// disagree about it. On 10.0.400 the child inherits the working directory it was started
    /// with. On 10.0.100, which is the version <c>global.json</c> literally names, a file run this
    /// way can get the directory the file itself is in instead. A boss fight sitting in
    /// <c>boss/</c> and reading <c>lesson.cs</c> is right under one of those and looking one
    /// directory too deep under the other, and which one you get was decided by whichever SDK the
    /// machine resolved. So nothing here reads a file by a path relative to the working directory,
    /// and the build refuses lesson code that tries.
    /// </remarks>
    internal const string Here = "XRAY_HERE";

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
        process.StartInfo.Environment[Here] = Path.GetFullPath(directory);

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        return (process.ExitCode, Generated.Normalise(stdout.GetAwaiter().GetResult()), Generated.Normalise(stderr.GetAwaiter().GetResult()));
    }

    /// <summary>
    /// Everything a failed child said, both streams, labelled.
    /// </summary>
    /// <remarks>
    /// Reporting only the error stream is the obvious thing to do and it is wrong here. When a
    /// lesson fails to compile, the SDK writes the compiler's diagnostics to the output stream and
    /// puts "The build failed. Fix the build errors and run again." on the error stream. Report the
    /// error stream alone and the reader is told the build failed, with the reason discarded a
    /// moment earlier by the program telling them.
    /// </remarks>
    internal static string Said(string stdout, string stderr)
    {
        var said = new List<string>();

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            said.Add(stderr.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            said.Add("and on its output stream:\n" + stdout.TrimEnd());
        }

        return said.Count == 0 ? "and said nothing on either stream" : string.Join("\n\n", said);
    }

    /// <summary>
    /// Refuses a file the build runs that reaches for something on disk starting from a string
    /// literal, because the place a relative path starts from is the one thing about running that
    /// file the SDK does not promise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a small rule with a large reason. Every path in this repository was relative and
    /// every one of them was correct, on every machine anybody had run it on, right up until a
    /// machine resolved a different SDK feature band and four of them started pointing one
    /// directory too deep. Nothing went red where it broke, because where it broke was inside the
    /// self test, and the self test reported five failures whose text was a stack trace.
    /// </para>
    /// <para>
    /// The rule is mechanical so that it is not a judgement call. The first thing handed to
    /// <c>File</c>, <c>Directory</c> or <c>Path.Combine</c> is never a literal. It is
    /// <c>XRAY_HERE</c>, which the build sets to the directory the file belongs to, or something
    /// derived from it.
    /// </para>
    /// </remarks>
    internal static void Rooted(string path, IReadOnlyList<string> lines, Plan plan)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var call in Starts(line))
            {
                plan.Problem($"{path}:{i + 1}: {call} starts from a string literal, and where a relative path starts from is the one thing about running this file the SDK does not promise. Start from {Here} instead, which the build sets to this file's own directory.");
            }
        }
    }

    /// <summary>The calls on one line that begin at a literal, named the way they are written.</summary>
    private static IEnumerable<string> Starts(string line)
    {
        foreach (var opening in new[] { "File.", "Directory.", "Path.Combine(" })
        {
            var at = 0;

            while ((at = line.IndexOf(opening, at, StringComparison.Ordinal)) >= 0)
            {
                var bracket = opening.EndsWith('(') ? at + opening.Length - 1 : line.IndexOf('(', at);
                at += opening.Length;

                if (bracket < 0 || bracket + 1 >= line.Length || line[bracket + 1] != '"')
                {
                    continue;
                }

                var name = line[(bracket + 1)..];
                yield return line[..bracket].Split(' ', '\t', '(', '=', ',').Last() + "(" + name[..Math.Min(name.IndexOf('"', 1) + 1, name.Length)];
            }
        }
    }
}
