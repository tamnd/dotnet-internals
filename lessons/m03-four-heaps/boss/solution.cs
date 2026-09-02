// The worked answer. Reading it before you have tried the fight yourself is allowed and is a worse
// way to learn it, because the thing being trained is the reaching, not the knowing.
//
// This file is also what generates answers.txt, so it runs on every build.

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

var file = Directory.GetFiles(Path.Combine("fixture", "bin", "Release"), "Sample.dll", SearchOption.AllDirectories)[0];

using var stream = File.OpenRead(file);
using var image = new PEReader(stream);

var reader = image.GetMetadataReader();
var metadata = image.GetMetadata().GetContent();

var start = reader.GetHeapMetadataOffset(HeapIndex.UserString);
var size = reader.GetHeapSize(HeapIndex.UserString);

var found = new List<(string Text, int Bytes)>();
var at = 1;

while (at < size)
{
    // A zero byte here is the padding that runs to the next four byte boundary, not a string.
    if (metadata[start + at] == 0)
    {
        break;
    }

    var length = Compressed(start + at, out var prefix);

    // The stored length counts the UTF-16 bytes plus the one flag byte on the end.
    var text = Encoding.Unicode.GetString(metadata.AsSpan(start + at + prefix, length - 1).ToArray());

    found.Add((text, prefix + length));
    at += prefix + length;
}

var longest = found.OrderByDescending(entry => entry.Text.Length).First();

Console.WriteLine($"answer count = {found.Count}");
Console.WriteLine($"answer longest = {longest.Text}");
Console.WriteLine($"answer bytes = {longest.Bytes}");

// ECMA-335 II.23.2. The top bits of the first byte say how wide the number is, and the remaining
// bits of that byte are the top of the value. Nothing else in the format needs a different rule,
// which is why it is worth writing once and remembering.
int Compressed(int index, out int width)
{
    var first = metadata[index];

    if ((first & 0x80) == 0)
    {
        width = 1;
        return first;
    }

    if ((first & 0x40) == 0)
    {
        width = 2;
        return ((first & 0x3F) << 8) | metadata[index + 1];
    }

    width = 4;
    return ((first & 0x1F) << 24)
        | (metadata[index + 1] << 16)
        | (metadata[index + 2] << 8)
        | metadata[index + 3];
}
