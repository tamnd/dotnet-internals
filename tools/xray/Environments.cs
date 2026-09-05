using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClrXray;

/// <summary>How the tool works out whether one configuration is present on this machine.</summary>
internal sealed class Detect
{
    /// <summary>One of <c>always</c>, <c>beside-the-runtime</c> or <c>pointed-at</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>For <c>beside-the-runtime</c>, the file that has to be sitting next to the shared framework.</summary>
    public string? File { get; init; }

    /// <summary>The environment variable that points at this configuration when it is somewhere else.</summary>
    public string? Variable { get; init; }
}

/// <summary>
/// One configuration a lesson is allowed to need, as declared in <c>environments.json</c>.
/// </summary>
internal sealed class Configuration
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>What the configuration actually is, in a sentence a reader can act on.</summary>
    public string What { get; init; } = string.Empty;

    /// <summary>What it costs to get, which is the number that decides whether a reader bothers.</summary>
    public string Cost { get; init; } = string.Empty;

    /// <summary>The document that says how to get it. Checked, so it cannot point at a deleted file.</summary>
    public string How { get; init; } = string.Empty;

    public Detect Here { get; init; } = new();
}

/// <summary>Whether one configuration is on this machine, and how the tool decided.</summary>
internal sealed record Available(Configuration Configuration, bool Present, string Why);

/// <summary>
/// The configurations a lesson is allowed to need, and which of them this machine has.
/// </summary>
/// <remarks>
/// <para>
/// Every block in every lesson has always carried an <c>env=</c> attribute. Until now the tool
/// parsed it, stored it and did nothing else with it, which meant every block declaring
/// <c>E0</c> was a claim nobody had checked. The day somebody wrote <c>env=E1</c> the block would
/// have run under <c>E0</c> anyway and the page would have pinned output from the wrong runtime
/// while saying it came from the right one. That is the worst kind of wrong this project can be,
/// because it is wrong in the direction of looking correct.
/// </para>
/// <para>
/// So the list lives in a file the tool reads rather than in a sentence in a document, a block
/// naming an id that is not in the list fails the build, a lesson needing a configuration this
/// machine does not have is skipped by name and not regenerated from a run that did not happen,
/// and a lesson that needs anything above <c>E0</c> has to say so on its own page.
/// </para>
/// <para>
/// The order is the cost order, cheapest first, because the number that matters to a reader
/// deciding whether to follow a lesson is what it will cost them to follow it.
/// </para>
/// </remarks>
internal static class Environments
{
    internal const string FileName = "environments.json";
    internal const string Stock = "E0";

    private const string Always = "always";
    private const string Beside = "beside-the-runtime";
    private const string PointedAt = "pointed-at";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Reads the declaration and refuses anything that would make it unusable as a contract.
    /// </summary>
    /// <remarks>
    /// The ids have to run from <c>E0</c> upward with no gaps, because the whole point of a
    /// numbered ladder is that a reader can tell from the number roughly what a lesson is going to
    /// ask of them. A list with <c>E0</c> and <c>E3</c> in it has stopped being a ladder.
    /// </remarks>
    internal static IReadOnlyList<Configuration> Load(string root)
    {
        var path = Path.Combine(root, FileName);

        if (!System.IO.File.Exists(path))
        {
            throw new LessonException($"no {FileName} at {root}, so there is no list of what a lesson is allowed to need");
        }

        var declaration = JsonSerializer.Deserialize<Declaration>(System.IO.File.ReadAllText(path), Options);
        var configurations = declaration?.Environments ?? [];

        if (configurations.Count == 0)
        {
            throw new LessonException($"{FileName}: no environments in here, so every block in the book declares something undeclared");
        }

        for (var i = 0; i < configurations.Count; i++)
        {
            var configuration = configurations[i];
            var wanted = "E" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (configuration.Id != wanted)
            {
                throw new LessonException($"{FileName}: entry {i + 1} is '{configuration.Id}' where the ladder wants '{wanted}', and a ladder with a rung missing is not one");
            }

            Says(configuration, configuration.Name, "name");
            Says(configuration, configuration.What, "what");
            Says(configuration, configuration.Cost, "cost");
            Says(configuration, configuration.How, "how");

            if (!System.IO.File.Exists(Path.Combine(root, configuration.How)))
            {
                throw new LessonException($"{FileName}: {configuration.Id} says the way to get it is written in {configuration.How}, and there is no such file");
            }

            Detects(configuration);
        }

        return configurations;
    }

    /// <summary>
    /// Works out which of the declared configurations this machine actually has.
    /// </summary>
    internal static IReadOnlyList<Available> Detect(IReadOnlyList<Configuration> configurations) =>
        configurations.Select(Look).ToList();

    /// <summary>
    /// The one line the resolve step prints, which is the line that decides whether the numbers
    /// further down the log are the numbers a reader would get.
    /// </summary>
    internal static string Describe(IReadOnlyList<Available> available)
    {
        var here = available.Where(a => a.Present).Select(a => a.Configuration.Id).ToList();
        var missing = available.Where(a => !a.Present).Select(a => a.Configuration.Id).ToList();

        return missing.Count == 0
            ? $"{string.Join(" ", here)} all here"
            : $"{string.Join(" ", here)} here, {string.Join(" ", missing)} not";
    }

    /// <summary>
    /// The notice a lesson needing more than the stock SDK puts on its own page.
    /// </summary>
    /// <remarks>
    /// This is the contributing rule that used to be enforced by somebody remembering it in
    /// review: a lesson that needs a runtime build has to say so, in bold, with what it costs.
    /// Generating the notice from the same file the build checks against means the page cannot
    /// promise one configuration while the code runs in another.
    /// </remarks>
    internal static string Notice(IReadOnlyList<Configuration> needed, string root, string directory)
    {
        var lines = needed
            .Where(c => c.Id != Stock)
            .Select(c => $"**This lesson needs {c.Id}, {c.Name}.** {c.What} {c.Cost} How to get it is in [{c.How}]({Link(root, directory, c.How)}).")
            .ToList();

        return lines.Count == 0
            ? "This lesson runs on the stock SDK. Nothing to install beyond the one in the README."
            : string.Join("\n\n", lines);
    }

    /// <summary>
    /// A link from a lesson page to a document at the top of the repository, written the way a
    /// link in a markdown file has to be written on every platform.
    /// </summary>
    private static string Link(string root, string directory, string how) =>
        Path.GetRelativePath(directory, Path.Combine(root, how)).Replace('\\', '/');

    /// <summary>
    /// The <c>env</c> command. Prints what this machine has, and optionally insists on one.
    /// </summary>
    internal static int Run(string path, string? required)
    {
        IReadOnlyList<Available> available;

        try
        {
            available = Detect(Load(Files.Root(path)));
        }
        catch (LessonException error)
        {
            Console.Error.WriteLine($"xray: {error.Message}");
            return 2;
        }

        Console.WriteLine($"{Banner.Framework} on {Banner.Platform}");
        Console.WriteLine();

        foreach (var entry in available)
        {
            Console.WriteLine($"  {entry.Configuration.Id}  {(entry.Present ? "here    " : "not here")}  {entry.Configuration.Name}");
            Console.WriteLine($"        {entry.Why}");
            Console.WriteLine($"        {entry.Configuration.Cost} See {entry.Configuration.How}.");
            Console.WriteLine();
        }

        if (required is null)
        {
            return 0;
        }

        var wanted = available.FirstOrDefault(a => a.Configuration.Id == required);

        if (wanted is null)
        {
            Console.Error.WriteLine($"xray env: nothing called '{required}' is declared in {FileName}");
            return 2;
        }

        if (!wanted.Present)
        {
            // CI asserts what it is supposed to have rather than discovering it, because a job
            // that quietly loses an environment goes green by skipping the lessons that used it.
            Console.Error.WriteLine($"xray env: this machine was required to have {required} and does not, because {wanted.Why}");
            return 1;
        }

        Console.WriteLine($"xray env: {required} is here, as required");
        return 0;
    }

    private static Available Look(Configuration configuration)
    {
        switch (configuration.Here.Kind)
        {
            case Always:
                return new Available(configuration, true, "this is the SDK the tool is running on, so it is here by definition");

            case Beside:
                {
                    var directory = RuntimeEnvironment.GetRuntimeDirectory();
                    var beside = Path.Combine(directory, configuration.Here.File!);

                    if (System.IO.File.Exists(beside))
                    {
                        return new Available(configuration, true, $"{configuration.Here.File} is beside the runtime at {directory}");
                    }

                    var named = Environment.GetEnvironmentVariable(configuration.Here.Variable!);

                    if (!string.IsNullOrWhiteSpace(named) && System.IO.File.Exists(named))
                    {
                        return new Available(configuration, true, $"{configuration.Here.Variable} points at {named}");
                    }

                    return new Available(
                        configuration,
                        false,
                        $"there is no {configuration.Here.File} beside the runtime at {directory}, and {configuration.Here.Variable} does not name a file that exists");
                }

            case PointedAt:
                {
                    var named = Environment.GetEnvironmentVariable(configuration.Here.Variable!);

                    if (!string.IsNullOrWhiteSpace(named) && Directory.Exists(named))
                    {
                        return new Available(configuration, true, $"{configuration.Here.Variable} points at {named}");
                    }

                    return new Available(
                        configuration,
                        false,
                        $"{configuration.Here.Variable} does not name a directory that exists, and nothing else can tell the tool where a built runtime is");
                }

            default:
                throw new LessonException($"{FileName}: {configuration.Id} is detected by '{configuration.Here.Kind}', which is not a way of detecting anything");
        }
    }

    private static void Says(Configuration configuration, string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new LessonException($"{FileName}: {configuration.Id} has no {field}, and a configuration nobody has described is one nobody can decide to install");
        }
    }

    private static void Detects(Configuration configuration)
    {
        switch (configuration.Here.Kind)
        {
            case Always:
                return;

            case Beside when string.IsNullOrWhiteSpace(configuration.Here.File) || string.IsNullOrWhiteSpace(configuration.Here.Variable):
                throw new LessonException($"{FileName}: {configuration.Id} is found beside the runtime, so it needs both the file to look for and the variable that overrides it");

            case PointedAt when string.IsNullOrWhiteSpace(configuration.Here.Variable):
                throw new LessonException($"{FileName}: {configuration.Id} is found by being pointed at, and nothing says which variable does the pointing");

            case Beside:
            case PointedAt:
                return;

            default:
                throw new LessonException($"{FileName}: {configuration.Id} is detected by '{configuration.Here.Kind}', and the three ways the tool knows are {Always}, {Beside} and {PointedAt}");
        }
    }

    private sealed class Declaration
    {
        [JsonPropertyName("environments")]
        public List<Configuration> Environments { get; init; } = [];
    }
}
