using System.Diagnostics;
using System.Text;

namespace ClrXray;

/// <summary>
/// Builds a lesson, or checks that the lesson already in the repository is the one the code
/// produces.
/// </summary>
/// <remarks>
/// <para>
/// These are the same operation with one flag flipped, and that is the point. <c>xray build</c>
/// writes the captured output and the rendered page. <c>xray check</c> does all the same work
/// and then fails if what it produced is not what is committed. So the pull request that changes
/// a line of lesson code cannot be merged with the old numbers still on the page, and nobody has
/// to notice in review.
/// </para>
/// <para>
/// The run itself is one process per lesson. The lesson source is copied with a marker printed
/// at the top of each block, the whole program runs once, and the markers cut the output into
/// per block files afterwards. Running blocks separately would be tidier on paper and wrong in
/// practice, because a lesson is a sequence where the third block depends on what the first one
/// allocated.
/// </para>
/// </remarks>
internal static class LessonCommand
{
    private const string SourceName = "lesson.cs";
    private const string ProseName = "lesson.src.md";
    private const string PageName = "lesson.md";
    private const string ExpectedDirectory = "expected";
    private const string RunDirectory = ".xray";

    internal static int Run(string path, bool write)
    {
        var lessons = Discover(path);
        if (lessons.Count == 0)
        {
            Console.Error.WriteLine($"xray: no lesson under {path}, a lesson is a directory holding {SourceName}");
            return 2;
        }

        var problems = 0;

        foreach (var lesson in lessons)
        {
            try
            {
                problems += One(lesson, write);
            }
            catch (LessonException error)
            {
                Console.Error.WriteLine($"xray: {error.Message}");
                problems++;
            }
        }

        var verb = write ? "build" : "check";
        Console.WriteLine($"xray {verb}: {lessons.Count} lesson(s), {problems} problem(s)");
        return problems == 0 ? 0 : 1;
    }

    private static List<string> Discover(string path)
    {
        if (File.Exists(Path.Combine(path, SourceName)))
        {
            return [Path.GetFullPath(path)];
        }

        if (!Directory.Exists(path))
        {
            return [];
        }

        return Directory.EnumerateFiles(path, SourceName, SearchOption.AllDirectories)
            .Select(file => Path.GetFullPath(Path.GetDirectoryName(file)!))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static int One(string directory, bool write)
    {
        var name = Path.GetFileName(directory);
        var sourcePath = Path.Combine(directory, SourceName);
        var lines = File.ReadAllLines(sourcePath);
        var blocks = Blocks.Parse(sourcePath, lines);

        BuildFixture(directory);

        var captured = Execute(directory, lines, blocks);
        var problems = 0;

        foreach (var block in blocks)
        {
            if (block.Capture != Blocks.Stdout)
            {
                continue;
            }

            if (!captured.TryGetValue(block.Id, out var output))
            {
                Console.Error.WriteLine($"{name}: block '{block.Id}' printed no marker, so it never ran");
                problems++;
                continue;
            }

            problems += Machine(directory, name, block.Id, output);
            problems += Settle(Path.Combine(directory, ExpectedDirectory, block.Id + ".txt"), output, write);
        }

        var prosePath = Path.Combine(directory, ProseName);
        if (File.Exists(prosePath))
        {
            var page = Render(directory, prosePath, blocks, captured);
            problems += Settle(Path.Combine(directory, PageName), page, write);
        }

        return problems;
    }

    /// <summary>
    /// Builds the lesson's fixture, if it has one. A fixture is a tiny project the lesson reads
    /// rather than a binary committed to the repository, because a committed binary is a number
    /// typed by a human in the only format nobody can review.
    /// </summary>
    private static void BuildFixture(string directory)
    {
        var fixture = Path.Combine(directory, "fixture");
        if (!Directory.Exists(fixture) || !Directory.EnumerateFiles(fixture, "*.csproj").Any())
        {
            return;
        }

        var (exit, _, stderr) = Start(directory, ["build", fixture, "--configuration", "Release", "--nologo", "--verbosity", "quiet"]);
        if (exit != 0)
        {
            throw new LessonException($"{Path.GetFileName(directory)}: the fixture did not build\n{stderr}");
        }
    }

    private static Dictionary<string, string> Execute(string directory, IReadOnlyList<string> lines, IReadOnlyList<Block> blocks)
    {
        var runDirectory = Path.Combine(directory, RunDirectory);
        var runFile = Path.Combine(runDirectory, "run.cs");

        try
        {
            Directory.CreateDirectory(runDirectory);
            File.WriteAllText(runFile, Blocks.Rewrite(lines, blocks));

            var (exit, stdout, stderr) = Start(directory, ["run", Path.Combine(RunDirectory, "run.cs")]);
            if (exit != 0)
            {
                throw new LessonException($"{Path.GetFileName(directory)}: the lesson exited with {exit}\n{stderr}");
            }

            return Split(stdout);
        }
        finally
        {
            if (Directory.Exists(runDirectory))
            {
                Directory.Delete(runDirectory, recursive: true);
            }
        }
    }

    private static (int Exit, string Out, string Error) Start(string directory, string[] arguments)
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

        return (process.ExitCode, Normalise(stdout.GetAwaiter().GetResult()), Normalise(stderr.GetAwaiter().GetResult()));
    }

    /// <summary>
    /// Cuts one program's output into one piece per block. Everything before the first marker is
    /// dropped, which is what makes build chatter from a cold restore harmless.
    /// </summary>
    private static Dictionary<string, string> Split(string stdout)
    {
        var captured = new Dictionary<string, string>(StringComparer.Ordinal);
        var current = (string?)null;
        var body = new List<string>();

        foreach (var line in stdout.Split('\n'))
        {
            if (line.StartsWith(Blocks.MarkerPrefix, StringComparison.Ordinal))
            {
                Flush();
                current = line[Blocks.MarkerPrefix.Length..].Trim();
                body.Clear();
                continue;
            }

            if (current is not null)
            {
                body.Add(line);
            }
        }

        Flush();
        return captured;

        void Flush()
        {
            if (current is null)
            {
                return;
            }

            while (body.Count > 0 && body[^1].Length == 0)
            {
                body.RemoveAt(body.Count - 1);
            }

            captured[current] = body.Count == 0 ? string.Empty : string.Join('\n', body) + "\n";
        }
    }

    /// <summary>
    /// Rejects captured output that carries something about the machine that produced it. A path
    /// out of somebody's home directory in an expected file is a file that can only ever match on
    /// one laptop, and it fails on the fourth platform instead of in review.
    /// </summary>
    private static int Machine(string directory, string name, string id, string output)
    {
        var problems = 0;

        foreach (var secret in Leaks(directory))
        {
            if (secret.Length > 0 && output.Contains(secret, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"{name}/{id}: output contains a path from this machine, print a file name rather than a full path");
                problems++;
                break;
            }
        }

        return problems;
    }

    private static IEnumerable<string> Leaks(string directory)
    {
        yield return directory;
        yield return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string Render(string directory, string prosePath, IReadOnlyList<Block> blocks, Dictionary<string, string> captured)
    {
        var byId = blocks.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var gates = Gates.Load(directory);
        var source = File.ReadAllLines(prosePath);
        var page = new StringBuilder();
        var notice = false;

        for (var i = 0; i < source.Length; i++)
        {
            var line = source[i];
            var trimmed = line.Trim();

            page.Append(line).Append('\n');

            // The notice goes under the front matter rather than above it, because a page whose
            // first line is not the front matter opener is a page a static site generator will
            // publish with the metadata showing.
            if (!notice && i > 0 && trimmed == "---" && source[0].Trim() == "---")
            {
                page.Append("\n<!-- Generated by xray from lesson.src.md and lesson.cs. Do not edit this file, edit those two and run: dotnet run --project tools/xray -- build lessons -->\n");
                notice = true;
            }
        }

        var rendered = new StringBuilder();
        foreach (var line in page.ToString().Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("{{", StringComparison.Ordinal) || !trimmed.EndsWith("}}", StringComparison.Ordinal))
            {
                rendered.Append(line).Append('\n');
                continue;
            }

            var inner = trimmed[2..^2].Trim();
            var colon = inner.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                throw new LessonException($"{prosePath}: '{trimmed}' is not a transclusion");
            }

            var kind = inner[..colon];
            var id = inner[(colon + 1)..];

            rendered.Append(Transclude(prosePath, kind, id, byId, captured, gates)).Append('\n');
        }

        return rendered.ToString().TrimEnd('\n') + "\n";
    }

    private static string Transclude(
        string prosePath,
        string kind,
        string id,
        Dictionary<string, Block> blocks,
        Dictionary<string, string> captured,
        IReadOnlyDictionary<string, Gate> gates)
    {
        switch (kind)
        {
            case "block":
                if (!blocks.TryGetValue(id, out var block))
                {
                    throw new LessonException($"{prosePath}: no block named '{id}' in {SourceName}");
                }

                return "```csharp\n" + block.Source + "\n```";

            case "output":
                if (!blocks.TryGetValue(id, out var printed) || printed.Capture != Blocks.Stdout)
                {
                    throw new LessonException($"{prosePath}: block '{id}' does not store its output, so the page cannot quote it");
                }

                if (!captured.TryGetValue(id, out var output))
                {
                    throw new LessonException($"{prosePath}: block '{id}' has no captured output");
                }

                return "```text\n" + (output.Length == 0 ? "(the block printed nothing)\n" : output) + "```";

            case "gate":
                if (!gates.TryGetValue(id, out var gate))
                {
                    throw new LessonException($"{prosePath}: no gate named '{id}' in gates.json");
                }

                return Gates.Render(gate);

            default:
                throw new LessonException($"{prosePath}: '{kind}' is not a kind of transclusion");
        }
    }

    /// <summary>
    /// Writes the file, or reports the first place it differs from what is on disk.
    /// </summary>
    private static int Settle(string path, string content, bool write)
    {
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);

        if (write)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var before = File.Exists(path) ? Normalise(File.ReadAllText(path)) : null;
            if (before != content)
            {
                File.WriteAllText(path, content);
                Console.WriteLine($"  wrote {relative}");
            }

            return 0;
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"{relative}: missing, run: dotnet run --project tools/xray -- build lessons");
            return 1;
        }

        var actual = Normalise(File.ReadAllText(path));
        if (actual == content)
        {
            return 0;
        }

        Console.Error.WriteLine($"{relative}: does not match what the code produces");
        Report(content, actual);
        return 1;
    }

    private static void Report(string wanted, string found)
    {
        var a = wanted.Split('\n');
        var b = found.Split('\n');
        var shown = 0;

        for (var i = 0; i < Math.Max(a.Length, b.Length) && shown < 3; i++)
        {
            var left = i < a.Length ? a[i] : "(end of file)";
            var right = i < b.Length ? b[i] : "(end of file)";
            if (left == right)
            {
                continue;
            }

            Console.Error.WriteLine($"  line {i + 1} produced: {left}");
            Console.Error.WriteLine($"  line {i + 1} on disk:  {right}");
            shown++;
        }
    }

    /// <summary>
    /// One line ending, everywhere. Windows would otherwise disagree with the other three
    /// platforms about every line of every expected file, which is a real difference about
    /// nothing.
    /// </summary>
    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
