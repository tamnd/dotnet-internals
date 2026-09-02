using System.Text;

namespace ClrXray;

/// <summary>
/// One named region of a lesson's source file. A block is the unit the book quotes, the unit it
/// runs, and the unit whose output it stores, and those three are the same region of the same
/// file so that a listing on the page cannot drift from the program that produced the numbers
/// underneath it.
/// </summary>
internal sealed class Block
{
    internal required string Id { get; init; }

    /// <summary>
    /// Which environment the block needs. E0 is the stock SDK, E1 adds a runtime built from
    /// source, E2 adds a checked build. A lesson made entirely of E0 blocks is a lesson a reader
    /// can finish on the machine they already have.
    /// </summary>
    internal string Env { get; init; } = "E0";

    internal IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// One of three words. <c>stdout</c> stores what the block printed and lets the page quote
    /// it. <c>drop</c> runs the block and throws the output away, which is what a block that
    /// prints something different on every machine needs. <c>none</c> also stops the runner
    /// marking the block at all, which is what a block holding a type or a helper needs, because
    /// a marker is a statement and a statement cannot follow a declaration in a file of top level
    /// statements.
    /// </summary>
    internal string Capture { get; init; } = Blocks.Stdout;

    /// <summary>Index of the opening directive line in the source file.</summary>
    internal int DirectiveLine { get; init; }

    /// <summary>The lines between the directives, with no directive lines in them.</summary>
    internal string Source { get; init; } = string.Empty;
}

/// <summary>
/// Reads the block directives out of a lesson source file, and writes the version of that file
/// that the runner actually executes.
/// </summary>
/// <remarks>
/// The directive is a comment, so the file compiles and runs as an ordinary program whether or
/// not this tool is involved. A reader who clones the repository and types
/// <c>dotnet run lesson.cs</c> gets the whole lesson in one go, and the block boundaries are
/// invisible to them.
/// </remarks>
internal static class Blocks
{
    internal const string MarkerPrefix = "\u001Fxray:block:";

    internal const string Stdout = "stdout";
    internal const string Drop = "drop";
    internal const string None = "none";

    private const string Open = "//# block";
    private const string Close = "//# end";
    private const string RunDirectory = ".xray";

    /// <summary>
    /// Parses a lesson source file. Throws <see cref="LessonException"/> on a malformed
    /// directive, because a lesson with a broken directive has no correct output to fall back on.
    /// </summary>
    internal static IReadOnlyList<Block> Parse(string path, IReadOnlyList<string> lines)
    {
        var blocks = new List<Block>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var open = -1;
        var id = string.Empty;
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        var body = new List<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();

            if (trimmed.StartsWith(Open, StringComparison.Ordinal))
            {
                if (open >= 0)
                {
                    throw new LessonException($"{path}:{i + 1}: a block opened while block '{id}' was still open");
                }

                attributes = ParseAttributes(path, i, trimmed[Open.Length..]);
                if (!attributes.TryGetValue("id", out var declared))
                {
                    throw new LessonException($"{path}:{i + 1}: block directive with no id");
                }

                id = declared;
                if (!seen.Add(id))
                {
                    throw new LessonException($"{path}:{i + 1}: duplicate block id '{id}'");
                }

                open = i;
                body.Clear();
                continue;
            }

            if (trimmed.StartsWith(Close, StringComparison.Ordinal))
            {
                if (open < 0)
                {
                    throw new LessonException($"{path}:{i + 1}: block end with no block open");
                }

                blocks.Add(new Block
                {
                    Id = id,
                    Env = attributes.TryGetValue("env", out var env) ? env : "E0",
                    Tags = attributes.TryGetValue("tags", out var tags) ? SplitList(tags) : [],
                    Capture = Capture(path, i, attributes),
                    DirectiveLine = open,
                    Source = Trim(body),
                });

                open = -1;
                continue;
            }

            if (open >= 0)
            {
                body.Add(lines[i]);
            }
        }

        if (open >= 0)
        {
            throw new LessonException($"{path}:{open + 1}: block '{id}' is never closed");
        }

        if (blocks.Count == 0)
        {
            throw new LessonException($"{path}: no blocks, so there is nothing to run or to quote");
        }

        return blocks;
    }

    /// <summary>
    /// Produces the source the runner executes: the original file with one extra line at the top
    /// of each block that prints a marker. The marker is how one run of one program turns into
    /// one captured output file per block, without the lesson having to say anything about it.
    /// </summary>
    /// <remarks>
    /// A block with <c>capture=none</c> gets no marker, and that is what makes it the place to
    /// put a type or a helper. A statement cannot go after a type declaration in a file of top
    /// level statements, so a block holding a declaration has to be one this method leaves alone.
    /// </remarks>
    internal static string Rewrite(IReadOnlyList<string> lines, IReadOnlyList<Block> blocks)
    {
        var markers = blocks.Where(b => b.Capture != None).ToDictionary(b => b.DirectiveLine, b => b.Id);
        var text = new StringBuilder();

        for (var i = 0; i < lines.Count; i++)
        {
            text.Append(lines[i]).Append('\n');
            if (markers.TryGetValue(i, out var id))
            {
                text.Append("Console.WriteLine(\"\\u001Fxray:block:").Append(id).Append("\");\n");
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Runs a block file once and hands back what each block printed.
    /// </summary>
    /// <remarks>
    /// One process, not one per block, because the file is a program and the fourth block usually
    /// depends on what the first one opened. The rewritten copy goes in a scratch directory that
    /// is deleted afterwards, so a failed run does not leave something behind that the next run
    /// picks up and nobody notices.
    /// </remarks>
    internal static Dictionary<string, string> Execute(string directory, IReadOnlyList<string> lines, IReadOnlyList<Block> blocks, string what)
    {
        var runDirectory = Path.Combine(directory, RunDirectory);
        var runFile = Path.Combine(runDirectory, "run.cs");

        try
        {
            System.IO.Directory.CreateDirectory(runDirectory);
            File.WriteAllText(runFile, Rewrite(lines, blocks));

            var (exit, stdout, stderr) = Runner.Dotnet(directory, ["run", Path.Combine(RunDirectory, "run.cs")]);
            if (exit != 0)
            {
                throw new LessonException($"{Path.GetFileName(directory)}: the {what} exited with {exit}\n{stderr}");
            }

            return Split(stdout);
        }
        finally
        {
            if (System.IO.Directory.Exists(runDirectory))
            {
                System.IO.Directory.Delete(runDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Cuts one program's output into one piece per block. Everything before the first marker is
    /// dropped, which is what makes build chatter from a cold restore harmless.
    /// </summary>
    private static Dictionary<string, string> Split(string stdout)
    {
        var captured = new Dictionary<string, string>(StringComparer.Ordinal);
        var current = (string?)null;
        var body = new List<string>();

        foreach (var line in stdout.Split('\n'))
        {
            if (line.StartsWith(MarkerPrefix, StringComparison.Ordinal))
            {
                Flush();
                current = line[MarkerPrefix.Length..].Trim();
                body.Clear();
                continue;
            }

            if (current is not null)
            {
                body.Add(line);
            }
        }

        Flush();
        return captured;

        void Flush()
        {
            if (current is null)
            {
                return;
            }

            while (body.Count > 0 && body[^1].Length == 0)
            {
                body.RemoveAt(body.Count - 1);
            }

            captured[current] = body.Count == 0 ? string.Empty : string.Join('\n', body) + "\n";
        }
    }

    private static string Capture(string path, int line, Dictionary<string, string> attributes)
    {
        var capture = attributes.TryGetValue("capture", out var value) ? value : Stdout;
        if (capture is not (Stdout or Drop or None))
        {
            throw new LessonException($"{path}:{line + 1}: capture is one of {Stdout}, {Drop} or {None}, not '{capture}'");
        }

        return capture;
    }

    private static Dictionary<string, string> ParseAttributes(string path, int line, string rest)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var part in rest.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                throw new LessonException($"{path}:{line + 1}: '{part}' is not name=value");
            }

            attributes[part[..equals]] = part[(equals + 1)..];
        }

        return attributes;
    }

    private static string[] SplitList(string value)
    {
        var inner = value.StartsWith('[') && value.EndsWith(']') ? value[1..^1] : value;
        return inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Drops blank lines at the top and bottom of a block so that the listing on the page starts
    /// at the first line of code rather than at whatever spacing the source file wanted.
    /// </summary>
    private static string Trim(List<string> body)
    {
        var first = 0;
        var last = body.Count - 1;

        while (first <= last && body[first].Trim().Length == 0)
        {
            first++;
        }

        while (last >= first && body[last].Trim().Length == 0)
        {
            last--;
        }

        return first > last ? string.Empty : string.Join('\n', body.GetRange(first, last - first + 1));
    }
}

/// <summary>
/// Raised when a lesson is malformed in a way that has no sensible partial result.
/// </summary>
internal sealed class LessonException : Exception
{
    internal LessonException(string message)
        : base(message)
    {
    }

    internal LessonException()
    {
    }

    internal LessonException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
