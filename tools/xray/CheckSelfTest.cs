using System.Text;

namespace ClrXray;

/// <summary>
/// Proves that hand editing a generated file is a change CI does not let through.
/// </summary>
/// <remarks>
/// <para>
/// The README makes two claims that everything else here rests on. No number in a lesson is typed
/// by a person, and no table in a blueprint is transcribed by one. Both of those are enforced the
/// same way: the tool regenerates the file, compares it against what is committed, and fails on
/// any difference. Both of them are therefore only as true as that comparison is.
/// </para>
/// <para>
/// Which had never been tested. A comparison that always passed would look exactly like a
/// repository where nobody had ever hand edited anything, and the difference between those two
/// would first become visible at whatever point somebody noticed a page saying something the
/// program does not. So this goes and edits the files, on purpose, and requires the build to
/// object by name.
/// </para>
/// <para>
/// The two cases that change nothing matter as much as the five that break something. Without
/// them a harness that failed on everything, including work that is correct, would report a clean
/// sweep.
/// </para>
/// </remarks>
internal static class CheckSelfTest
{
    private static int failures;

    private const string Lesson = "lessons/smoke-pipeline";
    private const string Blueprint = "blueprints/bp-metadata";

    internal static int Run(string root)
    {
        failures = 0;

        var lesson = Path.Combine(root, "lessons", "smoke-pipeline");
        var blueprint = Path.Combine(root, "blueprints", "bp-metadata");

        if (!Directory.Exists(lesson) || !Directory.Exists(blueprint))
        {
            Console.Error.WriteLine($"xray check --selftest: run this from the repository root, it needs {Lesson} and {Blueprint} to tamper with");
            return 2;
        }

        // The control. Everything below tampers with a copy of this same lesson, so if the copy
        // does not pass untouched then none of the failures underneath it mean anything.
        Case("an untouched lesson", lesson, _ => { }, null);

        Case(
            "a number changed by hand in a captured output",
            lesson,
            where => Digit(Digits(Path.Combine(where, "expected"))),
            "does not match what the code produces");

        Case(
            "a captured output deleted",
            lesson,
            where => File.Delete(First(Path.Combine(where, "expected"))),
            "missing, run");

        Case(
            "a sentence added by hand to a generated page",
            lesson,
            where => Append(Path.Combine(where, "lesson.md")),
            "lesson.md");

        Case("an untouched blueprint", blueprint, _ => { }, null);

        Case(
            "a number changed by hand in a generated blueprint section",
            blueprint,
            where => Digit(Digits(Path.Combine(where, "generated"))),
            "does not match what the code produces");

        Case(
            "a sentence added by hand to a generated blueprint page",
            blueprint,
            where => Append(Path.Combine(where, "blueprint.md")),
            "blueprint.md");

        Console.WriteLine($"xray check --selftest: {failures} failure(s)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Copies the real thing somewhere else, breaks it in one specific way, and runs the ordinary
    /// check over the copy. An expectation of nothing means the copy was not broken and the check
    /// is supposed to pass.
    /// </summary>
    private static void Case(string what, string source, Action<string> tamper, string? expected)
    {
        // The copy keeps its position in the tree, under a marker file that says this is the top of
        // a repository, because a generated page names the lesson by its path from there. A copy
        // dumped in a flat temp directory would produce a different page for that reason alone,
        // and every case below would then pass without testing anything.
        var repository = Path.Combine(Path.GetTempPath(), $"xray-check-selftest-{Environment.ProcessId}");
        var where = Path.Combine(repository, Path.GetFileName(Path.GetDirectoryName(source)!), Path.GetFileName(source));

        Directory.CreateDirectory(repository);
        File.WriteAllText(Path.Combine(repository, "ClrXray.slnx"), string.Empty);
        Copy(source, where);

        var log = new StringWriter();
        var wasOut = Console.Out;
        var wasError = Console.Error;
        int exit;

        try
        {
            tamper(where);

            Console.SetOut(log);
            Console.SetError(log);

            try
            {
                exit = LessonCommand.Run(where, write: false);
            }
            finally
            {
                Console.SetOut(wasOut);
                Console.SetError(wasError);
            }
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }

        var said = log.ToString();

        if (expected is null)
        {
            Check(exit == 0, $"passes {what}", $"it came back with {exit} and said: {Squash(said)}");
            return;
        }

        if (exit == 0)
        {
            Check(false, $"refuses {what}", "the check passed, so this gate is not doing anything");
            return;
        }

        Check(
            said.Contains(expected, StringComparison.Ordinal),
            $"refuses {what}",
            $"it failed, which is right, but did not mention '{expected}'. It said: {Squash(said)}");
    }

    /// <summary>Bumps the first digit in a file, which is the smallest edit that is still a lie.</summary>
    private static void Digit(string path)
    {
        var text = Generated.Normalise(File.ReadAllText(path));
        var at = text.AsSpan().IndexOfAnyInRange('0', '9');
        if (at < 0)
        {
            throw new LessonException($"{path}: no digit in here to change, so this case cannot test what it says it tests");
        }

        var builder = new StringBuilder(text);
        builder[at] = text[at] == '9' ? '8' : (char)(text[at] + 1);
        File.WriteAllText(path, builder.ToString());
    }

    private static void Append(string path) =>
        File.AppendAllText(path, "\nA sentence somebody added to a generated file by hand.\n");

    /// <summary>The first file in a directory, in the same order everywhere, so the case is the same everywhere.</summary>
    private static string First(string directory) =>
        Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal).First();

    /// <summary>The first file in a directory that has a digit in it to change.</summary>
    private static string Digits(string directory) =>
        Directory.EnumerateFiles(directory)
            .Order(StringComparer.Ordinal)
            .First(file => File.ReadAllText(file).Any(char.IsAsciiDigit));

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (var file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(from))
        {
            Copy(directory, Path.Combine(to, Path.GetFileName(directory)));
        }
    }

    /// <summary>The whole log on one line, because a failure here is worth reading in full.</summary>
    private static string Squash(string text) =>
        string.Join(" | ", text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));

    private static void Check(bool passed, string what, string why)
    {
        if (passed)
        {
            Console.WriteLine($"  ok    {what}");
            return;
        }

        failures++;
        Console.Error.WriteLine($"  FAIL  {what}: {why}");
    }
}
