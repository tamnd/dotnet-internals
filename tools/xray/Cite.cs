using System.Globalization;

namespace ClrXray;

/// <summary>
/// The citation gate. Every citation on every page is taken apart, checked against the pin, and
/// then fetched from the repository it names at the commit it names.
/// </summary>
/// <remarks>
/// <para>
/// This is the one check in the repository that needs the network, which is why it is its own
/// command and its own CI job rather than part of <c>xray check</c>. A reader offline on a train
/// can still build every lesson and every diagram. What they cannot do is prove that a citation
/// still resolves, and pretending otherwise by skipping the check quietly would be worse than
/// saying so.
/// </para>
/// <para>
/// Fetched files are cached by commit, so the second run costs nothing and a pull request that
/// touches one paragraph does not pull half of <c>dotnet/runtime</c>. A commit never changes, so
/// the cache never needs invalidating.
/// </para>
/// </remarks>
internal static class Cite
{
    internal static int Run(string root)
    {
        try
        {
            if (!Directory.Exists(root))
            {
                Console.Error.WriteLine($"xray cite: no such directory: {root}");
                return 2;
            }

            var (count, errors) = Verify(root, verbose: true);

            foreach (var error in errors.Order(StringComparer.Ordinal))
            {
                Console.Error.WriteLine(error);
            }

            // Saying the count out loud matters while the pin has not landed, because zero
            // citations and a working checker look exactly the same from the outside otherwise.
            Console.WriteLine($"xray cite: {count} citation(s), {errors.Count} problem(s)");
            if (count == 0 && errors.Count == 0)
            {
                Console.WriteLine("xray cite: there are none yet, because pin.json still holds a null commit and a citation without one is not accepted");
            }

            return errors.Count == 0 ? 0 : 1;
        }
        catch (CiteException error)
        {
            Console.Error.WriteLine($"xray cite: {error.Message}");
            return 2;
        }
    }

    /// <summary>
    /// The work itself, shared by the standalone command and by the cite step of a build. The
    /// build wants the numbers rather than a footer of its own, so the reporting is the caller's
    /// job and this hands back what it found.
    /// </summary>
    internal static (int Count, List<string> Errors) Verify(string root, bool verbose)
    {
        var pin = Pin.Load(root);
        var errors = new List<string>();
        var citations = new List<Citation>();

        foreach (var path in Files.Markdown(root).Order(StringComparer.Ordinal))
        {
            citations.AddRange(Citations.Scan(path, pin, errors));
        }

        var source = new Source();
        foreach (var citation in citations)
        {
            var resolved = Resolve(citation, pin, source, out var error);
            if (resolved is null)
            {
                errors.Add($"{citation.Where}: `{citation.Text}`: {error}");
                continue;
            }

            if (verbose)
            {
                Console.WriteLine($"  {citation.Where}  {citation.Repo}:{citation.Path}:{Span(citation)}  {resolved}");
            }
        }

        return (citations.Count, errors);
    }

    private static string Span(Citation citation) =>
        citation.WholeFile ? "whole file"
        : citation.First == citation.Last ? citation.First.ToString(CultureInfo.InvariantCulture)
        : $"{citation.First}-{citation.Last}";

    /// <summary>
    /// Checks one citation and gives back the first cited line, which is what gets printed so that
    /// a reviewer reading the log sees what the citation actually points at rather than a tick.
    /// </summary>
    internal static string? Resolve(Citation citation, Pin pin, Source source, out string? error)
    {
        error = null;

        var pinned = pin.Find(citation.Repo);
        if (pinned is null)
        {
            error = $"'{citation.Repo}' is not a pinned repository, and there are two of those: {string.Join(" and ", pin.Keys)}";
            return null;
        }

        if (pinned.Commit is null)
        {
            error = $"the {pinned.Key} pin has not landed, so pin.json holds a null commit and no {pinned.Key} citation can be checked yet";
            return null;
        }

        if (!pinned.Commit.StartsWith(citation.Commit, StringComparison.OrdinalIgnoreCase))
        {
            error = $"pinned at {pinned.Commit[..12]}, cited at {citation.Commit}, and a page pinned to a second commit is a page nobody can reproduce";
            return null;
        }

        var lines = source.Read(pinned.Repo, pinned.Commit, citation.Path, out error);
        if (lines is null)
        {
            return null;
        }

        if (citation.WholeFile)
        {
            return $"{lines.Length} line(s)";
        }

        if (citation.Last > lines.Length)
        {
            error = $"the file has {lines.Length} line(s) at that commit, so line {citation.Last} is past the end of it";
            return null;
        }

        var cited = lines[(citation.First - 1)..citation.Last];

        if (citation.Expect is not null && !cited.Any(line => line.Contains(citation.Expect, StringComparison.Ordinal)))
        {
            error = $"nothing in line(s) {Span(citation)} contains '{citation.Expect}', and the first of them is: {Trim(cited[0])}";
            return null;
        }

        return Trim(cited[0]) + (cited.Length > 1 ? $"  (and {cited.Length - 1} more)" : string.Empty);
    }

    private static string Trim(string line)
    {
        var text = line.Trim();
        return text.Length <= 72 ? text : text[..69] + "...";
    }
}

/// <summary>
/// Reads a file out of a pinned repository at a pinned commit, over the network once and out of a
/// cache every time after.
/// </summary>
internal sealed class Source
{
    private static readonly HttpClient Http = Client();

    private readonly Dictionary<string, string[]> loaded = new(StringComparer.Ordinal);

    /// <summary>Where fetched files live, overridable so the self test does not touch a real cache.</summary>
    internal string Cache { get; init; } = Default();

    internal string[]? Read(string repo, string commit, string path, out string? error)
    {
        error = null;
        var key = $"{repo}@{commit}:{path}";
        if (loaded.TryGetValue(key, out var already))
        {
            return already;
        }

        var file = Path.Combine(Cache, repo.Replace('/', Path.DirectorySeparatorChar), commit, path.Replace('/', Path.DirectorySeparatorChar));
        string text;

        if (File.Exists(file))
        {
            text = File.ReadAllText(file);
        }
        else
        {
            var fetched = Fetch(repo, commit, path, out error);
            if (fetched is null)
            {
                return null;
            }

            text = fetched;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }

        var lines = Generated.Normalise(text).Split('\n');

        // A file that ends with a newline splits into a last element that is the empty string
        // after it, and counting that as a line would let a citation point one line past the end
        // of the file and be told it is fine.
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        loaded[key] = lines;
        return lines;
    }

    private static string? Fetch(string repo, string commit, string path, out string? error)
    {
        error = null;
        var url = $"https://raw.githubusercontent.com/{repo}/{commit}/{path}";

        try
        {
            using var response = Http.GetAsync(url).GetAwaiter().GetResult();

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                error = $"no such path in {repo} at that commit";
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                error = $"{repo} answered {(int)response.StatusCode} for that path, which is not a verdict on the citation";
                return null;
            }

            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
        {
            // Not a citation problem, so it stops the command rather than being counted as one bad
            // citation among several. A red build that means the network was down is a build
            // somebody learns to ignore.
            throw new CiteException($"could not reach {url}: {failure.Message}");
        }
    }

    private static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("User-Agent", "xray-citation-checker");
        return client;
    }

    private static string Default()
    {
        var set = Environment.GetEnvironmentVariable("XRAY_CITE_CACHE");
        if (!string.IsNullOrWhiteSpace(set))
        {
            return set;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(string.IsNullOrEmpty(home) ? Path.GetTempPath() : home, "xray", "cite");
    }
}
