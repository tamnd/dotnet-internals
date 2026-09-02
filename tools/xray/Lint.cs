namespace ClrXray;

/// <summary>
/// The prose rules, checked by a machine so that nobody has to be the person who mentions them
/// in review.
/// </summary>
/// <remarks>
/// A line can opt out by carrying the marker <c>xray-lint: allow</c> inside an HTML comment,
/// which is what the paragraph listing the banned words does. Opting out is meant to be visible
/// in the diff, which is why it is a comment in the file rather than a list somewhere else.
/// </remarks>
internal static class Lint
{
    private const string Allow = "xray-lint: allow";

    private static readonly string[] SkipDirectories =
    [
        ".git", "bin", "obj", "vendor", "node_modules", "artifacts", "build",
    ];

    private static readonly string[] BannedWords =
    [
        "simply", "just", "obviously", "trivially", "of course",
    ];

    internal static int Run(string root)
    {
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"xray lint: no such directory: {root}");
            return 2;
        }

        var problems = 0;
        var files = 0;

        foreach (var path in Markdown(root).Order(StringComparer.Ordinal))
        {
            files++;
            problems += Check(path);
        }

        Console.WriteLine($"xray lint: {files} file(s), {problems} problem(s)");
        return problems == 0 ? 0 : 1;
    }

    private static IEnumerable<string> Markdown(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!parts.Any(part => SkipDirectories.Contains(part, StringComparer.Ordinal)))
            {
                yield return path;
            }
        }
    }

    private static int Check(string path)
    {
        var lines = File.ReadAllLines(path);
        var problems = 0;
        var fenced = false;
        var previousWasProse = false;

        // Front matter opens on the first line and closes on the next line that is three dashes.
        // It is the one place in a markdown file where three dashes are structure rather than a
        // page break, so it is skipped rather than argued with.
        var matter = lines.Length > 0 && lines[0].Trim() == "---";

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (matter)
            {
                if (i > 0 && trimmed == "---")
                {
                    matter = false;
                }

                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                fenced = !fenced;
                previousWasProse = false;
                continue;
            }

            if (fenced)
            {
                continue;
            }

            if (line.Contains(Allow, StringComparison.Ordinal))
            {
                previousWasProse = false;
                continue;
            }

            if (line.Contains('—'))
            {
                problems += Report(path, i, "em dash, use a comma or a full stop");
            }

            if (IsHorizontalRule(trimmed))
            {
                problems += Report(path, i, "horizontal rule, a document that needs a break needs a heading");
            }

            foreach (var word in BannedWords)
            {
                if (ContainsWord(line, word))
                {
                    problems += Report(path, i, $"banned word: {word}");
                }
            }

            var prose = IsProse(line, trimmed);
            if (prose && previousWasProse)
            {
                problems += Report(path, i, "hard wrapped paragraph, one line per paragraph");
            }

            previousWasProse = prose;
        }

        return problems;
    }

    private static bool IsHorizontalRule(string trimmed)
    {
        if (trimmed.Length < 3)
        {
            return false;
        }

        return trimmed.All(c => c == '-') || trimmed.All(c => c == '*') || trimmed.All(c => c == '_');
    }

    /// <summary>
    /// True for a line that carries running prose, which is the only kind of line the one line
    /// per paragraph rule applies to.
    /// </summary>
    private static bool IsProse(string line, string trimmed)
    {
        if (trimmed.Length == 0 || line.StartsWith("    ", StringComparison.Ordinal))
        {
            return false;
        }

        char first = trimmed[0];
        if (first is '#' or '>' or '|' or '<' or '-' or '*' or '+' or '=')
        {
            return false;
        }

        // An ordered list item, which wraps the same way a list item does rather than the way a
        // paragraph does.
        var dot = trimmed.IndexOf('.');
        if (dot > 0 && dot < 4 && trimmed[..dot].All(char.IsAsciiDigit))
        {
            return false;
        }

        return true;
    }

    private static bool ContainsWord(string line, string word)
    {
        var from = 0;
        while (true)
        {
            var at = line.IndexOf(word, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                return false;
            }

            var before = at == 0 || !char.IsLetterOrDigit(line[at - 1]);
            var end = at + word.Length;
            var after = end >= line.Length || !char.IsLetterOrDigit(line[end]);
            if (before && after)
            {
                return true;
            }

            from = at + 1;
        }
    }

    private static int Report(string path, int index, string message)
    {
        Console.Error.WriteLine($"{path}:{index + 1}: {message}");
        return 1;
    }
}
