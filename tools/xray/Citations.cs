namespace ClrXray;

/// <summary>
/// One citation, as written on a page and as the checker understands it.
/// </summary>
/// <param name="Repo">The pinned repository key, <c>runtime</c> or <c>roslyn</c>.</param>
/// <param name="Path">The path inside that repository.</param>
/// <param name="First">The first cited line, or zero for a citation of a whole file.</param>
/// <param name="Last">The last cited line, equal to <paramref name="First"/> for a single line.</param>
/// <param name="Commit">The commit as written, seven to forty hex digits.</param>
/// <param name="Expect">The text the cited lines have to contain, or null if the citation did not say.</param>
/// <param name="Source">The markdown file the citation was found in.</param>
/// <param name="SourceLine">The line of that file, one based.</param>
/// <param name="Text">The citation exactly as written, for messages.</param>
internal sealed record Citation(
    string Repo,
    string Path,
    int First,
    int Last,
    string Commit,
    string? Expect,
    string Source,
    int SourceLine,
    string Text)
{
    internal bool WholeFile => First == 0;

    internal string Where => $"{Source}:{SourceLine}";

    /// <summary>What to fetch once, no matter how many citations point into the same file.</summary>
    internal string Key => $"{Repo}@{Commit}:{Path}";
}

/// <summary>
/// Finding citations on a page and taking them apart.
/// </summary>
/// <remarks>
/// <para>
/// A citation is an inline code span that starts with a pinned repository key and a colon. That
/// rule is the whole of the detection, and it has one honest weakness: a reference written as
/// ordinary prose, with no prefix, is invisible here. The format exists so that review has
/// something mechanical to point at, not because a machine can find the references somebody chose
/// not to write down.
/// </para>
/// <para>
/// Fenced code blocks are skipped, because the examples in the format documentation live in them.
/// A line can also opt out with <c>xray-cite: allow</c>, which is what a sentence quoting a made up
/// citation inside running prose has to do.
/// </para>
/// </remarks>
internal static class Citations
{
    internal const string Allow = "xray-cite: allow";

    /// <summary>
    /// Reads one markdown file. Anything that looks like a citation and is not one comes back in
    /// <paramref name="errors"/> rather than being skipped, because a citation with a typo in it is
    /// the case this checker exists for.
    /// </summary>
    internal static List<Citation> Scan(string path, Pin pin, List<string> errors)
    {
        var found = new List<Citation>();
        var lines = File.ReadAllLines(path);
        var fenced = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced || line.Contains(Allow, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var span in Spans(line))
            {
                if (!Claims(span, pin))
                {
                    continue;
                }

                var citation = Parse(span, path, i + 1, out var error);
                if (citation is null)
                {
                    errors.Add($"{path}:{i + 1}: `{span}`: {error}");
                    continue;
                }

                found.Add(citation);
            }
        }

        return found;
    }

    /// <summary>
    /// True for a code span that is trying to be a citation, whether or not it succeeds.
    /// </summary>
    /// <remarks>
    /// The bare prefix with nothing after it, as in a sentence saying there are no <c>runtime:</c>
    /// citations on a page yet, is a mention of the prefix rather than an attempt at a citation.
    /// Anything with so much as one character after the colon is an attempt, including a path with
    /// no commit on it, which is the mistake worth catching.
    /// </remarks>
    private static bool Claims(string span, Pin pin)
    {
        var colon = span.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && colon < span.Length - 1 && pin.Find(span[..colon]) is not null;
    }

    /// <summary>
    /// Inline code spans on one line. Only single backticks, because a citation is short and
    /// nothing in it needs the double backtick escape.
    /// </summary>
    private static IEnumerable<string> Spans(string line)
    {
        var from = 0;
        while (true)
        {
            var open = line.IndexOf('`', from);
            if (open < 0)
            {
                yield break;
            }

            var close = line.IndexOf('`', open + 1);
            if (close < 0)
            {
                yield break;
            }

            yield return line[(open + 1)..close];
            from = close + 1;
        }
    }

    /// <summary>
    /// Takes one citation apart, or says what is wrong with it. The grammar is
    /// <c>repo:path[:line[-line]]@commit[#text]</c>.
    /// </summary>
    internal static Citation? Parse(string span, string source, int sourceLine, out string? error)
    {
        error = null;

        var colon = span.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            error = "no repository prefix, a citation starts with runtime: or roslyn:";
            return null;
        }

        var repo = span[..colon];
        var rest = span[(colon + 1)..];

        var at = rest.LastIndexOf('@');
        if (at < 0)
        {
            error = "no commit, and a citation is pinned by commit rather than by tag or branch";
            return null;
        }

        var tail = rest[(at + 1)..];
        var left = rest[..at];

        string? expect = null;
        var hash = tail.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            expect = tail[(hash + 1)..];
            tail = tail[..hash];
            if (expect.Length == 0)
            {
                error = "empty expectation after the hash, either name the text the line has to contain or drop the hash";
                return null;
            }
        }

        if (!Hex(tail))
        {
            error = $"'{tail}' is not a commit, which is seven to forty hex digits";
            return null;
        }

        var path = left;
        var first = 0;
        var last = 0;

        var lineColon = left.LastIndexOf(':');
        if (lineColon >= 0)
        {
            path = left[..lineColon];
            if (!Lines(left[(lineColon + 1)..], out first, out last, out error))
            {
                return null;
            }
        }

        if (!Path(path, out error))
        {
            return null;
        }

        return new Citation(repo, path, first, last, tail, expect, source, sourceLine, span);
    }

    private static bool Hex(string text) =>
        text.Length is >= 7 and <= 40 && text.All(Uri.IsHexDigit);

    private static bool Lines(string text, out int first, out int last, out string? error)
    {
        first = 0;
        last = 0;
        error = null;

        var dash = text.IndexOf('-', StringComparison.Ordinal);
        var one = dash < 0 ? text : text[..dash];
        var two = dash < 0 ? text : text[(dash + 1)..];

        if (!int.TryParse(one, out first) || !int.TryParse(two, out last))
        {
            error = $"'{text}' is not a line or a line range";
            return false;
        }

        if (first < 1)
        {
            error = "line numbers start at one";
            return false;
        }

        if (last < first)
        {
            error = $"the range {first}-{last} ends before it starts";
            return false;
        }

        return true;
    }

    private static bool Path(string path, out string? error)
    {
        error = null;

        if (path.Length == 0)
        {
            error = "no path";
            return false;
        }

        if (path.StartsWith('/'))
        {
            error = "the path is relative to the root of the pinned repository, so it does not start with a slash";
            return false;
        }

        if (path.Contains('\\', StringComparison.Ordinal))
        {
            error = "the path separator is a forward slash on all four platforms, because this path is a path in somebody else's repository";
            return false;
        }

        if (path.Split('/').Contains("..", StringComparer.Ordinal))
        {
            error = "the path walks upwards, and there is nothing above the root of a repository";
            return false;
        }

        return true;
    }
}
