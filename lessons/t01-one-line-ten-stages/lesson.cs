// T01, one line, ten stages. What happens between a line of C# and a CPU.
//
// You can run this the ordinary way, with none of this repository's tooling in the picture:
//
//     dotnet build fixture -c Release
//     dotnet run lesson.cs
//
// The directives below are comments, so that prints every block in order.
//
// Everything here looks at the program in fixture/, first as a file and then as a thing running
// inside this process. Nothing needs a debugger, a build of the runtime, or a privilege you do
// not already have.

//# block id=usings env=E0 tags=[tour] capture=none
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
//# end

// Stage one. The line is text, and text is bytes.
//# block id=source env=E0 tags=[tour]
// The build says which directory this lesson is in. It has to, because dotnet run does not
// promise the working directory of a file you point it at. Run this by hand from the lesson
// directory and the dot is the same answer.
var here = Environment.GetEnvironmentVariable("XRAY_HERE") ?? ".";

var sourceFile = Path.Combine(here, "fixture", "Sample.cs");
var line = File.ReadAllLines(sourceFile).First(text => text.Contains("Count(string line)", StringComparison.Ordinal)).Trim();

Console.WriteLine(line);
Console.WriteLine($"{Encoding.UTF8.GetByteCount(line)} bytes of UTF-8, and nothing else");
//# end

// Stage two through five all read the same built file, so it gets opened once here.
//# block id=open env=E0 tags=[tour] capture=none
var built = Path.Combine(here, "fixture", "bin", "Release");
var assemblyFile = Directory.GetFiles(built, "Sample.dll", SearchOption.AllDirectories)[0];
var output = Path.GetDirectoryName(assemblyFile)!;

using var stream = File.OpenRead(assemblyFile);
using var image = new PEReader(stream);
var reader = image.GetMetadataReader();
//# end

// Stage two. The compiler read the line and resolved the names in it, and what it resolved them
// to is written down in the file it produced.
//# block id=overload env=E0 tags=[tour]
var count = reader.MethodDefinitions
    .Select(reader.GetMethodDefinition)
    .First(method => reader.GetString(method.Name) == "Count");

var body = image.GetMethodBody(count.RelativeVirtualAddress);
var il = body.GetILBytes()!;

// The call instruction is 0x6F, and the four bytes after it are a metadata token naming what is
// being called. Finding it this way rather than by counting is the point: the token is in the
// bytes, so nobody has to be trusted about it.
var callAt = Array.IndexOf(il, (byte)0x6F);
var token = BitConverter.ToInt32(il, callAt + 1);
var target = reader.GetMemberReference((MemberReferenceHandle)MetadataTokens.Handle(token));
var owner = reader.GetTypeReference((TypeReferenceHandle)target.Parent);

// A method signature blob opens with a calling convention byte and then the number of parameters.
var signature = reader.GetBlobReader(target.Signature);
signature.ReadByte();
var parameters = signature.ReadCompressedInteger();

Console.WriteLine($"the call goes to {reader.GetString(owner.Namespace)}.{reader.GetString(owner.Name)}.{reader.GetString(target.Name)}");
Console.WriteLine($"which takes {parameters} arguments, and the source passed 1");
//# end

// Stage three. What the compiler produced is a Windows executable, whatever machine you are on.
//# block id=pe env=E0 tags=[tour]
var headers = image.PEHeaders;
var firstTwo = File.ReadAllBytes(assemblyFile).AsSpan(0, 2).ToArray();

Console.WriteLine($"first two bytes:       {Encoding.ASCII.GetString(firstTwo)}");
Console.WriteLine($"machine in the header: {headers.CoffHeader.Machine}");
Console.WriteLine($"sections:              {string.Join(", ", headers.SectionHeaders.Select(section => section.Name))}");
Console.WriteLine($"managed header says:   {headers.CorHeader!.Flags}");
Console.WriteLine($"precompiled code:      {headers.CorHeader.ManagedNativeHeaderDirectory.Size} bytes");
//# end

// Stage four. The method is a row in a table.
//# block id=row env=E0 tags=[tour]
var countHandle = reader.MethodDefinitions
    .First(handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == "Count");

var declaring = reader.GetTypeDefinition(reader.GetMethodDefinition(countHandle).GetDeclaringType());

Console.WriteLine($"token:          0x{MetadataTokens.GetToken(countHandle):X8}");
Console.WriteLine($"top byte:       0x{MetadataTokens.GetToken(countHandle) >> 24:X2}, which is the MethodDef table");
Console.WriteLine($"row number:     {MetadataTokens.GetToken(countHandle) & 0xFFFFFF}");
Console.WriteLine($"declaring type: {reader.GetString(declaring.Namespace)}.{reader.GetString(declaring.Name)}");
Console.WriteLine($"name:           {reader.GetString(count.Name)}");
Console.WriteLine($"attributes:     {count.Attributes}");
Console.WriteLine($"the row holds no code of its own, only the address of some");
//# end

// Stage five. The line is twelve bytes of a different language.
//# block id=il env=E0 tags=[tour]
Console.WriteLine($"{il.Length} bytes: {Convert.ToHexString(il)}");
Console.WriteLine();

// The opcodes in this method are all one byte, which is true of most of the instruction set and
// not of all of it. Decoding it properly is what T03 is for. This is a table with six rows in it.
var names = new Dictionary<byte, string>
{
    [0x02] = "ldarg.0        push the first argument, which is the string",
    [0x1F] = "ldc.i4.s       push the next byte as a number",
    [0x16] = "ldc.i4.0       push zero, the argument nobody wrote",
    [0x6F] = "callvirt       call the method named by the next four bytes",
    [0x8E] = "ldlen          pop an array, push its length",
    [0x69] = "conv.i4        make that length a 32 bit int",
    [0x2A] = "ret            return what is on the stack",
};

for (var at = 0; at < il.Length; at++)
{
    var opcode = il[at];
    var operand = string.Empty;

    if (opcode == 0x1F)
    {
        operand = $" 0x{il[at + 1]:X2}";
        at++;
    }
    else if (opcode == 0x6F)
    {
        operand = $" 0x{BitConverter.ToInt32(il, at + 1):X8}";
        at += 4;
    }

    Console.WriteLine($"  {opcode:X2}{operand,-11}  {names[opcode]}");
}
//# end

// Stage six. Four other files came out of the build, and none of them holds a line of your code.
//# block id=files env=E0 tags=[tour] capture=drop
foreach (var file in Directory.GetFiles(output).Order(StringComparer.Ordinal))
{
    Console.WriteLine($"{Path.GetFileName(file),-27} {new FileInfo(file).Length,8} bytes");
}
//# end

// The launcher is the big one and the interesting one, because it is the file a reader thinks of
// as their program. Two searches over raw bytes say what is in it, and the answer is neither the
// line nor anything else from the source.
//# block id=launcher env=E0 tags=[tour]
var launcherFile = Directory.GetFiles(output)
    .First(file => Path.GetFileNameWithoutExtension(file) == "Sample" && Path.GetExtension(file) is "" or ".exe");

var assemblyBytes = File.ReadAllBytes(assemblyFile);
var launcherBytes = File.ReadAllBytes(launcherFile);

// BSJB is the four byte signature at the start of every metadata root, and it is the cheapest
// possible answer to "does this file have managed metadata in it".
var metadataMagic = Encoding.ASCII.GetBytes("BSJB");

// The launcher's name is the one thing here that is not the same on all four platforms, so it is
// printed by the block above, whose output this page does not show.
Console.WriteLine($"your line's IL, inside Sample.dll:   {assemblyBytes.AsSpan().IndexOf(il) >= 0}");
Console.WriteLine($"your line's IL, inside the launcher: {launcherBytes.AsSpan().IndexOf(il) >= 0}");
Console.WriteLine($"managed metadata in Sample.dll:      {assemblyBytes.AsSpan().IndexOf(metadataMagic) >= 0}");
Console.WriteLine($"managed metadata in the launcher:    {launcherBytes.AsSpan().IndexOf(metadataMagic) >= 0}");
//# end

// Stage seven. Nothing above has run. Loading it is a separate act, and it is observable.
//# block id=load env=E0 tags=[tour]
static bool Loaded(string name) =>
    AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == name);

Console.WriteLine($"Sample loaded before we ask for it: {Loaded("Sample")}");

var sample = Assembly.LoadFrom(assemblyFile);

Console.WriteLine($"Sample loaded after we ask for it:  {Loaded("Sample")}");
Console.WriteLine($"the library it was compiled against: {typeof(object).Assembly.GetName().Name}");
Console.WriteLine($"and that one is loaded:              {Loaded("System.Private.CoreLib")}");
//# end

// Stage eight. Asking for the type builds a runtime structure that the file does not contain.
//# block id=type env=E0 tags=[tour]
var text = sample.GetType("Sample.Text")!;
var program = sample.GetType("Sample.Program")!;

Console.WriteLine($"the handle is not zero:          {text.TypeHandle.Value != 0}");
Console.WriteLine($"asking twice gives one answer:   {text.TypeHandle.Value == sample.GetType("Sample.Text")!.TypeHandle.Value}");
Console.WriteLine($"two types, two structures:       {text.TypeHandle.Value != program.TypeHandle.Value}");
Console.WriteLine($"the file has no such number in it, because it is an address in this process");
//# end

// Stage nine. The first call costs more than the rest, and the reason is that the first call is
// where the machine code gets made.
//# block id=jit env=E0 tags=[tour] capture=drop
var method = text.GetMethod("Count")!;
var counter = method.CreateDelegate<Func<string, int>>();
var sentence = "the quick brown fox";

var clock = Stopwatch.StartNew();
counter(sentence);
var first = clock.Elapsed.TotalMicroseconds;

clock.Restart();
for (var i = 0; i < 1000; i++)
{
    counter(sentence);
}

var later = clock.Elapsed.TotalMicroseconds / 1000;

Console.WriteLine($"first call:  {first,8:F2} microseconds");
Console.WriteLine($"later calls: {later,8:F2} microseconds each");
Console.WriteLine($"the first one cost at least ten times the rest: {first > later * 10}");
Console.WriteLine($"entry point: 0x{method.MethodHandle.GetFunctionPointer():X}");
//# end

// Stage ten. It ran, and the heap is different afterwards.
//# block id=heap env=E0 tags=[tour]
var before = GC.GetAllocatedBytesForCurrentThread();
var words = counter(sentence);
var after = GC.GetAllocatedBytesForCurrentThread();

Console.WriteLine($"the line returned {words}");
Console.WriteLine($"and allocated {after - before} bytes on the way");
Console.WriteLine();

// Where those bytes went, measured one piece at a time rather than worked out on paper. Each of
// these is the cost of one allocation and nothing else, because the counter is per thread and
// exact.
var a = GC.GetAllocatedBytesForCurrentThread();
var shortWord = new string('x', "the".Length);
var b = GC.GetAllocatedBytesForCurrentThread();
var longWord = new string('x', "quick".Length);
var c = GC.GetAllocatedBytesForCurrentThread();
var slots = new string[words];
var d = GC.GetAllocatedBytesForCurrentThread();

Console.WriteLine($"a string of {shortWord.Length} characters: {b - a} bytes");
Console.WriteLine($"a string of {longWord.Length} characters: {c - b} bytes");
Console.WriteLine($"an array of {slots.Length} references: {d - c} bytes");
Console.WriteLine($"which adds up to {2 * (b - a) + 2 * (c - b) + (d - c)}");
//# end

// A block of everything that is different on your machine and mine, kept together so the rest of
// the page can be the same everywhere.
//# block id=machine env=E0 tags=[tour] capture=drop
Console.WriteLine($"runtime:  {RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"platform: {RuntimeInformation.RuntimeIdentifier}");
Console.WriteLine($"pointers: {IntPtr.Size * 8} bits");
//# end
