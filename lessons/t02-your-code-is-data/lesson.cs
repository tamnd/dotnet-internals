// T02, your code is data. What is actually inside the .dll you just built.
//
// You can run this the ordinary way, with none of this repository's tooling in the picture:
//
//     dotnet build fixture -c Release
//     dotnet run lesson.cs
//
// The directives below are comments, so that prints every block in order.
//
// The program being read is L1, the ten line shapes program the whole book keeps coming back to.
// Its source is in lessons/shared/l1 and the project in fixture/ compiles it into this lesson's
// own output directory.

//# block id=usings env=E0 tags=[tour] capture=none
using System.Collections.Immutable;
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

// The census. Every table the format defines, and how many rows of each one this program filled.
//# block id=census env=E0 tags=[tour]
var all = Enum.GetValues<TableIndex>();
var used = all.Where(table => reader.GetTableRowCount(table) > 0).ToArray();

Console.WriteLine("  number  table             rows");

foreach (var table in used)
{
    Console.WriteLine($"    0x{(int)table:X2}  {table,-16} {reader.GetTableRowCount(table),4}");
}

Console.WriteLine();
Console.WriteLine($"{used.Length} tables with anything in them, out of the {all.Length} this reader knows how to find");
Console.WriteLine($"{all.Sum(table => reader.GetTableRowCount(table))} rows in total, and that is the whole program");
//# end

// Every type in the file, including the ones nobody typed.
//# block id=types env=E0 tags=[tour]
foreach (var handle in reader.TypeDefinitions)
{
    var type = reader.GetTypeDefinition(handle);
    var name = reader.GetString(type.Name);
    var space = reader.GetString(type.Namespace);

    Console.WriteLine($"  0x{MetadataTokens.GetToken(handle):X8}  {(space.Length == 0 ? name : $"{space}.{name}"),-12} {type.Attributes}");
}
//# end

// Every method, and which type row owns it.
//# block id=methods env=E0 tags=[tour]
foreach (var handle in reader.MethodDefinitions)
{
    var method = reader.GetMethodDefinition(handle);
    var owner = reader.GetTypeDefinition(method.GetDeclaringType());

    Console.WriteLine($"  0x{MetadataTokens.GetToken(handle):X8}  {reader.GetString(owner.Name)}.{reader.GetString(method.Name)}");
}
//# end

// Every name the file defines, all of them out of one byte array called #Strings, and then three
// names from the source that are not among them.
//# block id=names env=E0 tags=[tour]
var defined = new List<string>();

foreach (var handle in reader.TypeDefinitions)
{
    defined.Add(reader.GetString(reader.GetTypeDefinition(handle).Name));
}

foreach (var handle in reader.MethodDefinitions)
{
    var method = reader.GetMethodDefinition(handle);
    defined.Add(reader.GetString(method.Name));

    foreach (var parameter in method.GetParameters())
    {
        defined.Add(reader.GetString(reader.GetParameter(parameter).Name));
    }
}

foreach (var handle in reader.FieldDefinitions)
{
    defined.Add(reader.GetString(reader.GetFieldDefinition(handle).Name));
}

Console.WriteLine($"  {string.Join(" ", defined.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))}");
Console.WriteLine();

foreach (var wrote in new[] { "Area", "Circle", "radius", "shapes", "total", "shape" })
{
    Console.WriteLine($"  {wrote,-8} is a name in the file: {defined.Contains(wrote, StringComparer.Ordinal)}");
}
//# end

// The three that are missing did not vanish. They went into the other file.
//# block id=pdb env=E0 tags=[tour]
var pdbFile = Path.ChangeExtension(assemblyFile, ".pdb");

using var pdbStream = File.OpenRead(pdbFile);
using var pdbProvider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
var pdb = pdbProvider.GetMetadataReader();

Console.WriteLine($"local variable names in the assembly: {reader.GetTableRowCount(TableIndex.LocalVariable)}");
Console.WriteLine($"local variable names in the pdb:      {pdb.GetTableRowCount(TableIndex.LocalVariable)}");
Console.WriteLine();

foreach (var handle in pdb.LocalVariables)
{
    var local = pdb.GetLocalVariable(handle);
    Console.WriteLine($"  slot {local.Index}: {pdb.GetString(local.Name)}");
}

Console.WriteLine();
Console.WriteLine($"source documents the pdb knows about: {pdb.Documents.Count}");

foreach (var name in pdb.Documents.Select(handle => Path.GetFileName(pdb.GetString(pdb.GetDocument(handle).Name))).Order(StringComparer.Ordinal))
{
    Console.WriteLine($"  {name}");
}
//# end

// The MemberRef table, read as a transcript of what the program does. Nothing else in the file
// tells you as much about the source in as little space.
//# block id=calls env=E0 tags=[tour]
var refs = reader.MemberReferences.Select(reader.GetMemberReference).ToArray();

string Parent(MemberReference member) => member.Parent.Kind switch
{
    HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)member.Parent).Name),
    HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)member.Parent).DecodeSignature(new Naming(), reader),
    _ => member.Parent.Kind.ToString(),
};

// Sorting them by whether the owning type's name ends in Attribute is a shortcut rather than a
// rule, and it is fine here because none of the fourteen that survive it is an attribute.
bool IsAttribute(MemberReference member) => Parent(member).EndsWith("Attribute", StringComparison.Ordinal);

Console.WriteLine($"{refs.Length} rows, of which {refs.Count(IsAttribute)} are attribute constructors nobody typed.");
Console.WriteLine("the rest, in the order the compiler wrote them down:");
Console.WriteLine();

foreach (var member in refs.Where(member => !IsAttribute(member)))
{
    Console.WriteLine($"  {Parent(member)}.{reader.GetString(member.Name)}");
}
//# end

// A file names far more than it holds, and the names it cannot resolve on its own turn into a
// short list of other files.
//# block id=refs env=E0 tags=[tour]
Console.WriteLine($"types defined here:   {reader.GetTableRowCount(TableIndex.TypeDef),3}");
Console.WriteLine($"types named here:     {reader.GetTableRowCount(TableIndex.TypeRef),3}");
Console.WriteLine($"members named here:   {reader.GetTableRowCount(TableIndex.MemberRef),3}");
Console.WriteLine();
Console.WriteLine("assemblies it says it needs:");

foreach (var handle in reader.AssemblyReferences)
{
    var assembly = reader.GetAssemblyReference(handle);
    Console.WriteLine($"  {reader.GetString(assembly.Name),-20} {assembly.Version}");
}
//# end

// Attributes are rows too, and a row saying who it hangs off.
//# block id=attributes env=E0 tags=[tour]
var applied = reader.CustomAttributes.Select(reader.GetCustomAttribute).ToArray();

// The constructor column is a token. Here it always names a row in MemberRef, whose own parent
// column names a row in TypeRef, and that last one is the name worth printing.
string Named(CustomAttribute attribute)
{
    var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
    return reader.GetString(reader.GetTypeReference((TypeReferenceHandle)constructor.Parent).Name);
}

Console.WriteLine($"{applied.Length} rows, each one an owner and a constructor:");
Console.WriteLine();

foreach (var attribute in applied)
{
    Console.WriteLine($"  {attribute.Parent.Kind,-18} {Named(attribute)}");
}

Console.WriteLine();
Console.WriteLine($"every one of those constructors is in another assembly: {applied.All(attribute => attribute.Constructor.Kind == HandleKind.MemberReference)}");

// The one attribute here worth reading the value of. Its blob opens with a two byte prolog and
// then a length prefixed string, which is short enough to check by eye against the hex.
var framework = applied.First(attribute => Named(attribute) == "TargetFrameworkAttribute");
var blob = reader.GetBlobReader(framework.Value);
blob.ReadUInt16();

Console.WriteLine($"the framework the file asks for: {blob.ReadSerializedString()}");
//# end

// How big this gets. The numbers are a property of whichever runtime you have installed, so the
// page cannot quote them, but the gap between the two lines is the point and it does not move.
//# block id=scale env=E0 tags=[tour] capture=drop
long Rows(string file)
{
    using var open = File.OpenRead(file);
    using var pe = new PEReader(open);
    return Enum.GetValues<TableIndex>().Sum(table => (long)pe.GetMetadataReader().GetTableRowCount(table));
}

var mine = Rows(assemblyFile);
var corelib = Rows(typeof(object).Assembly.Location);

Console.WriteLine($"L1.dll                 {mine,8} rows");
Console.WriteLine($"System.Private.CoreLib {corelib,8} rows");
Console.WriteLine($"the library is more than a thousand times the program: {corelib > mine * 1000}");
//# end

//# block id=machine env=E0 tags=[tour] capture=drop
Console.WriteLine($"runtime:  {RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"platform: {RuntimeInformation.RuntimeIdentifier}");
//# end

// A signature is a tree of type codes rather than a string, so the reader will walk it for you but
// insists that you say what a walked type should be called. This is the smallest thing that
// answers that, and the only part of it this lesson uses is the generic instantiation.
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
