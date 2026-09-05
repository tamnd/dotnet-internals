namespace ClrXray;

/// <summary>
/// Proves the numbers gate still knows the difference between a measurement and a name.
/// </summary>
/// <remarks>
/// This gate is one loosened rule away from passing everything, and the day it does the repository
/// keeps printing a green tick under the sentence that no number in a lesson is typed by a person.
/// So the cases it has to get right are written down as lessons on disk and run, rather than being
/// remembered by whoever last touched the file.
/// </remarks>
internal static class NumbersSelfTest
{
    private static int failures;

    internal static int Run()
    {
        failures = 0;

        // Rule two, which is the one with teeth. The number is sitting in this lesson's own output
        // and somebody typed it into the prose anyway.
        Case(
            "a number that is already in the output",
            "The header is 16 bytes wide.",
            "#GUID holds 16 bytes\n",
            ["captured output"]);

        Case(
            "the same number with an excuse on the line",
            "The header is 16 bytes wide. <!-- literal: I would rather not -->",
            "#GUID holds 16 bytes\n",
            ["captured output"]);

        // Rule one, and the escape hatch.
        Case(
            "a number that is not in the output and has no reason",
            "The header is 24 bytes wide.",
            string.Empty,
            ["typed into prose"]);

        Case(
            "a number with a reason written down",
            "ECMA-335 was last revised in 2012. <!-- literal: the year the standard was published -->",
            string.Empty,
            []);

        Case(
            "a reason with nothing in it",
            "ECMA-335 was last revised in 2012. <!-- literal: -->",
            string.Empty,
            ["typed into prose"]);

        // Names with digits in them, which is most of what a page about a runtime is full of.
        Case(
            "names that happen to contain digits",
            "On x64 and arm64, a UTF-16 literal in v4.0.30319 metadata, per II.24.2.4, is read by M05.",
            string.Empty,
            []);

        Case(
            "a number at the end of a sentence, without the full stop stuck to it",
            "The offset is 24.",
            string.Empty,
            ["'24'"]);

        Case(
            "a link whose target has digits in it",
            "See [the table](../tables/24.md) for the rest.",
            string.Empty,
            []);

        Case(
            "a fenced block full of digits",
            "Like this:\n\n```\n16 24 32\n```\n\nAnd that is all.",
            string.Empty,
            []);

        Case(
            "a transclusion, which is the thing the gate is asking for",
            "{{output:name}}",
            "42\n",
            []);

        Console.WriteLine($"xray numbers --selftest: {failures} failure(s)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Writes one throwaway lesson, checks it, and compares what came back against what should
    /// have. An expectation of nothing means the prose is supposed to pass.
    /// </summary>
    private static void Case(string what, string prose, string captured, string[] expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"xray-numbers-selftest-{Environment.ProcessId}");
        Directory.CreateDirectory(Path.Combine(directory, "expected"));

        try
        {
            // The front matter goes in every case, because it is full of digits that are names and
            // a gate that tripped on its own header would never have got this far.
            File.WriteAllText(
                Path.Combine(directory, "lesson.src.md"),
                $"---\nid: m03-four-heaps\nnumber: 3\nplatforms: [linux-x64, win-x64]\n---\n\n{prose}\n");

            if (captured.Length > 0)
            {
                File.WriteAllText(Path.Combine(directory, "expected", "out.txt"), captured);
            }

            var found = Numbers.Check(directory);

            if (expected.Length == 0)
            {
                Check(found.Count == 0, $"accepts {what}", found.Count == 0 ? string.Empty : found[0]);
                return;
            }

            if (found.Count != expected.Length)
            {
                Check(false, $"refuses {what}", $"expected {expected.Length} problem(s) and got {found.Count}: {string.Join(" | ", found)}");
                return;
            }

            foreach (var (fragment, message) in expected.Zip(found))
            {
                Check(
                    message.Contains(fragment, StringComparison.Ordinal),
                    $"refuses {what}",
                    $"the message was '{message}', which does not mention '{fragment}'");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Check(bool passed, string what, string why)
    {
        if (passed)
        {
            Console.WriteLine($"  ok    {what}");
            return;
        }

        failures++;
        Console.Error.WriteLine($"  FAIL  {what}: {why}");
    }
}
