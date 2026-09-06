// T03, the language under the language. What IL is, and why there is one at all.
//
// You can run this the ordinary way, with none of this repository's tooling in the picture:
//
//     dotnet build fixture -c Release
//     dotnet run lesson.cs
//
// The directives below are comments, so that prints every block in order.
//
// The program being read is L1 again, the same ten line shapes program T02 took apart. T02 read
// every column of the file except one. This reads that one.

//# block id=usings env=E0 tags=[tour] capture=none
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
//# end

//# block id=open env=E0 tags=[tour] capture=none
var here = Environment.GetEnvironmentVariable("XRAY_HERE") ?? ".";
var built = Path.Combine(here, "fixture", "bin", "Release");

var assemblyFile = Directory.GetFiles(built, "L1.dll", SearchOption.AllDirectories)
    .Order(StringComparer.Ordinal)
    .First();

using var stream = File.OpenRead(assemblyFile);
using var image = new PEReader(stream);
var reader = image.GetMetadataReader();
//# end

// The whole instruction set, without typing any of it out. Every opcode is a public static field
// on System.Reflection.Emit.OpCodes, and each one knows its own number, its operand shape and what
// it does to the stack.
//# block id=table env=E0 tags=[tour]
var opcodes = typeof(OpCodes)
    .GetFields(BindingFlags.Public | BindingFlags.Static)
    .Select(field => (OpCode)field.GetValue(null)!)
    .ToDictionary(op => (ushort)op.Value);

Console.WriteLine($"opcodes in the instruction set: {opcodes.Count}");
Console.WriteLine($"  one byte:  {opcodes.Values.Count(op => op.Size == 1)}");
Console.WriteLine($"  two bytes: {opcodes.Values.Count(op => op.Size == 2)}");
Console.WriteLine();

foreach (var op in new[] { OpCodes.Ldarg_0, OpCodes.Ldfld, OpCodes.Mul, OpCodes.Callvirt, OpCodes.Ret })
{
    Console.WriteLine($"  0x{(ushort)op.Value:X4}  {op.Name,-10} operand {op.OperandType,-14} pop {op.StackBehaviourPop,-10} push {op.StackBehaviourPush}");
}
//# end

// A disassembler. This is all of it. An opcode is one byte, or two when the first byte is 0xFE,
// followed by an operand whose length the opcode already told us.
//# block id=decoder env=E0 tags=[tour] capture=none
int OperandSize(OperandType type) => type switch
{
    OperandType.InlineNone => 0,
    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
    OperandType.InlineVar => 2,
    OperandType.InlineI8 or OperandType.InlineR => 8,
    _ => 4,
};

IEnumerable<(int Offset, OpCode Op, string Text)> Decode(byte[] il)
{
    var at = 0;

    while (at < il.Length)
    {
        var start = at;
        var code = (ushort)il[at++];

        if (code == 0xFE)
        {
            code = (ushort)(0xFE00 | il[at++]);
        }

        var op = opcodes[code];
        var operand = at;
        at += OperandSize(op.OperandType);

        yield return (start, op, Render(op, il, operand, at));
    }
}
//# end

// Turning an operand into something readable. The four byte ones are mostly tokens, and a token is
// the same table and row pair T02 was about, so the answer is a lookup in the tables.
//# block id=render env=E0 tags=[tour] capture=none
string Render(OpCode op, byte[] il, int at, int next) => op.OperandType switch
{
    OperandType.InlineNone => "",
    OperandType.ShortInlineI => ((sbyte)il[at]).ToString(),
    OperandType.InlineI => BitConverter.ToInt32(il, at).ToString(),
    OperandType.ShortInlineVar => il[at].ToString(),
    OperandType.InlineVar => BitConverter.ToUInt16(il, at).ToString(),
    OperandType.ShortInlineR => BitConverter.ToSingle(il, at).ToString("R"),
    OperandType.InlineR => BitConverter.ToDouble(il, at).ToString("R"),
    OperandType.ShortInlineBrTarget => $"IL_{next + (sbyte)il[at]:X4}",
    OperandType.InlineBrTarget => $"IL_{next + BitConverter.ToInt32(il, at):X4}",
    _ => Named(BitConverter.ToInt32(il, at)),
};

string Named(int token)
{
    // A string literal is the one operand that does not point into the tables. It points into a
    // heap of its own, which is why ldstr is the only instruction here that needs a special case.
    if ((token >>> 24) == 0x70)
    {
        return $"\"{reader.GetUserString(MetadataTokens.UserStringHandle(token))}\"";
    }

    var handle = MetadataTokens.EntityHandle(token);

    switch (handle.Kind)
    {
        case HandleKind.MethodDefinition:
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            return $"{TypeName(method.GetDeclaringType())}.{reader.GetString(method.Name)}";
        case HandleKind.FieldDefinition:
            var field = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
            return $"{TypeName(field.GetDeclaringType())}.{reader.GetString(field.Name)}";
        case HandleKind.MemberReference:
            var member = reader.GetMemberReference((MemberReferenceHandle)handle);
            return $"{Named(MetadataTokens.GetToken(member.Parent))}.{reader.GetString(member.Name)}";
        case HandleKind.MethodSpecification:
            var specific = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
            var arguments = specific.DecodeSignature(new Naming(), reader);
            return $"{Named(MetadataTokens.GetToken(specific.Method))}<{string.Join(", ", arguments)}>";
        case HandleKind.TypeDefinition:
            return TypeName((TypeDefinitionHandle)handle);
        case HandleKind.TypeReference:
            return reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Name);
        case HandleKind.TypeSpecification:
            return reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(new Naming(), reader);
        default:
            return handle.Kind.ToString();
    }
}

string TypeName(TypeDefinitionHandle handle) => reader.GetString(reader.GetTypeDefinition(handle).Name);

MethodDefinition Find(string type, string name) => reader.MethodDefinitions
    .Select(reader.GetMethodDefinition)
    .First(method => TypeName(method.GetDeclaringType()) == type && reader.GetString(method.Name) == name);

void Print(MethodDefinition method)
{
    foreach (var (offset, op, text) in Decode(image.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!))
    {
        Console.WriteLine($"  IL_{offset:X4}  {op.Name,-14} {text}".TrimEnd());
    }
}
//# end

// The smallest method in the program.
//# block id=square env=E0 tags=[tour]
Print(Find("Square", "Area"));
//# end

// Every instruction says what it does to the stack, so the depth at each point can be worked out
// without running anything. This method has no calls in it, which is what makes the walk short:
// a call pops as many values as its signature has parameters, so anything with a call in it has to
// read signatures too.
//# block id=stack env=E0 tags=[tour]
var walked = Find("Square", "Area");
var depth = 0;
var deepest = 0;

foreach (var (offset, op, _) in Decode(image.GetMethodBody(walked.RelativeVirtualAddress).GetILBytes()!))
{
    var pops = op.StackBehaviourPop switch
    {
        StackBehaviour.Pop0 => 0,
        StackBehaviour.Pop1 or StackBehaviour.Popref or StackBehaviour.Popi => 1,
        StackBehaviour.Pop1_pop1 => 2,
        // ret is the only one left here, and it pops the return value.
        _ => 1,
    };

    var pushes = op.StackBehaviourPush == StackBehaviour.Push0 ? 0 : 1;

    depth = depth - pops + pushes;
    deepest = Math.Max(deepest, depth);

    Console.WriteLine($"  IL_{offset:X4}  {op.Name,-8} pops {pops}  pushes {pushes}  stack now {depth}");
}

Console.WriteLine();
Console.WriteLine($"deepest the stack ever gets: {deepest}");
Console.WriteLine($"the method header claims:    {image.GetMethodBody(walked.RelativeVirtualAddress).MaxStack}");
//# end

// The next smallest, and the one constant in the program.
//# block id=circle env=E0 tags=[tour]
Print(Find("Circle", "Area"));
//# end

// What sits in front of the instructions. Two formats, and which one you get is decided by whether
// the method is small enough and plain enough to fit in the small one.
//# block id=header env=E0 tags=[tour]
Console.WriteLine("  method                 header  maxstack  code  locals  init");

foreach (var handle in reader.MethodDefinitions)
{
    var method = reader.GetMethodDefinition(handle);
    var name = $"{TypeName(method.GetDeclaringType())}.{reader.GetString(method.Name)}";

    if (method.RelativeVirtualAddress == 0)
    {
        Console.WriteLine($"  {name,-22} no body at all, and the row says why: {method.Attributes & MethodAttributes.Abstract}");
        continue;
    }

    var body = image.GetMethodBody(method.RelativeVirtualAddress);
    var code = body.GetILBytes()!.Length;
    var locals = body.LocalSignature.IsNil
        ? 0
        : reader.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(new Naming(), reader).Length;

    Console.WriteLine($"  {name,-22} {body.Size - code,6} {body.MaxStack,9} {code,5} {locals,7}  {body.LocalVariablesInitialized}");
}
//# end

// The main method, which is the whole program, and the only one here big enough to need the large
// header. Its locals and its one exception region come out of that header rather than out of the
// instructions.
//# block id=main env=E0 tags=[tour]
var main = Find("Program", "<Main>$");
var mainBody = image.GetMethodBody(main.RelativeVirtualAddress);
var slots = reader.GetStandaloneSignature(mainBody.LocalSignature).DecodeLocalSignature(new Naming(), reader);

for (var slot = 0; slot < slots.Length; slot++)
{
    Console.WriteLine($"  local {slot}: {slots[slot]}");
}

foreach (var region in mainBody.ExceptionRegions)
{
    Console.WriteLine($"  {region.Kind}: try IL_{region.TryOffset:X4} to IL_{region.TryOffset + region.TryLength:X4}, handler IL_{region.HandlerOffset:X4} to IL_{region.HandlerOffset + region.HandlerLength:X4}");
}

Console.WriteLine();
Print(main);
//# end

// Every instruction in the program that carries a method token. Which of the two calling ones the
// compiler picks is not a matter of what you wrote.
//# block id=dispatch env=E0 tags=[tour]
foreach (var handle in reader.MethodDefinitions)
{
    var method = reader.GetMethodDefinition(handle);

    if (method.RelativeVirtualAddress == 0)
    {
        continue;
    }

    foreach (var (_, op, text) in Decode(image.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!))
    {
        if (op.OperandType == OperandType.InlineMethod)
        {
            Console.WriteLine($"  {op.Name,-10} {text}");
        }
    }
}
//# end

// The whole program, counted.
//# block id=census env=E0 tags=[tour]
var used = new List<OpCode>();
var bytes = 0;
var bodies = 0;

foreach (var handle in reader.MethodDefinitions)
{
    var method = reader.GetMethodDefinition(handle);

    if (method.RelativeVirtualAddress == 0)
    {
        continue;
    }

    bodies++;
    var il = image.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
    bytes += il.Length;
    used.AddRange(Decode(il).Select(instruction => instruction.Op));
}

Console.WriteLine($"methods with a body:  {bodies}");
Console.WriteLine($"bytes of IL in total: {bytes}");
Console.WriteLine($"instructions:         {used.Count}");
Console.WriteLine($"distinct opcodes:     {used.Select(op => op.Name).Distinct(StringComparer.Ordinal).Count()} of the {opcodes.Count} that exist");
Console.WriteLine();

foreach (var group in used.GroupBy(op => op.Name!).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal))
{
    Console.WriteLine($"  {group.Key,-14} {group.Count(),2}");
}
//# end

// How much IL there is in the library this program was built against. Dropped, because it belongs
// to whichever runtime is installed, but the gap is the point and the gap does not move.
//# block id=scale env=E0 tags=[tour] capture=drop
long Bytes(string file)
{
    using var open = File.OpenRead(file);
    using var pe = new PEReader(open);
    var metadata = pe.GetMetadataReader();

    return metadata.MethodDefinitions
        .Select(metadata.GetMethodDefinition)
        .Where(method => method.RelativeVirtualAddress != 0)
        .Sum(method => (long)pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!.Length);
}

var mine = Bytes(assemblyFile);
var corelib = Bytes(typeof(object).Assembly.Location);

Console.WriteLine($"L1.dll                 {mine,9} bytes of IL");
Console.WriteLine($"System.Private.CoreLib {corelib,9} bytes of IL");
Console.WriteLine($"the library is more than two thousand times the program: {corelib > mine * 2000}");
//# end

//# block id=machine env=E0 tags=[tour] capture=drop
Console.WriteLine($"runtime:  {RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"platform: {RuntimeInformation.RuntimeIdentifier}");
//# end

// The same signature walker T02 needed, for the same reason: a type in a signature is a tree of
// codes rather than a string, and the reader will walk the tree only if you say what to call things.
//# block id=naming env=E0 tags=[tour] capture=none
sealed class Naming : ISignatureTypeProvider<string, MetadataReader>
{
    public string GetPrimitiveType(PrimitiveTypeCode code) => code.ToString();
    public string GetSZArrayType(string element) => element + "[]";
    public string GetArrayType(string element, ArrayShape shape) => element + "[]";
    public string GetByReferenceType(string element) => element + "&";
    public string GetPointerType(string element) => element + "*";
    public string GetPinnedType(string element) => element;
    public string GetModifiedType(string modifier, string unmodified, bool required) => unmodified;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "method pointer";
    public string GetGenericInstantiation(string generic, ImmutableArray<string> arguments) => $"{generic}<{string.Join(", ", arguments)}>";
    public string GetGenericMethodParameter(MetadataReader reader, int index) => "!!" + index;
    public string GetGenericTypeParameter(MetadataReader reader, int index) => "!" + index;
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte kind) => reader.GetString(reader.GetTypeDefinition(handle).Name);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte kind) => reader.GetString(reader.GetTypeReference(handle).Name);
    public string GetTypeFromSpecification(MetadataReader reader, MetadataReader context, TypeSpecificationHandle handle, byte kind) => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
}
//# end
