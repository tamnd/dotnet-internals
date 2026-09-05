using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ClrXray;

/// <summary>
/// What a blueprint says about itself. Everything in here ends up on the page rather than being
/// typed into the prose twice, because a document whose own header can disagree with it is a
/// document that eventually does.
/// </summary>
internal sealed class BlueprintManifest
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    /// <summary>Which part of the book argues for this subsystem. It appears in the front matter and nowhere in the prose.</summary>
    public string Part { get; init; } = string.Empty;

    /// <summary>Either <c>draft</c> or <c>complete</c>. A complete blueprint has all nine sections and the tool checks it.</summary>
    public string Status { get; init; } = Blueprints.Draft;

    public IReadOnlyList<string> Sources { get; init; } = [];
}

/// <summary>
/// One blueprint whose generator has been run, and everything the later steps of the build need
/// from it.
/// </summary>
internal sealed record Made(
    string Directory,
    string Name,
    BlueprintManifest Manifest,
    IReadOnlyList<Block> Blocks,
    Dictionary<string, string> Captured);

/// <summary>
/// Builds a blueprint, which is a specification of one subsystem written for somebody
/// implementing it rather than for somebody learning it.
/// </summary>
/// <remarks>
/// <para>
/// The reason this exists as machinery rather than as a directory of markdown is the claim the
/// project is making: that a specification of .NET can be generated from what the runtime and its
/// libraries already publish, rather than transcribed by a person who gets one row of a table
/// wrong and nobody finds out for a year. A generated section is produced by a program, committed,
/// and regenerated on every pull request, so hand editing one is a change that cannot survive CI.
/// </para>
/// <para>
/// Everything factual about the document is generated too. The title, the status, the list of
/// sources and the list of which sections are generated are all produced from the manifest and
/// from where the holes actually are, so none of them can be left saying something that stopped
/// being true three commits ago.
/// </para>
/// </remarks>
internal static class Blueprints
{
    internal const string Draft = "draft";
    internal const string Complete = "complete";

    private const string ManifestName = "blueprint.json";
    private const string SourceName = "generate.cs";
    private const string ProseName = "blueprint.src.md";
    private const string PageName = "blueprint.md";
    private const string GeneratedDirectory = "generated";

    /// <summary>
    /// The nine sections, in order, every time. A blueprint may leave a section out while it is a
    /// draft, because a section written to fill a slot is worse than an absent one, but it may not
    /// invent a section, rename one, or put them in a different order.
    /// </summary>
    private static readonly string[] Template =
    [
        "Purpose and scope",
        "Data structures",
        "Algorithms",
        "Invariants",
        "Observable behaviour",
        "Edge cases and error paths",
        "Interactions",
        "Conformance",
        "Port notes",
    ];

    /// <summary>
    /// Phrases that point at the teaching side of the book. A blueprint is read by somebody who
    /// has not read that side and is not going to, so a sentence that leans on it is a hole in the
    /// specification rather than a cross reference.
    /// </summary>
    private static readonly string[] Pointers =
    [
        "lesson", "chapter", "as we saw", "recall that", "earlier we", "you will remember",
    ];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    internal static List<string> Discover(string path)
    {
        if (File.Exists(Path.Combine(path, ManifestName)))
        {
            return [Path.GetFullPath(path)];
        }

        if (!Directory.Exists(path))
        {
            return [];
        }

        return Directory.EnumerateFiles(path, ManifestName, SearchOption.AllDirectories)
            .Select(file => Path.GetFullPath(Path.GetDirectoryName(file)!))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Step three. Reads the manifest and runs the generator, which is the only part of a
    /// blueprint that is a program rather than a file.
    /// </summary>
    internal static Made Execute(string directory, Plan plan)
    {
        var name = Path.GetFileName(directory);
        var manifest = LoadManifest(directory);

        var prosePath = Path.Combine(directory, ProseName);
        if (!File.Exists(prosePath))
        {
            throw new LessonException($"{name}: no {ProseName}, so there is a generator and nothing to put it in");
        }

        var sourcePath = Path.Combine(directory, SourceName);
        if (!File.Exists(sourcePath))
        {
            return new Made(directory, name, manifest, [], []);
        }

        var lines = File.ReadAllLines(sourcePath);
        var blocks = Blocks.Parse(sourcePath, lines);
        var captured = Blocks.Execute(directory, lines, blocks, "generator");

        foreach (var block in blocks)
        {
            if (block.Capture != Blocks.Stdout)
            {
                continue;
            }

            if (!captured.TryGetValue(block.Id, out var output))
            {
                plan.Problem($"{name}: block '{block.Id}' printed no marker, so it never ran");
                continue;
            }

            if (output.Length == 0)
            {
                plan.Problem($"{name}: block '{block.Id}' produced nothing, so the section it fills would be empty");
                continue;
            }

            Lessons.Machine(directory, name, block.Id, output, plan);
        }

        return new Made(directory, name, manifest, blocks, captured);
    }

    /// <summary>
    /// Step four. One file per generated section, which is the artefact a reviewer diffs when
    /// somebody changes the generator.
    /// </summary>
    internal static int Generate(Made blueprint, Plan plan)
    {
        var count = 0;

        foreach (var block in blueprint.Blocks)
        {
            if (block.Capture != Blocks.Stdout
                || !blueprint.Captured.TryGetValue(block.Id, out var output)
                || output.Length == 0)
            {
                continue;
            }

            plan.Add(Path.Combine(blueprint.Directory, GeneratedDirectory, block.Id + ".md"), output);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Step five. Checks the nine sections and the house rule about pointing at the teaching side
    /// of the book, then puts the page together.
    /// </summary>
    internal static int Assemble(Made blueprint, Plan plan)
    {
        var prosePath = Path.Combine(blueprint.Directory, ProseName);
        var prose = Generated.Normalise(File.ReadAllText(prosePath)).Split('\n');

        var written = Sections(prosePath, blueprint.Manifest, prose, plan);
        References(prosePath, prose, plan);

        var page = Render(prosePath, blueprint.Manifest, prose, blueprint.Captured, written);
        plan.Add(Path.Combine(blueprint.Directory, PageName), page);

        return 1;
    }

    private static BlueprintManifest LoadManifest(string directory)
    {
        var path = Path.Combine(directory, ManifestName);
        var manifest = JsonSerializer.Deserialize<BlueprintManifest>(File.ReadAllText(path), Options)
            ?? throw new LessonException($"{path}: not a blueprint manifest");

        if (manifest.Id.Length == 0 || manifest.Title.Length == 0)
        {
            throw new LessonException($"{path}: a blueprint needs an id and a title");
        }

        if (manifest.Status is not (Draft or Complete))
        {
            throw new LessonException($"{path}: status is {Draft} or {Complete}, not '{manifest.Status}'");
        }

        if (manifest.Sources.Count == 0)
        {
            throw new LessonException($"{path}: a blueprint with no source of truth is somebody's opinion");
        }

        return manifest;
    }

    /// <summary>
    /// Checks the nine sections: the right names, in the right order, and all of them once the
    /// blueprint stops calling itself a draft.
    /// </summary>
    private static List<int> Sections(string prosePath, BlueprintManifest manifest, string[] prose, Plan plan)
    {
        var written = new List<int>();
        var next = 0;

        foreach (var line in prose)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                continue;
            }

            var heading = trimmed[3..].Trim();
            var dot = heading.IndexOf('.', StringComparison.Ordinal);
            if (dot <= 0 || !int.TryParse(heading[..dot], out var number))
            {
                plan.Problem($"{prosePath}: '{heading}' is not one of the nine sections, which are numbered");
                continue;
            }

            var title = heading[(dot + 1)..].Trim();
            if (number < 1 || number > Template.Length || Template[number - 1] != title)
            {
                var expected = number >= 1 && number <= Template.Length ? Template[number - 1] : "no such section";
                plan.Problem($"{prosePath}: section {number} is '{expected}', not '{title}'");
                continue;
            }

            if (number < next)
            {
                plan.Problem($"{prosePath}: section {number} comes after section {next}, and the nine are always in order");
                continue;
            }

            next = number + 1;
            written.Add(number);
        }

        if (manifest.Status == Complete && written.Count != Template.Length)
        {
            plan.Problem($"{prosePath}: a blueprint that is not a draft has all nine sections, this one has {written.Count}");
        }

        if (written.Count == 0)
        {
            plan.Problem($"{prosePath}: no sections at all");
        }

        return written;
    }

    private static void References(string prosePath, string[] prose, Plan plan)
    {
        for (var i = 0; i < prose.Length; i++)
        {
            foreach (var pointer in Pointers)
            {
                if (prose[i].Contains(pointer, StringComparison.OrdinalIgnoreCase))
                {
                    plan.Problem($"{prosePath}:{i + 1}: '{pointer}' points at the teaching side of the book, and a blueprint stands on its own");
                }
            }
        }
    }

    private static string Render(
        string prosePath,
        BlueprintManifest manifest,
        string[] prose,
        Dictionary<string, string> captured,
        List<int> written)
    {
        // Two passes, because the header says which sections are generated and the header comes
        // before them. The first pass works out where the holes are, the second one fills them.
        var generated = new List<string>();
        var section = string.Empty;

        foreach (var line in prose)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("##", StringComparison.Ordinal))
            {
                section = Number(trimmed);
                continue;
            }

            if (Hole(trimmed) is not null && section.Length > 0 && !generated.Contains(section, StringComparer.Ordinal))
            {
                generated.Add(section);
            }
        }

        var page = new StringBuilder();
        page.Append(Head(manifest, generated, written));

        foreach (var line in prose)
        {
            var trimmed = line.Trim();
            var id = Hole(trimmed);

            if (id is null)
            {
                page.Append(line).Append('\n');
                continue;
            }

            if (!captured.TryGetValue(id, out var content))
            {
                throw new LessonException($"{prosePath}: no block named '{id}' in {SourceName}");
            }

            page.Append(content);
        }

        return page.ToString().TrimEnd('\n') + "\n";
    }

    /// <summary>The id inside a <c>{{generated:id}}</c> hole, or null for an ordinary line.</summary>
    private static string? Hole(string trimmed)
    {
        const string Open = "{{generated:";
        if (!trimmed.StartsWith(Open, StringComparison.Ordinal) || !trimmed.EndsWith("}}", StringComparison.Ordinal))
        {
            return null;
        }

        return trimmed[Open.Length..^2].Trim();
    }

    /// <summary>The number off the front of a heading, so that <c>### 2.4 Coded indexes</c> is 2.4.</summary>
    private static string Number(string trimmed)
    {
        var text = trimmed.TrimStart('#').Trim();
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        if (space <= 0)
        {
            return string.Empty;
        }

        var head = text[..space].TrimEnd('.');
        return head.Length > 0 && head.All(c => char.IsAsciiDigit(c) || c == '.') ? head : string.Empty;
    }

    private static string Head(BlueprintManifest manifest, List<string> generated, List<int> written)
    {
        var head = new StringBuilder();

        head.Append("---\n");
        head.Append("id: ").Append(manifest.Id).Append('\n');
        head.Append("title: ").Append(manifest.Title).Append('\n');
        head.Append("part: ").Append(manifest.Part).Append('\n');
        head.Append("status: ").Append(manifest.Status).Append('\n');
        head.Append("---\n\n");

        head.Append("<!-- Generated by xray from blueprint.src.md and generate.cs. Do not edit this file, edit those two and run: dotnet run --project tools/xray -- build blueprints -->\n\n");

        head.Append("# ").Append(manifest.Id).Append(", ").Append(manifest.Title).Append("\n\n");

        head.Append("**Status.** ");
        head.Append(manifest.Status == Complete
            ? "Complete. All nine sections are written."
            : $"Draft. {(written.Count == 1 ? "Section" : "Sections")} {List(written.Select(n => n.ToString(CultureInfo.InvariantCulture)))} of the nine {(written.Count == 1 ? "is" : "are")} written, and the rest are not here rather than being here and empty.");
        head.Append("\n\n");

        head.Append("**Source of truth.** ").Append(string.Join(" ", manifest.Sources.Select(s => s.TrimEnd('.') + "."))).Append("\n\n");

        head.Append("**Generated sections.** ");
        head.Append(generated.Count == 0
            ? "None. Every line below was written by a person."
            : $"{List(generated)}. {(generated.Count == 1 ? "It is" : "They are")} produced by `generate.cs`, rewritten by `dotnet run --project tools/xray -- build blueprints`, and compared against what is committed on every pull request, so editing {(generated.Count == 1 ? "it" : "one of them")} by hand is a change that does not survive CI.");
        head.Append("\n\n");

        return head.ToString();
    }

    private static string List(IEnumerable<string> items)
    {
        var all = items.ToList();
        return all.Count switch
        {
            0 => string.Empty,
            1 => all[0],
            _ => string.Join(", ", all.Take(all.Count - 1)) + " and " + all[^1],
        };
    }
}
