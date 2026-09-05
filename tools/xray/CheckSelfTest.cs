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
/// The cases that change nothing matter as much as the ones that break something. Without them a
/// harness that failed on everything, including work that is correct, would report a clean sweep.
/// </para>
/// </remarks>
internal static class CheckSelfTest
{
    private static int failures;
    private static string top = ".";

    private const string Lesson = "lessons/smoke-pipeline";
    private const string Blueprint = "blueprints/bp-metadata";

    internal static int Run(string root)
    {
        failures = 0;
        top = Files.Root(root);

        var lesson = Path.Combine(top, "lessons", "smoke-pipeline");
        var blueprint = Path.Combine(top, "blueprints", "bp-metadata");

        if (!Directory.Exists(lesson) || !Directory.Exists(blueprint))
        {
            Console.Error.WriteLine($"xray check --selftest: run this from the repository root, it needs {Lesson} and {Blueprint} to tamper with");
            return 2;
        }

        // The control. Everything below tampers with a copy of this same lesson, so if the copy
        // does not pass untouched then none of the failures underneath it mean anything.
        Case("an untouched lesson", lesson, (_, _) => { });

        Case(
            "a number changed by hand in a captured output",
            lesson,
            (_, where) => Digit(Digits(Path.Combine(where, "expected"))),
            "does not match what the code produces");

        Case(
            "a captured output deleted",
            lesson,
            (_, where) => File.Delete(First(Path.Combine(where, "expected"))),
            "missing, run");

        Case(
            "a sentence added by hand to a generated page",
            lesson,
            (_, where) => Append(Path.Combine(where, "lesson.md")),
            "lesson.md");

        // A block gets renamed and its old captured output stays on disk. Nothing reads it and
        // nothing compares it, so before the build kept a list of what it produces this was
        // invisible.
        Case(
            "a captured output left behind by a block that no longer exists",
            lesson,
            (_, where) => File.WriteAllText(Path.Combine(where, "expected", "renamed-away.txt"), "42\n"),
            "left over from a block that has been renamed or deleted");

        // Not a tamper with a generated file at all. This one proves the first step of the build
        // is load bearing, and that a build which cannot say what it is pinned to does not go on
        // to produce five steps worth of output anyway.
        Case(
            "a build that cannot say what it is pinned to",
            lesson,
            (repository, _) => File.Delete(Path.Combine(repository, Resolve.PinName)),
            "nothing saying what this build is pinned to",
            "the steps after that one did not run");

        // The attribute every block has carried since the first lesson, and which until recently
        // nothing read. A block naming a configuration nobody declared has to be refused, or the
        // number in the page comes from a runtime the page says it did not use.
        Case(
            "a block declaring an environment nobody declared",
            lesson,
            (_, where) => Retype(Path.Combine(where, Lessons.SourceName), "env=E0", "env=E7"),
            $"declares env=E7, which is not in {Environments.FileName}");

        // The other half of that rule. A lesson this machine cannot run is left out rather than
        // regenerated from a run that did not happen, and leaving it out is only allowed when
        // somebody else has already built it and committed the result.
        Case(
            "a lesson skipped for a missing environment that has never been built",
            lesson,
            (repository, where) =>
            {
                Absent(repository);
                Wants(where);
                File.Delete(Path.Combine(where, "lesson.md"));
            },
            "no committed copy of this file either, so nothing has ever produced it");

        // And the case that keeps the one above honest. Same missing environment, same skip, but
        // the pages are on disk, so there is nothing to complain about.
        Case(
            "a lesson skipped for a missing environment whose pages are committed",
            lesson,
            (repository, where) =>
            {
                Absent(repository);
                Wants(where);
            });

        // The rule that used to live in CONTRIBUTING and was enforced by somebody remembering it.
        Case(
            "a lesson that needs more than the stock SDK and never says so",
            lesson,
            (repository, where) =>
            {
                Absent(repository);
                Wants(where);
                Retype(Path.Combine(where, "lesson.src.md"), "{{needs}}", string.Empty);
            },
            "the page never says so");

        // A lesson says what it needs in two places, and two places that are never compared is
        // one place plus a decoration.
        Case(
            "front matter and blocks disagreeing about what a lesson needs",
            lesson,
            (repository, where) =>
            {
                Absent(repository);
                Retype(Path.Combine(where, Lessons.SourceName), "env=E0", "env=E3");
            },
            "the front matter says env: E0 and the most a block in this lesson asks for is E3");

        Case("an untouched blueprint", blueprint, (_, _) => { });

        Case(
            "a number changed by hand in a generated blueprint section",
            blueprint,
            (_, where) => Digit(Digits(Path.Combine(where, "generated"))),
            "does not match what the code produces");

        Case(
            "a sentence added by hand to a generated blueprint page",
            blueprint,
            (_, where) => Append(Path.Combine(where, "blueprint.md")),
            "blueprint.md");

        Console.WriteLine($"xray check --selftest: {failures} failure(s)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Copies the real thing somewhere else, breaks it in one specific way, and runs the ordinary
    /// check over the copy. No expectations means the copy was not broken and the check is
    /// supposed to pass. Otherwise every expectation has to turn up in what the check said, so
    /// that a red build for some unrelated reason does not count as this case passing.
    /// </summary>
    private static void Case(string what, string source, Action<string, string> tamper, params string[] expected)
    {
        // The copy keeps its position in the tree, under the three files that make a directory the
        // top of this repository, because a generated page names the lesson by its path from there
        // and the build refuses to start without a pin. A copy dumped in a flat temp directory
        // would produce a different page for that reason alone, and every case below would then
        // pass without testing anything.
        var repository = Path.Combine(Path.GetTempPath(), $"xray-check-selftest-{Environment.ProcessId}");
        var where = Path.Combine(repository, Path.GetFileName(Path.GetDirectoryName(source)!), Path.GetFileName(source));

        Directory.CreateDirectory(repository);
        File.WriteAllText(Path.Combine(repository, "ClrXray.slnx"), string.Empty);
        File.Copy(Path.Combine(top, Resolve.PinName), Path.Combine(repository, Resolve.PinName), overwrite: true);
        File.Copy(Path.Combine(top, Resolve.GlobalName), Path.Combine(repository, Resolve.GlobalName), overwrite: true);
        File.Copy(Path.Combine(top, Environments.FileName), Path.Combine(repository, Environments.FileName), overwrite: true);

        // The declaration points at the documents that say how to get each configuration, and the
        // build checks those exist. The copy needs them, as empty files, or every case below fails
        // for a reason that has nothing to do with what the case is testing.
        foreach (var how in Environments.Load(top).Select(c => c.How).Distinct(StringComparer.Ordinal))
        {
            var to = Path.Combine(repository, how);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.WriteAllText(to, string.Empty);
        }

        Copy(source, where);

        var log = new StringWriter();
        var wasOut = Console.Out;
        var wasError = Console.Error;
        int exit;

        try
        {
            tamper(repository, where);

            Console.SetOut(log);
            Console.SetError(log);

            try
            {
                exit = Build.Run(where, write: false, offline: true);
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

        if (expected.Length == 0)
        {
            Check(exit == 0, $"passes {what}", $"it came back with {exit} and said: {Squash(said)}");
            return;
        }

        if (exit == 0)
        {
            Check(false, $"refuses {what}", "the check passed, so this gate is not doing anything");
            return;
        }

        var missing = expected.Where(text => !said.Contains(text, StringComparison.Ordinal)).ToList();

        Check(
            missing.Count == 0,
            $"refuses {what}",
            $"it failed, which is right, but did not mention {string.Join(" or ", missing.Select(m => $"'{m}'"))}. It said: {Squash(said)}");
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

    /// <summary>
    /// Makes the lesson need the rung that nothing on this machine provides, in both of the places
    /// a lesson says what it needs.
    /// </summary>
    private static void Wants(string where)
    {
        Retype(Path.Combine(where, Lessons.SourceName), "env=E0", "env=E3");
        Retype(Path.Combine(where, "lesson.src.md"), "env: E0", "env: E3");
    }

    /// <summary>Changes the first occurrence of something, which is one block rather than all of them.</summary>
    private static void Retype(string path, string from, string to)
    {
        var text = File.ReadAllText(path);
        var at = text.IndexOf(from, StringComparison.Ordinal);

        if (at < 0)
        {
            throw new LessonException($"{path}: nothing here says '{from}', so this case cannot test what it says it tests");
        }

        File.WriteAllText(path, string.Concat(text.AsSpan(0, at), to, text.AsSpan(at + from.Length)));
    }

    /// <summary>
    /// Declares a fourth configuration that this machine certainly does not have, because it is
    /// found by an environment variable nothing sets. The rung has to be the next one up rather
    /// than a number picked out of the air, since the ladder is checked for gaps.
    /// </summary>
    private static void Absent(string repository)
    {
        var path = Path.Combine(repository, Environments.FileName);
        var rung = Environments.Load(repository).Count;

        var entry = $$"""
              {
                "id": "E{{rung}}",
                "name": "a configuration nothing on this machine provides",
                "what": "Declared by the self test so that skipping can be tested at all.",
                "cost": "Not obtainable, which is the point of it.",
                "how": "README.md",
                "here": {
                  "kind": "pointed-at",
                  "variable": "XRAY_SELFTEST_NEVER_SET"
                }
              }
            """;

        Retype(path, "\n  ]", ",\n" + entry + "\n  ]");
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
