namespace Sample;

public static class Text
{
    // This is the line. Everything on the lesson page next door is about what happens to it, and
    // nothing else in this file is doing any work.
    public static int Count(string line) => line.Split(' ').Length;
}

public static class Program
{
    public static void Main() => Console.WriteLine(Text.Count("the quick brown fox"));
}
