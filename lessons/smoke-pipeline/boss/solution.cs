// The worked answer. Reading this before you have tried the fight yourself is allowed and is a
// worse way to learn it, because the thing being trained is the reaching, not the knowing.
//
// This file is also what generates answers.txt, so it is run on every build. If you change it and
// the answers move, the build says so.

var directives = new List<string>();

foreach (var line in File.ReadAllLines("lesson.cs"))
{
    var trimmed = line.Trim();
    if (trimmed.StartsWith("//# block", StringComparison.Ordinal))
    {
        directives.Add(trimmed);
    }
}

// A block with no capture setting stores its output, because that is the common case and the
// common case is the one that should need no words. The other two settings have to say so.
var stored = directives.Count(directive => Attribute(directive, "capture", "stdout") == "stdout");

Console.WriteLine($"answer directives = {directives.Count}");
Console.WriteLine($"answer stored = {stored}");
Console.WriteLine($"answer first = {Attribute(directives[0], "id", "")}");

// The directive is a line of space separated words, and each attribute is one word with an equals
// sign in it. That is a deliberately small grammar. A format you can parse in six lines is a
// format nobody has to look up.
static string Attribute(string directive, string name, string fallback)
{
    foreach (var word in directive.Split(' ', StringSplitOptions.RemoveEmptyEntries))
    {
        if (word.StartsWith(name + "=", StringComparison.Ordinal))
        {
            return word[(name.Length + 1)..];
        }
    }

    return fallback;
}
