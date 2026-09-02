// M03, the four heaps. Where the strings and the blobs live.
//
// You can run this the ordinary way, with none of this repository's tooling in the picture:
//
//     dotnet build fixture -c Release
//     dotnet run lesson.cs
//
// The directives below are comments, so that prints every block in order.
//
// Everything here reads the fixture assembly next door. Nothing here needs a runtime built from
// source, a debugger, or a privilege you do not already have.

//# block id=usings env=E0 tags=[metadata] capture=none
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
//# end

//# block id=open env=E0 tags=[metadata]
// The fixture is built before this runs, so there is exactly one Sample.dll under it.
var file = Directory.GetFiles(Path.Combine("fixture", "bin", "Release"), "Sample.dll", SearchOption.AllDirectories)[0];

using var stream = File.OpenRead(file);
using var image = new PEReader(stream);

// Two views of the same bytes. The reader is the polite one. The byte array is the whole metadata
// section, and every offset in this lesson is an index into it.
var reader = image.GetMetadataReader();
var metadata = image.GetMetadata().GetContent();

Console.WriteLine($"metadata version: {reader.MetadataVersion}");
Console.WriteLine($"type definitions: {reader.TypeDefinitions.Count}");

foreach (var handle in reader.TypeDefinitions)
{
    var type = reader.GetTypeDefinition(handle);
    var space = reader.GetString(type.Namespace);
    Console.WriteLine($"  {(space.Length == 0 ? string.Empty : space + ".")}{reader.GetString(type.Name)}");
}
//# end

// Every number in this block moves when the compiler moves, so it is dropped rather than stored.
// The prose says what you will see instead. This is the ordinary case for anything that is a
// position in a file rather than a property of the format.
//# block id=sizes env=E0 tags=[metadata] capture=drop
foreach (var heap in Enum.GetValues<HeapIndex>())
{
    Console.WriteLine($"{heap,-10} starts at {reader.GetHeapMetadataOffset(heap),5} and runs for {reader.GetHeapSize(heap),5} bytes");
}
//# end

//# block id=name env=E0 tags=[metadata]
var catalogue = reader.TypeDefinitions
    .Select(reader.GetTypeDefinition)
    .First(type => reader.GetString(type.Name) == "Catalogue");

var stringsStart = reader.GetHeapMetadataOffset(HeapIndex.String);
var stringsSize = reader.GetHeapSize(HeapIndex.String);

// The row gave us a number. Everything from here is us, walking the bytes by hand.
var nameOffset = MetadataTokens.GetHeapOffset(catalogue.Name);
var end = nameOffset;
while (metadata[stringsStart + end] != 0)
{
    end++;
}

var raw = metadata.AsSpan(stringsStart + nameOffset, end - nameOffset).ToArray();

Console.WriteLine($"bytes at that offset: {Convert.ToHexString(raw)}");
Console.WriteLine($"the byte after them:  {metadata[stringsStart + end]:X2}");
Console.WriteLine($"decoded as UTF-8:     {Encoding.UTF8.GetString(raw)}");
Console.WriteLine($"and the reader says:  {reader.GetString(catalogue.Name)}");
//# end

//# block id=suffix env=E0 tags=[metadata]
var greeting = reader.PropertyDefinitions
    .Select(reader.GetPropertyDefinition)
    .First(property => reader.GetString(property.Name) == "Greeting");

var getter = reader.MethodDefinitions
    .Select(reader.GetMethodDefinition)
    .First(method => reader.GetString(method.Name) == "get_Greeting");

var propertyAt = MetadataTokens.GetHeapOffset(greeting.Name);
var methodAt = MetadataTokens.GetHeapOffset(getter.Name);

Console.WriteLine($"the two offsets differ by {propertyAt - methodAt}");
Console.WriteLine($"bytes at the method's offset:   {Convert.ToHexString(Tail(methodAt))}");
Console.WriteLine($"bytes at the property's offset: {Convert.ToHexString(Tail(propertyAt))}");

byte[] Tail(int offset)
{
    var stop = offset;
    while (metadata[stringsStart + stop] != 0)
    {
        stop++;
    }

    return metadata.AsSpan(stringsStart + offset, stop - offset + 1).ToArray();
}
//# end

//# block id=us env=E0 tags=[metadata]
var userStart = reader.GetHeapMetadataOffset(HeapIndex.UserString);
var userSize = reader.GetHeapSize(HeapIndex.UserString);

// The heap opens with a single zero byte, so an offset of zero can mean "there is no string here".
// Every entry after that is a length, then that many bytes, and the last of them is a flag.
//
// The length is a compressed integer, and this walk pretends it is always one byte. That is true
// for every string in this assembly and it is not true in general. Making it true in general is
// the boss fight.
var at = 1;
while (at < userSize)
{
    var length = metadata[userStart + at];

    // The heap is padded up to a four byte boundary with zeros, so a length of zero is the end of
    // the strings and the start of the padding. Miss this and the walk runs off into it.
    if (length == 0)
    {
        break;
    }

    var text = Encoding.Unicode.GetString(metadata.AsSpan(userStart + at + 1, length - 1).ToArray());
    var flag = metadata[userStart + at + length];

    Console.WriteLine($"{length,3} bytes, {text.Length,2} characters, flag {flag}: {text}");
    at += 1 + length;
}
//# end

//# block id=separate env=E0 tags=[metadata]
var names = metadata.AsSpan(stringsStart, stringsSize);
var literals = metadata.AsSpan(userStart, userSize);

var literalUtf8 = Encoding.UTF8.GetBytes("the quick brown fox");
var literalUtf16 = Encoding.Unicode.GetBytes("the quick brown fox");
var nameUtf8 = Encoding.UTF8.GetBytes("Catalogue");
var nameUtf16 = Encoding.Unicode.GetBytes("Catalogue");

Console.WriteLine($"the literal, as UTF-8, inside #Strings:   {names.IndexOf(literalUtf8) >= 0}");
Console.WriteLine($"the literal, as UTF-16, inside #US:       {literals.IndexOf(literalUtf16) >= 0}");
Console.WriteLine($"the type name, as UTF-8, inside #Strings: {names.IndexOf(nameUtf8) >= 0}");
Console.WriteLine($"the type name, as UTF-16, inside #US:     {literals.IndexOf(nameUtf16) >= 0}");
//# end

//# block id=blob env=E0 tags=[metadata]
var count = reader.MethodDefinitions
    .Select(reader.GetMethodDefinition)
    .First(method => reader.GetString(method.Name) == "Count");

var signature = reader.GetBlobBytes(count.Signature);

Console.WriteLine($"int Count(string, int) is {signature.Length} bytes: {Convert.ToHexString(signature)}");
Console.WriteLine($"  {signature[0]:X2}  calling convention, the 0x20 bit is HASTHIS");
Console.WriteLine($"  {signature[1]:X2}  parameter count");
Console.WriteLine($"  {signature[2]:X2}  return type, ELEMENT_TYPE_I4");
Console.WriteLine($"  {signature[3]:X2}  first parameter, ELEMENT_TYPE_STRING");
Console.WriteLine($"  {signature[4]:X2}  second parameter, ELEMENT_TYPE_I4");

// Three property getters, three different names, one shape. The blob heap stores the shape once.
var getters = new[] { "get_Greeting", "get_Section", "get_Label" };
var shapes = getters
    .Select(name => reader.MethodDefinitions.Select(reader.GetMethodDefinition).First(method => reader.GetString(method.Name) == name))
    .Select(method => MetadataTokens.GetHeapOffset(method.Signature))
    .Distinct()
    .Count();

Console.WriteLine($"{getters.Length} getters, {shapes} signature blob between them");
//# end

//# block id=guid env=E0 tags=[metadata]
var guidSize = reader.GetHeapSize(HeapIndex.Guid);
var module = reader.GetModuleDefinition();

Console.WriteLine($"#GUID holds {guidSize} bytes, so {guidSize / 16} entry of 16");
Console.WriteLine($"the module's Mvid is entry number {MetadataTokens.GetHeapOffset(module.Mvid)}, and this heap counts from 1");
Console.WriteLine($"entry 0 is not a GUID, it is how a row says it has none");
//# end

// The Mvid is a fresh identity for every distinct build, which is the entire point of it, so there
// is nothing here that could be an expected file.
//# block id=mvid env=E0 tags=[metadata] capture=drop
Console.WriteLine(reader.GetGuid(module.Mvid));
//# end
