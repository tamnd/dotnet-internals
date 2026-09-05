namespace ClrXray;

/// <summary>
/// The gate behind the sentence this whole project rests on, which is that no number in a lesson
/// is typed by a person.
/// </summary>
/// <remarks>
/// <para>
/// Every other guarantee here is downstream of that one. A page whose listings are generated and
/// whose prose says "the header is sixteen bytes" because somebody measured it once in a debugger
/// is a page that goes quietly wrong at the next version bump, and it goes wrong in the half a
/// reader is most likely to believe.
/// </para>
/// <para>
/// So there are two rules, and the second one is the one with teeth.
/// </para>
/// </remarks>
internal static class Numbers
{
    private const string ProseName = "lesson.src.md";
    private const string ExpectedDirectory = "expected";
    private const string Literal = "literal:";

    internal static int Run(string root)
    {
        var lessons = Discover(root);
        if (lessons.Count == 0)
        {
            Console.Error.WriteLine($"xray numbers: nothing to check under {root}, a lesson is a directory holding {ProseName}");
            return 2;
        }

        var problems = 0;
        foreach (var lesson in lessons)
        {
            foreach (var problem in Check(lesson))
            {
                Console.Error.WriteLine(problem);
                problems++;
            }
        }

        Console.WriteLine($"xray numbers: {lessons.Count} lesson(s), {problems} problem(s)");
        return problems == 0 ? 0 : 1;
    }

    private static List<string> Discover(string root)
    {
        if (File.Exists(Path.Combine(root, ProseName)))
        {
            return [Path.GetFullPath(root)];
        }

        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, ProseName, SearchOption.AllDirectories)
            .Select(file => Path.GetFullPath(Path.GetDirectoryName(file)!))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Every problem with one lesson, given back rather than printed, so the self test can read
    /// the messages as well as count them.
    /// </summary>
    internal static List<string> Check(string directory)
    {
        var name = Path.GetFileName(directory);
        var prose = File.ReadAllLines(Path.Combine(directory, ProseName));
        var captured = Captured(directory);
        var problems = new List<string>();

        var fenced = false;
        var matter = prose.Length > 0 && prose[0].Trim() == "---";

        for (var i = 0; i < prose.Length; i++)
        {
            var line = prose[i];
            var trimmed = line.Trim();

            // Front matter is a data block that has an id, a lesson number and a platform list in
            // it, and every one of those is a name rather than a measurement.
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
                continue;
            }

            // A fenced block on a source page is either an example or a listing, and a transclusion
            // is a hole the tool fills, so neither one is somebody typing a number.
            if (fenced || (trimmed.StartsWith("{{", StringComparison.Ordinal) && trimmed.EndsWith("}}", StringComparison.Ordinal)))
            {
                continue;
            }

            var excused = Excuse(line);

            foreach (var token in Tokens(Strip(line)))
            {
                if (!IsNumber(token))
                {
                    continue;
                }

                // Rule two. The number is in this lesson's own output, sitting a few lines up the
                // page, and somebody typed it again anyway. There is no reason worth hearing for
                // this one, so the excuse comment does not apply to it.
                if (Appears(token, captured))
                {
                    problems.Add($"{name}/{ProseName}:{i + 1}: '{token}' is in this lesson's captured output, so transclude the block instead of retyping it");
                    continue;
                }

                // Rule one. Any other bare number wants a reason, in a comment, in the diff.
                if (!excused)
                {
                    problems.Add($"{name}/{ProseName}:{i + 1}: '{token}' is a number typed into prose, transclude it or say why it is a literal: <!-- literal: a reason -->");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// Everything the lesson printed, as one blob. Rule two asks whether a number in the prose is
    /// already sitting in it.
    /// </summary>
    private static string Captured(string directory)
    {
        var expected = Path.Combine(directory, ExpectedDirectory);
        if (!Directory.Exists(expected))
        {
            return string.Empty;
        }

        return string.Join('\n', Directory.EnumerateFiles(expected, "*.txt")
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText));
    }

    /// <summary>
    /// True for a line carrying <c>&lt;!-- literal: some reason --&gt;</c>. The reason is required,
    /// because an escape hatch with nothing written in it is a checkbox.
    /// </summary>
    private static bool Excuse(string line)
    {
        var at = line.IndexOf(Literal, StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }

        var after = line[(at + Literal.Length)..];
        var end = after.IndexOf("-->", StringComparison.Ordinal);
        var reason = (end < 0 ? after : after[..end]).Trim();
        return reason.Length > 0;
    }

    /// <summary>
    /// Removes the parts of a markdown line that are addresses rather than prose. A link target and
    /// an image path are how a file is named, and a file is allowed digits in its name.
    /// </summary>
    private static string Strip(string line)
    {
        var text = new System.Text.StringBuilder(line.Length);
        var depth = 0;

        foreach (var c in line)
        {
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0)
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Words, where a word runs through a dot, a hyphen or an underscore that has something on the
    /// other side of it. So <c>v4.0.30319</c> and <c>UTF-16</c> and <c>II.24.2.1</c> come back
    /// whole, and a full stop at the end of a sentence does not join two of them together.
    /// </summary>
    private static IEnumerable<string> Tokens(string line)
    {
        var start = -1;

        for (var i = 0; i <= line.Length; i++)
        {
            var inside = i < line.Length && Part(line, i);

            if (inside && start < 0)
            {
                start = i;
            }
            else if (!inside && start >= 0)
            {
                yield return line[start..i];
                start = -1;
            }
        }
    }

    private static bool Part(string line, int i)
    {
        var c = line[i];
        if (char.IsAsciiLetterOrDigit(c))
        {
            return true;
        }

        // A joiner counts only between two word characters, which is what keeps the full stop that
        // ends a sentence out of the number in front of it.
        return c is '.' or '-' or '_'
            && i > 0 && char.IsAsciiLetterOrDigit(line[i - 1])
            && i + 1 < line.Length && char.IsAsciiLetterOrDigit(line[i + 1]);
    }

    /// <summary>
    /// A number is a token with digits in it and no letters anywhere.
    /// </summary>
    /// <remarks>
    /// A letter turns a token into a name. <c>x64</c> is a platform, <c>UTF-16</c> is an encoding,
    /// <c>M05</c> is a lesson, <c>II.24.2.1</c> is a clause of the standard, <c>v4.0.30319</c> is a
    /// version string and <c>0x1F</c> is written the way the runtime writes it. None of those is
    /// somebody reporting a measurement, and a rule that argued with them would be a rule people
    /// turned off.
    /// </remarks>
    private static bool IsNumber(string token) =>
        token.Any(char.IsAsciiDigit) && !token.Any(char.IsAsciiLetter);

    /// <summary>
    /// True if the captured output contains this number as a number, rather than as part of a
    /// longer one. Nine is not in ninety, and neither of them is in a hex dump that happens to
    /// carry those two digits next to each other.
    /// </summary>
    private static bool Appears(string token, string captured)
    {
        var from = 0;
        while (true)
        {
            var at = captured.IndexOf(token, from, StringComparison.Ordinal);
            if (at < 0)
            {
                return false;
            }

            var before = at == 0 || !char.IsAsciiLetterOrDigit(captured[at - 1]);
            var end = at + token.Length;
            var after = end >= captured.Length || !char.IsAsciiLetterOrDigit(captured[end]);
            if (before && after)
            {
                return true;
            }

            from = at + 1;
        }
    }
}
