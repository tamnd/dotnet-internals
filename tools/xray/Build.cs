namespace ClrXray;

/// <summary>
/// One file the build is going to produce, and what it is going to contain.
/// </summary>
internal sealed record Artifact(string Path, string Content);

/// <summary>
/// What a build has decided to produce, and what has gone wrong so far.
/// </summary>
/// <remarks>
/// Nothing is written while the plan is being filled in. Every generated file in the repository
/// arrives on disk in the last step of the build and nowhere else, which is what makes the
/// difference between <c>build</c> and <c>check</c> one flag in one place rather than a decision
/// repeated in every generator.
/// </remarks>
internal sealed class Plan
{
    private readonly List<Artifact> artifacts = [];
    private readonly HashSet<string> claimed = new(StringComparer.Ordinal);

    internal int Problems { get; private set; }

    internal void Add(string path, string content)
    {
        var full = System.IO.Path.GetFullPath(path);

        // Two generators writing the same file is a bug with no symptom: whichever one runs second
        // wins, the build is green, and half the work quietly does not appear on the page.
        if (!claimed.Add(full))
        {
            Problem($"{full}: two different things in this build both want to produce this file");
            return;
        }

        artifacts.Add(new Artifact(full, content));
    }

    internal void Problem(string message)
    {
        Console.Error.WriteLine(message);
        Problems++;
    }

    /// <summary>
    /// The last step. Writes every artifact, or reports every one that differs from what is
    /// committed.
    /// </summary>
    internal (int Files, int Written, int Removed) Settle(bool write)
    {
        var written = 0;

        foreach (var artifact in artifacts.OrderBy(a => a.Path, StringComparer.Ordinal))
        {
            switch (Generated.Settle(artifact.Path, artifact.Content, write))
            {
                case Settled.Written:
                    written++;
                    break;
                case Settled.Wrong:
                    Problems++;
                    break;
                default:
                    break;
            }
        }

        return (artifacts.Count, written, Stale(write));
    }

    /// <summary>
    /// Finds generated files nothing in this build produces any more.
    /// </summary>
    /// <remarks>
    /// Rename a block and its old captured output stays on disk forever. Nothing reads it, nothing
    /// compares it, and the next person to open the directory has two files where one of them is
    /// the truth. Only directories this build put something into are looked at, so a lesson that
    /// has never been built is not accused of having leftovers.
    /// </remarks>
    private int Stale(bool write)
    {
        string[] swept = ["expected", "generated"];
        var removed = 0;

        var directories = artifacts
            .Select(a => System.IO.Path.GetDirectoryName(a.Path)!)
            .Where(d => swept.Contains(System.IO.Path.GetFileName(d), StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
            {
                if (claimed.Contains(System.IO.Path.GetFullPath(file)))
                {
                    continue;
                }

                var relative = System.IO.Path.GetRelativePath(Directory.GetCurrentDirectory(), file);

                if (write)
                {
                    File.Delete(file);
                    Console.WriteLine($"  removed {relative}, which nothing produces any more");
                    removed++;
                    continue;
                }

                Problem($"{relative}: nothing in this build produces this file, so it is left over from a block that has been renamed or deleted");
            }
        }

        return removed;
    }
}

/// <summary>
/// The build, which is six named steps in a fixed order.
/// </summary>
/// <remarks>
/// <para>
/// The six are resolve, cite, execute, generate, assemble and check. They are named because a
/// build that is one opaque operation is a build nobody can say anything true about. With names,
/// the failure has a place: a red execute is a lesson whose code is wrong, a red check is a lesson
/// whose code is right and whose committed page is out of date, and those two need completely
/// different things done about them.
/// </para>
/// <para>
/// A step that finds a problem stops the build. This is not tidiness. Every step reads what the
/// step before it produced, so the output of a step running on top of a failed one is not a second
/// opinion, it is noise with a line number in it. The one exception is inside a step: all the
/// lessons execute even after the first one fails, because those really are independent and
/// finding out about three broken lessons at once is worth a longer log.
/// </para>
/// <para>
/// <c>build</c> and <c>check</c> are the same six steps with one flag flipped. Only the last one
/// touches the disk.
/// </para>
/// </remarks>
internal static class Build
{
    internal static int Run(string path, bool write, bool offline)
    {
        var lessons = Lessons.Discover(path);
        var blueprints = Blueprints.Discover(path);
        var diagrams = Diagrams.Discover(path);

        if (lessons.Count == 0 && blueprints.Count == 0 && diagrams.Count == 0)
        {
            Console.Error.WriteLine($"xray: nothing to build under {path}, which would hold a lesson, a blueprint or a diagram source");
            return 2;
        }

        var plan = new Plan();
        var verb = write ? "build" : "check";
        Toolchain? toolchain = null;
        var ran = new List<Ran>();
        var made = new List<Made>();

        // The five that produce something, and then the one that puts it on disk. Written this way
        // rather than as six calls in a row because the short circuit is the rule: a step that
        // finds a problem is the last step that runs.
        var reached =
            Step("resolve", plan, () =>
            {
                toolchain = Resolve.Run(path, plan);
                return Resolve.Describe(toolchain);
            })
            && Step("cite", plan, () => Citations(path, plan, offline))
            && Step("execute", plan, () => Execute(lessons, blueprints, plan, ran, made))
            && Step("generate", plan, () => Generate(diagrams, ran, made, plan))
            && Step("assemble", plan, () => Assemble(ran, made, plan));

        if (reached)
        {
            Step("check", plan, () => Settle(plan, write));
        }

        Console.WriteLine($"xray {verb}: {plan.Problems} problem(s)" + (reached ? string.Empty : ", and the steps after that one did not run"));
        return plan.Problems == 0 ? 0 : 1;
    }

    /// <summary>
    /// Runs one step, says what it did, and says whether the build carries on.
    /// </summary>
    private static bool Step(string name, Plan plan, Func<string> work)
    {
        var before = plan.Problems;
        string summary;

        try
        {
            summary = work();
        }
        catch (LessonException error)
        {
            plan.Problem($"xray: {error.Message}");
            summary = "stopped here";
        }
        catch (CiteException error)
        {
            plan.Problem($"xray: {error.Message}");
            summary = "stopped here";
        }

        var found = plan.Problems - before;
        Console.WriteLine($"  {name,-9} {summary}" + (found == 0 ? string.Empty : $"  [{found} problem(s)]"));
        return found == 0;
    }

    private static string Citations(string path, Plan plan, bool offline)
    {
        if (offline)
        {
            // Said out loud rather than skipped quietly, because a check that did not run and a
            // check that passed have to look different or the flag becomes a way of being green.
            return "not checked, because --offline was passed and every citation needs the network";
        }

        var (count, errors) = Cite.Verify(path, verbose: false);

        foreach (var error in errors.Order(StringComparer.Ordinal))
        {
            plan.Problem(error);
        }

        return count == 0
            ? $"none yet, because {Resolve.PinName} holds a null commit and a citation without one is not accepted"
            : $"{count} citation(s) resolved";
    }

    private static string Execute(List<string> lessons, List<string> blueprints, Plan plan, List<Ran> ran, List<Made> made)
    {
        foreach (var lesson in lessons)
        {
            try
            {
                ran.Add(Lessons.Execute(lesson, plan));
            }
            catch (LessonException error)
            {
                plan.Problem($"xray: {error.Message}");
            }
        }

        foreach (var blueprint in blueprints)
        {
            try
            {
                made.Add(Blueprints.Execute(blueprint, plan));
            }
            catch (LessonException error)
            {
                plan.Problem($"xray: {error.Message}");
            }
        }

        var blocks = ran.Sum(r => r.Blocks.Count) + made.Sum(m => m.Blocks.Count);
        var claims = ran.Sum(r => r.Asserts.Values.Sum(a => a.Claims.Count));

        return $"{lessons.Count} lesson(s) and {blueprints.Count} blueprint(s), {blocks} block(s) run, {claims} assertion(s) held";
    }

    private static string Generate(List<string> diagrams, List<Ran> ran, List<Made> made, Plan plan)
    {
        foreach (var source in diagrams)
        {
            try
            {
                Diagrams.Generate(source, plan);
            }
            catch (LessonException error)
            {
                plan.Problem($"xray: {error.Message}");
            }
        }

        var outputs = 0;
        foreach (var lesson in ran)
        {
            outputs += Lessons.Generate(lesson, plan);
        }

        var sections = 0;
        foreach (var blueprint in made)
        {
            sections += Blueprints.Generate(blueprint, plan);
        }

        return $"{diagrams.Count} diagram(s), {outputs} captured output(s), {sections} generated section(s)";
    }

    private static string Assemble(List<Ran> ran, List<Made> made, Plan plan)
    {
        var pages = 0;
        foreach (var lesson in ran)
        {
            pages += Lessons.Assemble(lesson, plan);
        }

        foreach (var blueprint in made)
        {
            pages += Blueprints.Assemble(blueprint, plan);
        }

        return $"{pages} page(s)";
    }

    private static string Settle(Plan plan, bool write)
    {
        var before = plan.Problems;
        var (files, written, removed) = plan.Settle(write);

        if (write)
        {
            return (written, removed) switch
            {
                (0, 0) => $"{files} file(s), nothing needed rewriting",
                (_, 0) => $"{files} file(s), {written} rewritten",
                (0, _) => $"{files} file(s), {removed} removed",
                _ => $"{files} file(s), {written} rewritten and {removed} removed",
            };
        }

        return plan.Problems > before
            ? $"{files} file(s) compared"
            : $"{files} file(s), and what is committed is what the code produces";
    }
}
