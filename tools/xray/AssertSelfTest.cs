using System.Text.Json;

namespace ClrXray;

/// <summary>
/// Proves the assertion checker still refuses what it is supposed to.
/// </summary>
/// <remarks>
/// This one needs its own test more than the other gates do. An assertion that is quietly vacuous
/// passes forever and reads on the page exactly like an assertion that is doing something, so the
/// failure mode here is not a red build, it is a list of guarantees under a block that guarantee
/// nothing. The cases below are half about accepting output that holds and half about refusing
/// output that does not, because only the second half can tell those two apart.
/// </remarks>
internal static class AssertSelfTest
{
    private static int failures;

    private const string Table =
        "UserString starts at   712 and runs for   104 bytes\n" +
        "String     starts at   316 and runs for   396 bytes\n" +
        "Blob       starts at   816 and runs for   148 bytes\n" +
        "Guid       starts at   800 and runs for    16 bytes\n";

    internal static int Run()
    {
        failures = 0;

        Holds("a line count that is right", new Invariant { Lines = 4, Why = "." }, Table);
        Breaks("a line count that is wrong", new Invariant { Lines = 5, Why = "." }, Table, "printed 4 line(s)");
        Holds("no lines at all, when that is what was asked for", new Invariant { Lines = 0, Why = "." }, string.Empty);
        Breaks("no lines at all, when a line was asked for", new Invariant { Lines = 1, Why = "." }, string.Empty, "printed 0 line(s)");

        // A trailing newline is how every captured block ends, so a rule about line counts that
        // could not cope with one would be a rule that is always off by one.
        Holds("one line with a newline on the end", new Invariant { Lines = 1, Why = "." }, "only this\n");
        Holds("one line with no newline on the end", new Invariant { Lines = 1, Why = "." }, "only this");

        Holds("text that is there", new Invariant { Contains = "#GUID", Why = "." }, "#GUID holds 16 bytes\n");
        Breaks("text that is not there", new Invariant { Contains = "#Blob", Why = "." }, "#GUID holds 16 bytes\n", "does not");

        Holds("text that is absent, and should be", new Invariant { Absent = "Exception", Why = "." }, Table);
        Breaks("text that is absent, and is not", new Invariant { Absent = "Guid", Why = "." }, Table, "does");

        // The pattern is the one that earns this whole feature: it pins the one number in a table
        // of numbers that is not allowed to move, and says nothing about the rest.
        Holds(
            "a pattern matching one line of many",
            new Invariant { Matches = @"^Guid .* runs for +16 bytes$", Why = "." },
            Table);

        Breaks(
            "a pattern that matches no line",
            new Invariant { Matches = @"^Guid .* runs for +32 bytes$", Why = "." },
            Table,
            "has no line that does");

        // Multiline, because ^ and $ meaning the ends of the whole blob would make every rule
        // about a table of output either wrong or unwritable.
        Breaks(
            "a pattern anchored to a line, against a line that only nearly matches",
            new Invariant { Matches = @"^Blob +starts at +[0-9]+ and runs for +[0-9]+ bytes! *$", Why = "." },
            Table,
            "has no line that does");

        Load("a file that is not there at all, for a lesson with nothing to assert", null, "stdout", null);
        Load("an entry naming a block that does not exist", """[{"block": "nope", "claims": [{"lines": 1, "why": "."}]}]""", "stdout", "no block named 'nope'");
        Load("an entry with no block name", """[{"claims": [{"lines": 1, "why": "."}]}]""", "stdout", "does not say which block");
        Load("an entry with no assertions in it", """[{"block": "one", "claims": []}]""", "stdout", "listed with no assertions");
        Load("the same block listed twice", """[{"block": "one", "claims": [{"lines": 1, "why": "."}]}, {"block": "one", "claims": [{"lines": 1, "why": "."}]}]""", "stdout", "listed twice");
        Load("an assertion with two rules in it", """[{"block": "one", "claims": [{"lines": 1, "contains": "x", "why": "."}]}]""", "stdout", "sets 2 of contains");
        Load("an assertion with no rule in it", """[{"block": "one", "claims": [{"why": "."}]}]""", "stdout", "sets 0 of contains");
        Load("an assertion with no why", """[{"block": "one", "claims": [{"lines": 1}]}]""", "stdout", "has no why");
        Load("an assertion whose why is blank", """[{"block": "one", "claims": [{"lines": 1, "why": "   "}]}]""", "stdout", "has no why");
        Load("a pattern that is not a regular expression", """[{"block": "one", "claims": [{"matches": "([", "why": "."}]}]""", "stdout", "is not a regular expression");
        Load("an assertion on a block whose output is not separated out", """[{"block": "one", "claims": [{"lines": 1, "why": "."}]}]""", "none", "capture=none");

        // The rule this file exists for. Nothing else in the build looks at what a dropped block
        // printed, so a dropped block nobody has asserted anything about is unchecked output.
        Load("a dropped block with nothing asserted about it", null, "drop", "nothing checks what it prints");
        Load("a dropped block with something asserted about it", """[{"block": "one", "claims": [{"lines": 1, "why": "."}]}]""", "drop", null);

        Console.WriteLine($"xray assert --selftest: {failures} failure(s)");
        return failures == 0 ? 0 : 1;
    }

    private static void Holds(string what, Invariant claim, string output)
    {
        var broke = Asserts.Fails(claim, output);
        Check(broke is null, $"accepts {what}", $"it said the output {broke}");
    }

    private static void Breaks(string what, Invariant claim, string output, string expected)
    {
        var broke = Asserts.Fails(claim, output);
        if (broke is null)
        {
            Check(false, $"refuses {what}", "it accepted it");
            return;
        }

        Check(
            broke.Contains(expected, StringComparison.Ordinal),
            $"refuses {what}",
            $"the reason was '{broke}', which does not mention '{expected}'");
    }

    /// <summary>
    /// Writes a throwaway lesson with one block at the given capture setting, loads whatever
    /// assertions file goes with it, and checks that it was accepted or refused for the stated
    /// reason. An expectation of nothing means it should load.
    /// </summary>
    private static void Load(string what, string? json, string capture, string? expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"xray-assert-selftest-{Environment.ProcessId}");
        Directory.CreateDirectory(directory);

        try
        {
            var source = Path.Combine(directory, Lessons.SourceName);
            File.WriteAllText(source, $"//# block id=one env=E0 capture={capture}\nConsole.WriteLine(\"hello\");\n//# end\n");

            if (json is not null)
            {
                File.WriteAllText(Path.Combine(directory, Asserts.FileName), json);
            }

            var blocks = Blocks.Parse(source, File.ReadAllLines(source));

            try
            {
                _ = Asserts.Load(directory, blocks);
                Check(expected is null, expected is null ? $"loads {what}" : $"refuses {what}", "it loaded");
            }
            catch (LessonException error)
            {
                if (expected is null)
                {
                    Check(false, $"loads {what}", $"it refused it: {error.Message}");
                    return;
                }

                Check(
                    error.Message.Contains(expected, StringComparison.Ordinal),
                    $"refuses {what}",
                    $"the message was '{error.Message}', which does not mention '{expected}'");
            }
        }
        catch (JsonException error)
        {
            Check(false, what, $"the fixture json is malformed: {error.Message}");
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
