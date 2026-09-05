using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClrXray;

/// <summary>
/// What one cached thing is, in the four ways a thing this project fetches can differ.
/// </summary>
/// <remarks>
/// <para>
/// All four are here even though the only user today, the citation checker, varies by two of them.
/// The artifacts this key was designed for are a checked <c>clrjit</c> and a built runtime, and
/// those differ by platform and by configuration in ways that are invisible from the outside: a
/// checked and a release <c>libclrjit.so</c> for the same commit on the same platform are the same
/// size, the same name and completely different programs. Getting the layout wrong and fixing it
/// later means invalidating every cache anybody has, so it is worth being right about now.
/// </para>
/// <para>
/// A part is used as a directory name, so anything that could climb out of the cache root is
/// refused rather than escaped. A cache is a place a program writes without anybody watching,
/// which makes it the wrong place to be relaxed about paths.
/// </para>
/// </remarks>
/// <param name="Repository">Where it came from, such as <c>dotnet/runtime</c>.</param>
/// <param name="Tag">The commit or tag inside that repository. A pin, not a branch.</param>
/// <param name="Platform">The runtime identifier, or <c>any</c> for something that does not differ.</param>
/// <param name="Configuration">Release, Checked, Debug, or <c>any</c>.</param>
/// <param name="Name">What the thing is called. May have slashes in it, so a fetched source file keeps its shape.</param>
internal sealed record Key(string Repository, string Tag, string Platform, string Configuration, string Name)
{
    /// <summary>The value for an axis a thing genuinely does not vary along.</summary>
    internal const string Any = "any";

    /// <summary>
    /// One string, for somewhere that wants a key rather than a path, such as the cache action in
    /// a workflow. The version is on the front so a change to this layout cannot restore an old
    /// cache into a tool that would misread it.
    /// </summary>
    internal string Text =>
        string.Join('-', new[] { $"xray{Cache.Version}", Repository, Tag, Platform, Configuration, Name }.Select(Slug));

    /// <summary>The parts, in order, as directory names, with the file name last.</summary>
    internal IEnumerable<string> Parts()
    {
        yield return Slug(Repository);
        yield return Slug(Tag);
        yield return Slug(Platform);
        yield return Slug(Configuration);

        foreach (var segment in Name.Split('/', '\\'))
        {
            yield return Slug(segment);
        }
    }

    /// <summary>
    /// Checks every part is usable as one directory name. Throws rather than returning, because a
    /// key that does not hold is a bug in the caller and not a condition to carry on from.
    /// </summary>
    internal Key Sound()
    {
        foreach (var (part, what) in new[]
        {
            (Repository, nameof(Repository)),
            (Tag, nameof(Tag)),
            (Platform, nameof(Platform)),
            (Configuration, nameof(Configuration)),
            (Name, nameof(Name)),
        })
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                throw new LessonException($"a cache key with no {what.ToLowerInvariant()} is a key that collides with every other one");
            }
        }

        foreach (var segment in Parts())
        {
            if (segment is "" or "." or ".." || Path.IsPathRooted(segment))
            {
                throw new LessonException($"'{Name}' has a path segment in it that would put this outside the cache");
            }
        }

        return this;
    }

    /// <summary>
    /// One path segment, from something that may not be one. Two different originals can slug to
    /// the same text, which is why what is stored records the original and the store refuses to
    /// put a second one in the same place.
    /// </summary>
    private static string Slug(string part)
    {
        var text = new StringBuilder(part.Length);

        foreach (var character in part)
        {
            text.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' ? character : '-');
        }

        return text.ToString();
    }
}

/// <summary>One thing in the cache, and the record of where it came from.</summary>
internal sealed class Stored
{
    public string Repository { get; init; } = string.Empty;

    public string Tag { get; init; } = string.Empty;

    public string Platform { get; init; } = string.Empty;

    public string Configuration { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Where it was fetched from, so a binary in a cache is not a thing of unknown origin.</summary>
    public string From { get; init; } = string.Empty;

    public string Sha256 { get; init; } = string.Empty;

    public long Bytes { get; init; }

    public string When { get; init; } = string.Empty;

    [JsonIgnore]
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// One place on disk for everything this tool fetches, keyed the four ways a fetched thing can
/// differ, with a record beside each one of where it came from.
/// </summary>
/// <remarks>
/// <para>
/// There was already a cache before this, holding source files pulled out of the pinned
/// repositories by the citation checker. It had its own directory, its own environment variable,
/// no way to look inside it, no way to empty it, and nothing recording what any of it was. It also
/// lived only for as long as one CI job, so every pull request fetched every cited file again.
/// </para>
/// <para>
/// The part that matters most here is not speed. A cache is a set of files a program wrote without
/// anybody watching, and the rest of this repository is built on the idea that nothing is trusted
/// because it is sitting there. So every entry carries the address it came from and the hash of
/// what arrived, the hash is checked on the way back out, and an entry that has changed since it
/// was stored is refused and refetched rather than used.
/// </para>
/// </remarks>
internal sealed class Cache
{
    /// <summary>Bumped when the layout changes, so an old cache is ignored rather than misread.</summary>
    internal const int Version = 1;

    internal const string Variable = "XRAY_CACHE";

    private const string Sidecar = ".xray.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    internal string Root { get; init; } = Default();

    /// <summary>Where the tool keeps everything it fetches, unless told otherwise.</summary>
    internal static string Default()
    {
        var set = Environment.GetEnvironmentVariable(Variable);
        if (!string.IsNullOrWhiteSpace(set))
        {
            return set;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(string.IsNullOrEmpty(home) ? Path.GetTempPath() : home, "xray", "cache");
    }

    internal string Where(Key key) =>
        Path.Combine(new[] { Root, "v" + Version.ToString(System.Globalization.CultureInfo.InvariantCulture) }.Concat(key.Sound().Parts()).ToArray());

    /// <summary>
    /// What is in the cache for this key, or nothing. A file whose hash does not match what was
    /// recorded when it was stored counts as nothing, and says why on the way past.
    /// </summary>
    internal byte[]? Read(Key key)
    {
        var path = Where(key);
        var record = Record(path);

        if (!File.Exists(path) || record is null)
        {
            return null;
        }

        var content = File.ReadAllBytes(path);
        var hash = Hash(content);

        if (hash != record.Sha256)
        {
            Console.Error.WriteLine($"{path}: this has changed since it was cached, so it is being fetched again rather than trusted");
            File.Delete(path);
            File.Delete(path + Sidecar);
            return null;
        }

        if (record.Repository != key.Repository || record.Tag != key.Tag || record.Name != key.Name)
        {
            // Two keys that are different can slug to the same path. Saying so beats handing back
            // the wrong file, which is the one outcome a cache must never have.
            Console.Error.WriteLine($"{path}: holds {record.Repository}@{record.Tag} {record.Name} rather than what was asked for, so it is being fetched again");
            return null;
        }

        return content;
    }

    /// <summary>Puts something in the cache, with the record of where it came from beside it.</summary>
    internal void Store(Key key, byte[] content, string from)
    {
        var path = Where(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var record = new Stored
        {
            Repository = key.Repository,
            Tag = key.Tag,
            Platform = key.Platform,
            Configuration = key.Configuration,
            Name = key.Name,
            From = from,
            Sha256 = Hash(content),
            Bytes = content.LongLength,
            When = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };

        File.WriteAllBytes(path, content);
        File.WriteAllText(path + Sidecar, JsonSerializer.Serialize(record, Options));
    }

    /// <summary>Everything in the cache, oldest layout first, in one order on every platform.</summary>
    internal IEnumerable<Stored> Entries()
    {
        if (!Directory.Exists(Root))
        {
            yield break;
        }

        foreach (var sidecar in Directory.EnumerateFiles(Root, "*" + Sidecar, SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var path = sidecar[..^Sidecar.Length];
            var record = Record(path);

            if (record is not null && File.Exists(path))
            {
                record.Path = path;
                yield return record;
            }
        }
    }

    internal int Clear()
    {
        if (!Directory.Exists(Root))
        {
            return 0;
        }

        var count = Entries().Count();
        Directory.Delete(Root, recursive: true);
        return count;
    }

    /// <summary>The <c>cache</c> command, which is the only way to see inside this thing.</summary>
    internal static int Run(string[] args)
    {
        var cache = new Cache();
        var what = args.Skip(1).FirstOrDefault(a => !a.StartsWith('-')) ?? "list";

        switch (what)
        {
            case "path":
                Console.WriteLine(cache.Root);
                return 0;

            case "key":
                return PrintKey(args);

            case "list":
                {
                    var entries = cache.Entries().ToList();

                    foreach (var entry in entries)
                    {
                        Console.WriteLine($"{entry.Repository}@{entry.Tag} {entry.Platform} {entry.Configuration} {entry.Name}");
                        Console.WriteLine($"  {entry.Bytes} byte(s), fetched {entry.When} from {entry.From}");
                    }

                    Console.WriteLine($"xray cache: {entries.Count} entr(ies) under {cache.Root}");
                    return 0;
                }

            case "clear":
                {
                    var removed = cache.Clear();
                    Console.WriteLine($"xray cache: removed {removed} entr(ies) from {cache.Root}");
                    return 0;
                }

            default:
                Console.Error.WriteLine($"xray cache: no such thing to do: {what}. It is one of path, key, list or clear.");
                return 2;
        }
    }

    /// <summary>
    /// Prints the key for a set of parts, which is what a workflow hands to whatever restores the
    /// cache between runs. Written here rather than typed into the workflow, so the two cannot
    /// disagree about what a key is.
    /// </summary>
    private static int PrintKey(string[] args)
    {
        var key = new Key(
            Flag(args, "--repository") ?? Key.Any,
            Flag(args, "--tag") ?? Key.Any,
            Flag(args, "--platform") ?? Banner.Platform,
            Flag(args, "--configuration") ?? Key.Any,
            Flag(args, "--name") ?? Key.Any);

        try
        {
            Console.WriteLine(key.Sound().Text);
            return 0;
        }
        catch (LessonException error)
        {
            Console.Error.WriteLine($"xray cache: {error.Message}");
            return 2;
        }
    }

    private static string? Flag(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    private static Stored? Record(string path)
    {
        var sidecar = path + Sidecar;

        if (!File.Exists(sidecar))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Stored>(File.ReadAllText(sidecar));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Hash(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
}
