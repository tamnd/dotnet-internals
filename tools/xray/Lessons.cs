using System.Text;

namespace ClrXray;

/// <summary>
/// One lesson that has been run, and everything the later steps of the build need from it.
/// </summary>
/// <remarks>
/// A lesson runs exactly once per build. Everything after that reads this rather than starting
/// another process, which is why the page and the captured output can never be produced by two
/// different runs of the same program.
/// </remarks>
internal sealed record Ran(
    string Directory,
    string Name,
    IReadOnlyList<Block> Blocks,
    IReadOnlyList<Configuration> Needs,
    IReadOnlyDictionary<string, Asserted> Asserts,
    Dictionary<string, string> Captured,
    string? BossAnswers);

/// <summary>
/// The lesson side of the build, in the three steps the pipeline names: execute the code, generate
/// the files that hold what it printed, and assemble the page around them.
/// </summary>
/// <remarks>
/// <para>
/// The run itself is one process per lesson. The lesson source is copied with a marker printed
/// at the top of each block, the whole program runs once, and the markers cut the output into
/// per block files afterwards. Running blocks separately would be tidier on paper and wrong in
/// practice, because a lesson is a sequence where the third block depends on what the first one
/// allocated.
/// </para>
/// <para>
/// Nothing here writes a file. Every one of these steps hands its work to the plan, and the plan
/// is settled at the end, which is what makes <c>build</c> and <c>check</c> the same six steps
/// with one flag flipped. So the pull request that changes a line of lesson code cannot be merged
/// with the old numbers still on the page, and nobody has to notice in review.
/// </para>
/// </remarks>
internal static class Lessons
{
    internal const string SourceName = "lesson.cs";
    private const string ProseName = "lesson.src.md";
    private const string PageName = "lesson.md";
    private const string ExpectedDirectory = "expected";

    internal static List<string> Discover(string path)
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

    /// <summary>
    /// Step three. Builds the fixture, runs the lesson once, and checks everything that is a fact
    /// about the run rather than about a file: that each block reached its marker, that what it
    /// printed holds every assertion made about it, and that none of it carries a path off this
    /// machine.
    /// </summary>
    internal static Ran? Execute(string directory, IReadOnlyList<Available> available, Plan plan)
    {
        var name = Path.GetFileName(directory);
        var sourcePath = Path.Combine(directory, SourceName);
        var lines = File.ReadAllLines(sourcePath);
        var blocks = Blocks.Parse(sourcePath, lines);
        Runner.Rooted(sourcePath, lines, plan);

        foreach (var file in Boss.Code(directory))
        {
            Runner.Rooted(file, File.ReadAllLines(file), plan);
        }

        var needs = Needs(sourcePath, blocks, available, plan);
        if (needs is null)
        {
            return null;
        }

        Announced(directory, name, needs, plan);

        var missing = needs.Where(c => !available.First(a => a.Configuration.Id == c.Id).Present).ToList();
        if (missing.Count > 0)
        {
            Skip(directory, name, blocks, missing, available, plan);
            return null;
        }

        // Loaded before the run, so a lesson with a broken assertions file says so in a second
        // rather than after it has built a fixture and executed a program.
        var asserts = Asserts.Load(directory, blocks);

        BuildFixture(directory);

        var captured = Blocks.Execute(directory, lines, blocks, "lesson");

        foreach (var block in blocks)
        {
            // A none block is never marked, so there is nothing of its own to look at. Every other
            // block is checked for having run at all, including the ones whose output is dropped,
            // because a dropped block that never reached its marker is still a broken lesson.
            if (block.Capture == Blocks.None)
            {
                continue;
            }

            if (!captured.TryGetValue(block.Id, out var output))
            {
                plan.Problem($"{name}: block '{block.Id}' printed no marker, so it never ran");
                continue;
            }

            if (asserts.TryGetValue(block.Id, out var asserted))
            {
                foreach (var problem in Asserts.Check(asserted, output))
                {
                    plan.Problem($"{name}/{block.Id}: {problem}");
                }
            }

            if (block.Capture == Blocks.Stdout)
            {
                Machine(directory, name, block.Id, output, plan);
            }
        }

        var answers = Boss.Has(directory) ? Boss.Execute(directory, plan) : null;

        return new Ran(directory, name, blocks, needs, asserts, captured, answers);
    }

    /// <summary>
    /// The configurations this lesson needs, in ladder order, or nothing at all if a block names
    /// one that does not exist.
    /// </summary>
    /// <remarks>
    /// A block declaring <c>env=E7</c> used to be accepted in silence and run under the stock SDK,
    /// which is how a page ends up quoting output from a runtime it says it did not use.
    /// </remarks>
    private static List<Configuration>? Needs(
        string sourcePath,
        IReadOnlyList<Block> blocks,
        IReadOnlyList<Available> available,
        Plan plan)
    {
        var declared = available.ToDictionary(a => a.Configuration.Id, a => a.Configuration, StringComparer.Ordinal);
        var needs = new List<Configuration>();
        var known = true;

        foreach (var block in blocks)
        {
            if (!declared.TryGetValue(block.Env, out var configuration))
            {
                plan.Problem($"{sourcePath}:{block.DirectiveLine + 1}: block '{block.Id}' declares env={block.Env}, which is not in {Environments.FileName}");
                known = false;
                continue;
            }

            if (!needs.Any(c => c.Id == configuration.Id))
            {
                needs.Add(configuration);
            }
        }

        if (!known)
        {
            return null;
        }

        // Ladder order rather than the order the blocks happen to be in, so the notice on the page
        // reads the same way whichever block the author wrote first.
        var order = available.Select(a => a.Configuration.Id).ToList();
        needs.Sort((a, b) => order.IndexOf(a.Id).CompareTo(order.IndexOf(b.Id)));

        return needs;
    }

    /// <summary>
    /// A lesson that costs a reader more than an SDK install has to say so on its own page.
    /// </summary>
    /// <remarks>
    /// This was a rule in CONTRIBUTING enforced by somebody remembering it in review. Checked here
    /// rather than at assemble time, because the check has to happen on machines that are skipping
    /// the lesson too, and a skipped lesson never reaches assemble.
    /// </remarks>
    private static void Announced(string directory, string name, List<Configuration> needs, Plan plan)
    {
        var prosePath = Path.Combine(directory, ProseName);
        if (!File.Exists(prosePath))
        {
            return;
        }

        var prose = File.ReadAllText(prosePath);

        // The front matter names one configuration for the whole lesson, which is the most
        // expensive thing any of its blocks asks for. Two places saying what a lesson needs is
        // fine as long as one of them is checked against the other.
        var top = needs[^1].Id;
        var stated = FrontMatter(prose, "env");

        if (stated is null)
        {
            plan.Problem($"{name}/{ProseName}: no env in the front matter, so the page does not say what it costs to follow");
        }
        else if (stated != top)
        {
            plan.Problem($"{name}/{ProseName}: the front matter says env: {stated} and the most a block in this lesson asks for is {top}");
        }

        if (top != Environments.Stock && !prose.Contains("{{needs}}", StringComparison.Ordinal))
        {
            plan.Problem($"{name}/{ProseName}: this lesson needs {top} and the page never says so, put {{{{needs}}}} in the prose near the top");
        }
    }

    /// <summary>One field out of the front matter, or nothing if the page does not have it.</summary>
    private static string? FrontMatter(string prose, string key)
    {
        var lines = prose.Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return null;
        }

        foreach (var line in lines.Skip(1).TakeWhile(l => l.Trim() != "---"))
        {
            if (line.StartsWith(key + ":", StringComparison.Ordinal))
            {
                return line[(key.Length + 1)..].Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Leaves out a lesson this machine cannot run, and insists that what is already committed for
    /// it is complete.
    /// </summary>
    /// <remarks>
    /// Without the second half, a missing environment would be a way of getting a green build for
    /// a lesson that has never been run anywhere. The skip is allowed to mean "somebody else built
    /// this and it is on disk". It is not allowed to mean "nobody has ever built this".
    /// </remarks>
    private static void Skip(
        string directory,
        string name,
        IReadOnlyList<Block> blocks,
        List<Configuration> missing,
        IReadOnlyList<Available> available,
        Plan plan)
    {
        var why = missing.Select(c => available.First(a => a.Configuration.Id == c.Id).Why);
        Plan.Note($"{name} needs {string.Join(" and ", missing.Select(c => c.Id))}, not run here: {string.Join("; ", why)}");

        var wanted = blocks
            .Where(b => b.Capture == Blocks.Stdout)
            .Select(b => Path.Combine(directory, ExpectedDirectory, b.Id + ".txt"))
            .ToList();

        if (File.Exists(Path.Combine(directory, ProseName)))
        {
            wanted.Add(Path.Combine(directory, PageName));
        }

        foreach (var file in wanted.Where(f => !File.Exists(f)))
        {
            plan.Problem($"{Path.GetRelativePath(Directory.GetCurrentDirectory(), file)}: this lesson was skipped because this machine has no {string.Join(" or ", missing.Select(c => c.Id))}, and it has no committed copy of this file either, so nothing has ever produced it");
        }
    }

    /// <summary>
    /// Step four. One file per block whose output the page is allowed to quote, plus the boss
    /// fight's answer file.
    /// </summary>
    internal static int Generate(Ran lesson, Plan plan)
    {
        var count = 0;

        foreach (var block in lesson.Blocks)
        {
            if (block.Capture != Blocks.Stdout || !lesson.Captured.TryGetValue(block.Id, out var output))
            {
                continue;
            }

            plan.Add(Path.Combine(lesson.Directory, ExpectedDirectory, block.Id + ".txt"), output);
            count++;
        }

        if (lesson.BossAnswers is not null)
        {
            plan.Add(Path.Combine(lesson.Directory, Boss.Directory, Boss.Answers), lesson.BossAnswers);
        }

        return count;
    }

    /// <summary>
    /// Step five. Fills every hole in the prose and gives the plan the page.
    /// </summary>
    internal static int Assemble(Ran lesson, Plan plan)
    {
        var prosePath = Path.Combine(lesson.Directory, ProseName);
        if (!File.Exists(prosePath))
        {
            return 0;
        }

        plan.Add(Path.Combine(lesson.Directory, PageName), Render(lesson, prosePath));
        return 1;
    }

    /// <summary>
    /// Builds the lesson's fixture, if it has one. A fixture is a tiny project the lesson reads
    /// rather than a binary committed to the repository, because a committed binary is a number
    /// typed by a human in the only format nobody can review.
    /// </summary>
    internal static void BuildFixture(string directory)
    {
        var fixture = Path.Combine(directory, "fixture");
        if (!Directory.Exists(fixture) || !Directory.EnumerateFiles(fixture, "*.csproj").Any())
        {
            return;
        }

        var (exit, _, stderr) = Runner.Dotnet(directory, ["build", fixture, "--configuration", "Release", "--nologo", "--verbosity", "quiet"]);
        if (exit != 0)
        {
            throw new LessonException($"{Path.GetFileName(directory)}: the fixture did not build\n{stderr}");
        }
    }

    /// <summary>
    /// Rejects captured output that carries something about the machine that produced it. A path
    /// out of somebody's home directory in an expected file is a file that can only ever match on
    /// one laptop, and it fails on the fourth platform instead of in review.
    /// </summary>
    internal static void Machine(string directory, string name, string id, string output, Plan plan)
    {
        foreach (var secret in Leaks(directory))
        {
            if (secret.Length > 0 && output.Contains(secret, StringComparison.OrdinalIgnoreCase))
            {
                plan.Problem($"{name}/{id}: output contains a path from this machine, print a file name rather than a full path");
                return;
            }
        }
    }

    private static IEnumerable<string> Leaks(string directory)
    {
        yield return directory;
        yield return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string Render(Ran lesson, string prosePath)
    {
        var byId = lesson.Blocks.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var gates = Gates.Load(lesson.Directory);
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

            // Most holes name a thing, as in {{block:layout}}. The boss fight does not, because a
            // lesson has one of those and naming it would only give somebody a second name for it
            // to disagree with.
            var kind = colon < 0 ? inner : inner[..colon];
            var id = colon < 0 ? string.Empty : inner[(colon + 1)..];

            if (kind.Length == 0)
            {
                throw new LessonException($"{prosePath}: '{trimmed}' is not a transclusion");
            }

            rendered.Append(Transclude(lesson, prosePath, kind, id, byId, gates)).Append('\n');
        }

        return rendered.ToString().TrimEnd('\n') + "\n";
    }

    private static string Transclude(
        Ran lesson,
        string prosePath,
        string kind,
        string id,
        Dictionary<string, Block> blocks,
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

                if (!lesson.Captured.TryGetValue(id, out var output))
                {
                    throw new LessonException($"{prosePath}: block '{id}' has no captured output");
                }

                return "```text\n" + (output.Length == 0 ? "(the block printed nothing)\n" : output) + "```";

            case "asserts":
                if (!blocks.TryGetValue(id, out var promised))
                {
                    throw new LessonException($"{prosePath}: no block named '{id}' in {SourceName}");
                }

                if (!lesson.Asserts.TryGetValue(id, out var claims))
                {
                    throw new LessonException($"{prosePath}: block '{id}' has nothing asserted about it in {Asserts.FileName}");
                }

                return Asserts.Render(claims, promised);

            case "gate":
                if (!gates.TryGetValue(id, out var gate))
                {
                    throw new LessonException($"{prosePath}: no gate named '{id}' in gates.json");
                }

                return Gates.Render(gate);

            case "needs":
                return Environments.Notice(lesson.Needs, Files.Root(lesson.Directory), lesson.Directory);

            case "boss":
                if (!Boss.Has(lesson.Directory))
                {
                    throw new LessonException($"{prosePath}: the page asks for a boss fight and there is no {Boss.Directory} directory");
                }

                return Boss.Render(lesson.Directory, Boss.Load(lesson.Directory));

            default:
                throw new LessonException($"{prosePath}: '{kind}' is not a kind of transclusion");
        }
    }
}
