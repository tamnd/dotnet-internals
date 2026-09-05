// This is your file. Edit it until the grader stops complaining:
//
//     dotnet run --project tools/xray -- boss lessons/m03-four-heaps
//
// You can also run it on its own from the lesson directory, once the fixture is built:
//
//     dotnet build fixture -c Release
//     dotnet run boss/boss.cs
//
// What you need, and nothing else:
//
//   #US starts with one zero byte, so an offset of zero can mean "no string".
//   Every entry after that is a compressed length, then that many bytes.
//   The last of those bytes is a flag, not text, so the text is length - 1 bytes of UTF-16.
//   The heap is padded to a four byte boundary with zeros, so a zero length is the end.
//
// A compressed unsigned integer is ECMA-335 II.23.2. One byte if the top bit is clear. Two bytes
// if the top two bits are 10, and the value is the low six bits of the first byte followed by the
// second. Four bytes if the top three bits are 110, and the value is the low five bits followed by
// the next three. Write that once and you will use it for the rest of Part II.

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

// The build says which directory this lesson is in. It has to, because dotnet run does not
// promise the working directory of a file you point it at, and this file lives one level down.
var here = Environment.GetEnvironmentVariable("XRAY_HERE") ?? "..";

var file = Directory.GetFiles(Path.Combine(here, "fixture", "bin", "Release"), "Sample.dll", SearchOption.AllDirectories)[0];

using var stream = File.OpenRead(file);
using var image = new PEReader(stream);

var reader = image.GetMetadataReader();
var metadata = image.GetMetadata().GetContent();

var start = reader.GetHeapMetadataOffset(HeapIndex.UserString);
var size = reader.GetHeapSize(HeapIndex.UserString);

// Yours. Walk the heap from offset 1 and collect what you find. Each entry is a piece of text and
// the number of bytes it occupied, prefix and flag included.
var found = new List<(string Text, int Bytes)>();

// Yours, once found has something in it.
var count = 0;
var longest = "nothing yet";
var bytes = 0;

Console.WriteLine($"answer count = {count}");
Console.WriteLine($"answer longest = {longest}");
Console.WriteLine($"answer bytes = {bytes}");

// Yours. Return the value and set width to how many bytes it took.
int Compressed(int index, out int width)
{
    width = 1;
    return metadata[index];
}

// Keeps the compiler quiet about the parts you have not used yet. Delete it once you have.
_ = (found, size, Compressed(start, out _));
