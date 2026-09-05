namespace ClrXray;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Usage();
            return 2;
        }

        return args[0] switch
        {
            "banner" => Banner.Run(),
            "lint" => Lint.Run(args.Length > 1 ? args[1] : "."),
            "build" => Build.Run(Where(args), write: true, offline: args.Contains("--offline")),
            "check" => args.Contains("--selftest") ? CheckSelfTest.Run(Where(args)) : Build.Run(Where(args), write: false, offline: args.Contains("--offline")),
            "boss" => Fight(args),
            "cite" => args.Contains("--selftest") ? CiteSelfTest.Run() : Cite.Run(args.Length > 1 ? args[1] : "."),
            "numbers" => args.Contains("--selftest") ? NumbersSelfTest.Run() : Numbers.Run(args.Length > 1 ? args[1] : "lessons"),
            "assert" => args.Contains("--selftest") ? AssertSelfTest.Run() : Asserts.Run(args.Length > 1 ? args[1] : "lessons"),
            "--help" or "-h" or "help" => Usage(),
            _ => Unknown(args[0]),
        };
    }

    /// <summary>
    /// The path a command was pointed at, which is the first argument after the command that is
    /// not a flag. Everything defaults to the whole repository, because a build that quietly
    /// covers half of it is how a generated file goes stale without anybody being told.
    /// </summary>
    private static string Where(string[] args) =>
        args.Skip(1).FirstOrDefault(a => !a.StartsWith('-')) ?? ".";

    /// <summary>
    /// Grades a boss fight. This is the one command a reader runs at somebody, so it says which
    /// lesson it wants rather than guessing.
    /// </summary>
    private static int Fight(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("xray boss: say which lesson, as in: xray boss lessons/smoke-pipeline");
            return 2;
        }

        try
        {
            return Boss.Grade(args[1]);
        }
        catch (LessonException error)
        {
            Console.Error.WriteLine($"xray: {error.Message}");
            return 2;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("xray, the tool this book checks itself with.");
        Console.WriteLine();
        Console.WriteLine("  xray banner        Print the environment every claim on a page depends on.");
        Console.WriteLine("  xray lint [path]   Check the prose rules across every markdown file under path.");
        Console.WriteLine("  xray build [path]  The six step build over path: resolve, cite, execute, generate, assemble, check.");
        Console.WriteLine("  xray check [path]  The same six steps, and fail if the committed files differ from what they produce.");
        Console.WriteLine("      --offline      Skip the cite step, which is the one step that needs the network.");
        Console.WriteLine("  xray check --selftest   Hand edit a generated file on purpose and prove the check objects.");
        Console.WriteLine("  xray boss <path>   Grade your answer to one lesson's boss fight.");
        Console.WriteLine("  xray cite [path]   Resolve every citation under path against the two pinned repositories.");
        Console.WriteLine("  xray cite --selftest   Prove the citation gate still rejects what it is supposed to.");
        Console.WriteLine("  xray numbers [path]   Find numbers typed into lesson prose that should have been transcluded.");
        Console.WriteLine("  xray numbers --selftest   Prove the numbers gate still knows a measurement from a name.");
        Console.WriteLine("  xray assert [path]    Run the lessons under path and report every assertion, passing ones included.");
        Console.WriteLine("  xray assert --selftest   Prove the assertion checker still refuses output that does not hold.");
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"xray: no such command: {command}");
        Usage();
        return 2;
    }
}
