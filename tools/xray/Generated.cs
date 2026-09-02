namespace ClrXray;

/// <summary>
/// The half of a build that is the same whether the thing being generated is a lesson page, a
/// captured run or a picture. Writing it and checking it are one method with one flag, which is
/// the only way the two can be guaranteed not to drift apart.
/// </summary>
internal static class Generated
{
    /// <summary>
    /// Writes the file, or reports the first place it differs from what is on disk.
    /// </summary>
    internal static int Settle(string path, string content, bool write)
    {
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);

        if (write)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var before = File.Exists(path) ? Normalise(File.ReadAllText(path)) : null;
            if (before != content)
            {
                File.WriteAllText(path, content);
                Console.WriteLine($"  wrote {relative}");
            }

            return 0;
        }

        if (!File.Exists(path))
        {
            // The first segment of the path is the thing to rebuild, so the message names the
            // command that produces this particular file rather than one that produces some of
            // them and leaves somebody wondering why nothing happened.
            var where = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            Console.Error.WriteLine($"{relative}: missing, run: dotnet run --project tools/xray -- build {where}");
            return 1;
        }

        var actual = Normalise(File.ReadAllText(path));
        if (actual == content)
        {
            return 0;
        }

        Console.Error.WriteLine($"{relative}: does not match what the code produces");
        Report(content, actual);
        return 1;
    }

    private static void Report(string wanted, string found)
    {
        var a = wanted.Split('\n');
        var b = found.Split('\n');
        var shown = 0;

        for (var i = 0; i < Math.Max(a.Length, b.Length) && shown < 3; i++)
        {
            var left = i < a.Length ? a[i] : "(end of file)";
            var right = i < b.Length ? b[i] : "(end of file)";
            if (left == right)
            {
                continue;
            }

            Console.Error.WriteLine($"  line {i + 1} produced: {left}");
            Console.Error.WriteLine($"  line {i + 1} on disk:  {right}");
            shown++;
        }
    }

    /// <summary>
    /// One line ending, everywhere. Windows would otherwise disagree with the other three
    /// platforms about every line of every expected file, which is a real difference about
    /// nothing.
    /// </summary>
    internal static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
