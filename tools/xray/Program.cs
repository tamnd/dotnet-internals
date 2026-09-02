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
            "build" => LessonCommand.Run(args.Length > 1 ? args[1] : "lessons", write: true),
            "check" => LessonCommand.Run(args.Length > 1 ? args[1] : "lessons", write: false),
            "boss" => Fight(args),
            "--help" or "-h" or "help" => Usage(),
            _ => Unknown(args[0]),
        };
    }

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
        Console.WriteLine("  xray build [path]  Draw the diagrams, run the lessons and regenerate the blueprints under path.");
        Console.WriteLine("  xray check [path]  The same work, and fail if the committed files differ from it.");
        Console.WriteLine("  xray boss <path>   Grade your answer to one lesson's boss fight.");
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"xray: no such command: {command}");
        Usage();
        return 2;
    }
}
