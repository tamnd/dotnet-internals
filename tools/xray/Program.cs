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
            "--help" or "-h" or "help" => Usage(),
            _ => Unknown(args[0]),
        };
    }

    private static int Usage()
    {
        Console.WriteLine("xray, the tool this book checks itself with.");
        Console.WriteLine();
        Console.WriteLine("  xray banner        Print the environment every claim on a page depends on.");
        Console.WriteLine("  xray lint [path]   Check the prose rules across every markdown file under path.");
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"xray: no such command: {command}");
        Usage();
        return 2;
    }
}
