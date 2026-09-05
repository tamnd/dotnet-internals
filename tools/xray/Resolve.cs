using System.Text.Json;

namespace ClrXray;

/// <summary>
/// The tuple a build is standing on: where the repository is, what compiler is about to run, and
/// what the citations are written against.
/// </summary>
/// <param name="Root">The top of the repository, which is the directory holding the solution file.</param>
/// <param name="Framework">The runtime this tool is itself running on, as it describes itself.</param>
/// <param name="Platform">The runtime identifier, which is the thing that makes two runs differ.</param>
/// <param name="Sdk">The SDK version the lessons will be compiled by.</param>
/// <param name="Landed">Whether the pin has a version in it yet.</param>
/// <param name="Available">Every configuration a lesson may declare, and whether this machine has it.</param>
internal sealed record Toolchain(
    string Root,
    string Framework,
    string Platform,
    string Sdk,
    bool Landed,
    IReadOnlyList<Available> Available);

/// <summary>
/// The first step of a build. Works out what everything is pinned to, and refuses to go any
/// further if the pin and the toolchain are describing two different worlds.
/// </summary>
/// <remarks>
/// <para>
/// This step exists because every later step is only as reproducible as the thing underneath it.
/// A page that says a struct is twenty four bytes was produced by one compiler on one platform,
/// and the way that page quietly becomes wrong is that somebody builds it with a different one and
/// nothing anywhere says so. So the build says so first, before it runs a single line of a lesson.
/// </para>
/// <para>
/// The two files it reads are doing different jobs and are allowed to disagree exactly once.
/// <c>global.json</c> decides which compiler runs. <c>pin.json</c> decides which source tree a
/// citation resolves against. Today those name different .NET versions on purpose, because the
/// citations will be written against .NET 11 and the tooling still builds on 10. The moment the
/// pin lands, that gap has to close, and this step is what closes it.
/// </para>
/// </remarks>
internal static class Resolve
{
    internal const string PinName = "pin.json";
    internal const string GlobalName = "global.json";

    internal static Toolchain Run(string path, Plan plan)
    {
        var root = Files.Root(path);

        var pinPath = Path.Combine(root, PinName);
        if (!File.Exists(pinPath))
        {
            throw new LessonException($"no {PinName} at {root}, so there is nothing saying what this build is pinned to");
        }

        // A pin found somewhere other than the top of the repository is a second pin, and two pins
        // is the state where half the pages resolve against one commit and half against another.
        var nearest = Pin.Locate(path);
        if (nearest is not null && !string.Equals(Path.GetFullPath(nearest), pinPath, StringComparison.Ordinal))
        {
            plan.Problem($"{nearest}: a second {PinName} below the top of the repository, and the whole point of a pin is that there is one");
        }

        var globalPath = Path.Combine(root, GlobalName);
        if (!File.Exists(globalPath))
        {
            throw new LessonException($"no {GlobalName} at {root}, so the compiler that builds this is whatever the machine happens to have");
        }

        var wanted = Read(globalPath, "sdk", "version");
        if (wanted is null)
        {
            plan.Problem($"{globalPath}: no sdk version, so two people cloning this repository can be compiling it with two different compilers");
        }

        var pinned = Read(pinPath, "sdk", "version");
        var landed = pinned is not null;

        if (landed && wanted is not null && !string.Equals(pinned, wanted, StringComparison.Ordinal))
        {
            plan.Problem($"{PinName} pins the SDK at {pinned} and {GlobalName} asks for {wanted}, so the lessons would be compiled by one version and cited against another");
        }

        Half(plan, pinPath, "runtime");
        Half(plan, pinPath, "roslyn");

        if (!landed && Read(pinPath, "runtime", "commit") is not null)
        {
            plan.Problem($"{PinName} has a runtime commit and no SDK version, which is half a pin and reads on a page as a whole one");
        }

        // Last, because the list of configurations is a thing the build is standing on in exactly
        // the same way the pin is: it decides which lessons this machine is allowed to run at all.
        var available = Environments.Detect(Environments.Load(root));

        return new Toolchain(root, Banner.Framework, Banner.Platform, Sdk(root), landed, available);
    }

    /// <summary>
    /// Reports the one line a reader needs in order to know whether the numbers below it are the
    /// numbers they would get.
    /// </summary>
    internal static string Describe(Toolchain toolchain) =>
        $"{toolchain.Framework} on {toolchain.Platform}, SDK {toolchain.Sdk}, "
        + (toolchain.Landed ? "pinned" : $"{PinName} holds no version yet so no citation resolves")
        + $", {Environments.Describe(toolchain.Available)}";

    /// <summary>
    /// A tag with no commit, or a commit with no tag. Both halves say what the pin is, and one
    /// half of a pin is the state where nobody can tell whether the pin has landed.
    /// </summary>
    private static void Half(Plan plan, string pinPath, string key)
    {
        var tag = Read(pinPath, key, "tag");
        var commit = Read(pinPath, key, "commit");

        if (tag is null != commit is null)
        {
            plan.Problem($"{PinName}: the {key} entry has a {(tag is null ? "commit and no tag" : "tag and no commit")}, and a pin is both or neither");
        }
    }

    /// <summary>
    /// Asks the SDK what version of itself is about to compile the lessons, from the top of the
    /// repository so that <c>global.json</c> is the thing answering.
    /// </summary>
    private static string Sdk(string root)
    {
        var (exit, stdout, _) = Runner.Dotnet(root, ["--version"]);
        return exit == 0 ? stdout.Trim() : "unknown";
    }

    private static string? Read(string path, string section, string field)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        if (!document.RootElement.TryGetProperty(section, out var entry)
            || !entry.TryGetProperty(field, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
