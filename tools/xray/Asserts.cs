using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClrXray;

/// <summary>
/// One thing that is true of what a block printed, on every machine that runs it.
/// </summary>
/// <remarks>
/// Exactly one of the four rules is set. The <see cref="Why"/> is not optional, because it is the
/// sentence a reader gets on the page and the sentence whoever broke it gets in the build log, and
/// an invariant nobody can explain is one somebody will delete the first time it goes red.
/// </remarks>
internal sealed class Invariant
{
    /// <summary>The output has this text in it somewhere.</summary>
    public string? Contains { get; init; }

    /// <summary>The output does not have this text in it anywhere.</summary>
    public string? Absent { get; init; }

    /// <summary>Some line of the output matches this regular expression.</summary>
    public string? Matches { get; init; }

    /// <summary>The output is exactly this many lines.</summary>
    public int? Lines { get; init; }

    public string Why { get; init; } = string.Empty;
}

/// <summary>Everything asserted about one block.</summary>
internal sealed class Asserted
{
    public string Block { get; init; } = string.Empty;

    public IReadOnlyList<Invariant> Claims { get; init; } = [];
}

/// <summary>
/// What a lesson guarantees about output it cannot put on the page.
/// </summary>
/// <remarks>
/// <para>
/// A <c>capture=stdout</c> block has its output pinned byte for byte in an expected file, so the
/// build catches any change to it. A <c>capture=drop</c> block had nothing at all. It ran, it
/// printed a position or a timing or a fresh identifier, the output went in the bin, and the page
/// underneath it said whatever the author remembered seeing once. That is the one place in a
/// lesson where a program could print anything and nobody would find out.
/// </para>
/// <para>
/// An assertion closes it. The author says what is true of the output whatever machine produced
/// it, the build checks it on all four platforms, and the page prints the list. So a reader who
/// cannot be shown the output can still be told exactly what is being claimed about it, and can
/// see that the claim is checked rather than remembered.
/// </para>
/// <para>
/// Every drop block needs at least one, and that rule has no way out of it. A drop block with no
/// assertion is the hole this file exists to close.
/// </para>
/// </remarks>
internal static class Asserts
{
    internal const string FileName = "asserts.json";

    /// <summary>
    /// A pattern that backtracks forever is a build that hangs rather than one that fails, and the
    /// difference matters most on the platform nobody is watching.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Runs every lesson under a path and reports its assertions one at a time, passing ones
    /// included.
    /// </summary>
    /// <remarks>
    /// <c>build</c> and <c>check</c> evaluate the same assertions and only mention the failures,
    /// which is right for a gate and unhelpful for the half hour you are writing one. This command
    /// prints all of them, so you can see that the rule you just wrote is testing what you meant
    /// rather than passing because it is vacuous.
    /// </remarks>
    internal static int Run(string root)
    {
        var lessons = Lessons.Discover(root);
        if (lessons.Count == 0)
        {
            Console.Error.WriteLine($"xray assert: nothing to check under {root}, a lesson is a directory holding {Lessons.SourceName}");
            return 2;
        }

        var problems = 0;
        var counted = 0;

        foreach (var lesson in lessons)
        {
            try
            {
                counted += One(lesson, ref problems);
            }
            catch (LessonException error)
            {
                Console.Error.WriteLine($"xray: {error.Message}");
                problems++;
            }
        }

        Console.WriteLine($"xray assert: {counted} assertion(s), {problems} problem(s)");
        return problems == 0 ? 0 : 1;
    }

    private static int One(string directory, ref int problems)
    {
        var name = Path.GetFileName(directory);
        var sourcePath = Path.Combine(directory, Lessons.SourceName);
        var lines = File.ReadAllLines(sourcePath);
        var blocks = Blocks.Parse(sourcePath, lines);
        var asserts = Load(directory, blocks);

        if (asserts.Count == 0)
        {
            return 0;
        }

        Lessons.BuildFixture(directory);
        var captured = Blocks.Execute(directory, lines, blocks, "lesson");
        var counted = 0;

        foreach (var block in blocks)
        {
            if (!asserts.TryGetValue(block.Id, out var asserted))
            {
                continue;
            }

            var output = captured.TryGetValue(block.Id, out var printed) ? printed : string.Empty;

            foreach (var claim in asserted.Claims)
            {
                counted++;
                var broke = Fails(claim, output);
                if (broke is null)
                {
                    Console.WriteLine($"  ok    {name}/{block.Id}: {Describe(claim)}");
                    continue;
                }

                Console.Error.WriteLine($"  FAIL  {name}/{block.Id}: {Describe(claim)} It {broke}.");
                problems++;
            }
        }

        return counted;
    }

    /// <summary>
    /// Reads <c>asserts.json</c> from a lesson directory, checks it against the blocks that lesson
    /// actually declares, and refuses a drop block that nothing asserts anything about.
    /// </summary>
    internal static IReadOnlyDictionary<string, Asserted> Load(string directory, IReadOnlyList<Block> blocks)
    {
        var path = Path.Combine(directory, FileName);
        var byBlock = new Dictionary<string, Asserted>(StringComparer.Ordinal);
        var byId = blocks.ToDictionary(b => b.Id, StringComparer.Ordinal);

        if (File.Exists(path))
        {
            var declared = Read(path);

            foreach (var asserted in declared)
            {
                if (string.IsNullOrEmpty(asserted.Block))
                {
                    throw new LessonException($"{path}: an entry does not say which block it is about");
                }

                if (!byId.TryGetValue(asserted.Block, out var block))
                {
                    throw new LessonException($"{path}: no block named '{asserted.Block}' in {Lessons.SourceName}");
                }

                // A none block is never marked, so whatever it prints lands in the block above it.
                // Asserting on one would be asserting on somebody else's output.
                if (block.Capture == Blocks.None)
                {
                    throw new LessonException($"{path}: block '{asserted.Block}' is capture=none, so its output is not separated from the block before it and there is nothing here to assert on");
                }

                if (asserted.Claims.Count == 0)
                {
                    throw new LessonException($"{path}: block '{asserted.Block}' is listed with no assertions, so either write one or take the entry out");
                }

                foreach (var claim in asserted.Claims)
                {
                    Validate(path, asserted.Block, claim);
                }

                if (!byBlock.TryAdd(asserted.Block, asserted))
                {
                    throw new LessonException($"{path}: block '{asserted.Block}' is listed twice");
                }
            }
        }

        // The rule with teeth, and the reason this file exists. Nothing else in the build looks at
        // what a drop block printed.
        foreach (var block in blocks)
        {
            if (block.Capture == Blocks.Drop && !byBlock.ContainsKey(block.Id))
            {
                throw new LessonException($"{Path.GetFileName(directory)}: block '{block.Id}' is capture=drop, so nothing checks what it prints, say what is true of it in {FileName}");
            }
        }

        return byBlock;
    }

    private static List<Asserted> Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<List<Asserted>>(File.ReadAllText(path), Options)
                ?? throw new LessonException($"{path}: not a list of assertions");
        }
        catch (JsonException error)
        {
            throw new LessonException($"{path}: {error.Message}", error);
        }
    }

    private static void Validate(string path, string block, Invariant claim)
    {
        var kinds = new[] { claim.Contains is not null, claim.Absent is not null, claim.Matches is not null, claim.Lines is not null }
            .Count(set => set);

        if (kinds != 1)
        {
            throw new LessonException($"{path}: an assertion on '{block}' sets {kinds} of contains, absent, matches and lines, and it has to set exactly one");
        }

        if (claim.Why.Trim().Length == 0)
        {
            throw new LessonException($"{path}: an assertion on '{block}' has no why, and the why is what a reader sees on the page");
        }

        if (claim.Lines is < 0)
        {
            throw new LessonException($"{path}: an assertion on '{block}' asks for a negative number of lines");
        }

        // A bad pattern should fail here, once, with the file named, rather than in the middle of
        // a run on whichever platform got there first.
        if (claim.Matches is not null)
        {
            try
            {
                _ = Pattern(claim.Matches);
            }
            catch (ArgumentException error)
            {
                throw new LessonException($"{path}: the pattern '{claim.Matches}' on '{block}' is not a regular expression: {error.Message}", error);
            }
        }
    }

    /// <summary>
    /// Every assertion on one block that does not hold, as a sentence each. Given back rather than
    /// printed, so the self test can read the messages as well as count them.
    /// </summary>
    internal static List<string> Check(Asserted asserted, string output)
    {
        var problems = new List<string>();

        foreach (var claim in asserted.Claims)
        {
            var broke = Fails(claim, output);
            if (broke is not null)
            {
                problems.Add($"{Describe(claim)} It {broke}. {claim.Why}");
            }
        }

        return problems;
    }

    /// <summary>
    /// How this assertion is broken, or null if it is not.
    /// </summary>
    internal static string? Fails(Invariant claim, string output)
    {
        try
        {
            return claim switch
            {
                { Lines: int wanted } when Count(output) != wanted => $"printed {Count(output).ToString(CultureInfo.InvariantCulture)} line(s)",
                { Contains: string text } when !output.Contains(text, StringComparison.Ordinal) => "does not",
                { Absent: string text } when output.Contains(text, StringComparison.Ordinal) => "does",
                { Matches: string pattern } when !Pattern(pattern).IsMatch(output) => "has no line that does",
                _ => null,
            };
        }
        catch (RegexMatchTimeoutException)
        {
            return $"took longer than {Patience.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds to match, which means the pattern backtracks and needs rewriting";
        }
    }

    /// <summary>
    /// The assertion in one short sentence, used on the page and in the failure, so the two say
    /// the same thing in the same words.
    /// </summary>
    internal static string Describe(Invariant claim) => claim switch
    {
        { Lines: 1 } => "Exactly one line.",
        { Lines: int wanted } => $"Exactly {wanted.ToString(CultureInfo.InvariantCulture)} lines.",
        { Contains: string text } => $"Contains `{text}`.",
        { Absent: string text } => $"Does not contain `{text}`.",
        { Matches: string pattern } => $"A line matches `{pattern}`.",
        _ => throw new LessonException("an assertion with no rule in it"),
    };

    /// <summary>
    /// Writes the assertions into the page, so that a reader looking at a block whose output is
    /// not there is told what is being claimed about it and that the claim is checked.
    /// </summary>
    internal static string Render(Asserted asserted, Block block)
    {
        var text = new StringBuilder();

        text.Append(block.Capture == Blocks.Drop
            ? "**Checked, though the output is not on this page.** That block prints something different on every machine, so nothing is stored and nothing can be quoted. These are the things that are true of it everywhere, and the build fails on any platform where one of them stops being true.\n\n"
            : "**Checked, on top of the output above.** The output is pinned byte for byte, so any change to it fails the build. These are the parts of it that are not incidental, written down so that a change to them fails with a reason rather than as a diff.\n\n");

        foreach (var claim in asserted.Claims)
        {
            text.Append("- ").Append(Describe(claim)).Append(' ').Append(claim.Why.Trim()).Append('\n');
        }

        return text.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// Multiline, so that <c>^</c> and <c>$</c> mean the ends of a line rather than the ends of
    /// the whole output, which is what somebody writing a rule about a table of output expects.
    /// </summary>
    private static Regex Pattern(string pattern) => new(pattern, RegexOptions.Multiline, Patience);

    private static int Count(string output)
    {
        var trimmed = output.TrimEnd('\n');
        return trimmed.Length == 0 ? 0 : trimmed.Split('\n').Length;
    }
}
