// Everything in this file is here to be found again in one of the four heaps.
//
// The namespace and the two type names go into #Strings. So do every method name, every parameter
// name and the name of the assembly itself.
//
// The string literals go into #US, which is a different heap, and that is the fact this lesson is
// mostly about. One of them appears twice on purpose.
//
// The shape of Count, two parameters and a return value, becomes a signature in #Blob.
//
// Nothing here puts anything into #GUID. The compiler does that on its own, exactly once.

namespace Sample;

public sealed class Catalogue
{
    public string Greeting => "the quick brown fox";

    public static string Describe() => "the quick brown fox";

    public int Count(string name, int start) => start + name.Length;

    public string Section => "II.24";
}

public sealed class Shelf
{
    public string Label => "heap";
}
