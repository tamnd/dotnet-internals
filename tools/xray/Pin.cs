using System.Text.Json;

namespace ClrXray;

/// <summary>
/// One pinned repository: the key a citation writes, the GitHub path it resolves against, and the
/// commit it is frozen at.
/// </summary>
/// <param name="Key">What a citation writes before the colon, as in <c>runtime:</c>.</param>
/// <param name="Repo">Owner and name, as in <c>dotnet/runtime</c>.</param>
/// <param name="Commit">The frozen commit, or null while the pin has not landed.</param>
internal sealed record PinnedRepo(string Key, string Repo, string? Commit);

/// <summary>
/// The contents of <c>pin.json</c>, which is the only thing in this repository allowed to say what
/// a citation resolves against.
/// </summary>
/// <remarks>
/// A tag is not a commit. Tags in both pinned repositories are annotated, so the name points at a
/// tag object that points at a commit, and the two have different hashes. The pin records the
/// commit, and a citation that carries anything else is rejected, because the whole promise of the
/// pin is that two people reading the same citation are looking at the same bytes.
/// </remarks>
internal sealed class Pin
{
    private readonly Dictionary<string, PinnedRepo> repos;

    internal Pin(IEnumerable<PinnedRepo> pinned)
    {
        repos = pinned.ToDictionary(r => r.Key, StringComparer.Ordinal);
    }

    /// <summary>The keys a citation is allowed to start with, in the order they appear in the file.</summary>
    internal IReadOnlyCollection<string> Keys => repos.Keys;

    internal PinnedRepo? Find(string key) => repos.GetValueOrDefault(key);

    /// <summary>
    /// Reads <c>pin.json</c> from the directory given, or from the nearest ancestor of it that has
    /// one. Running the tool from a lesson directory is a normal thing to do and the pin is still
    /// the repository's pin when you do.
    /// </summary>
    internal static Pin Load(string from)
    {
        var path = Locate(from)
            ?? throw new CiteException($"no pin.json at or above {Path.GetFullPath(from)}, so there is nothing to resolve citations against");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var pinned = new List<PinnedRepo>();

        string[] expected = ["runtime", "roslyn"];
        foreach (var key in expected)
        {
            if (!root.TryGetProperty(key, out var entry))
            {
                throw new CiteException($"{path}: no '{key}' entry, and the citation format is built on there being exactly two pinned repositories");
            }

            var repo = entry.TryGetProperty("repo", out var name) ? name.GetString() : null;
            if (string.IsNullOrWhiteSpace(repo))
            {
                throw new CiteException($"{path}: the '{key}' entry has no repo");
            }

            var commit = entry.TryGetProperty("commit", out var sha) && sha.ValueKind == JsonValueKind.String
                ? sha.GetString()
                : null;

            pinned.Add(new PinnedRepo(key, repo, commit));
        }

        return new Pin(pinned);
    }

    internal static string? Locate(string from)
    {
        var directory = Directory.Exists(from) ? new DirectoryInfo(Path.GetFullPath(from)) : new FileInfo(Path.GetFullPath(from)).Directory;

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "pin.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

/// <summary>
/// Something went wrong that stops the whole command rather than one citation, such as a pin that
/// cannot be read.
/// </summary>
internal sealed class CiteException(string message) : Exception(message);
