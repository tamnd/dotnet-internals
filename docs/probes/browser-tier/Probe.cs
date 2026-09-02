using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Wasm;
using System.Runtime.Loader;
using System.Text;

namespace BrowserProbe;

public struct Point
{
    public int X;
    public int Y;
}

// One battery of checks, run in the browser, printed to the console with a prefix so the
// transcript can be pulled back out with one call. Nothing here is browser specific: the same
// class runs on a desktop runtime, which is how the two columns in the writeup were produced.
public static class Probe
{
    public static void RunAll()
    {
        Console.WriteLine("PROBE|BEGIN");
        Report("environment", "FrameworkDescription", () => RuntimeInformation.FrameworkDescription);
        Report("environment", "OSDescription", () => RuntimeInformation.OSDescription);
        Report("environment", "ProcessArchitecture", () => RuntimeInformation.ProcessArchitecture.ToString());
        Report("environment", "RuntimeIdentifier", () => RuntimeInformation.RuntimeIdentifier);
        Report("environment", "is this Mono", () =>
            Type.GetType("Mono.Runtime") is not null || Type.GetType("Mono.RuntimeStructs") is not null
                ? "yes, Mono types are present"
                : "no Mono types found");
        Report("environment", "IntPtr.Size", () => IntPtr.Size.ToString());
        Report("environment", "corelib assembly", () => typeof(object).Assembly.GetName().Name!);

        var pe = LoadSample();
        Report("metadata", "open a PE image", () =>
        {
            using var r = new PEReader(new MemoryStream(pe));
            return $"HasMetadata={r.HasMetadata}, sections={r.PEHeaders.SectionHeaders.Length}";
        });
        Report("metadata", "table row counts", () =>
        {
            using var r = new PEReader(new MemoryStream(pe));
            var m = r.GetMetadataReader();
            return $"TypeDef={m.GetTableRowCount(TableIndex.TypeDef)}, " +
                   $"MethodDef={m.GetTableRowCount(TableIndex.MethodDef)}, " +
                   $"Field={m.GetTableRowCount(TableIndex.Field)}";
        });
        Report("metadata", "read a name from the string heap", () =>
        {
            using var r = new PEReader(new MemoryStream(pe));
            var m = r.GetMetadataReader();
            var names = m.TypeDefinitions.Select(h => m.GetString(m.GetTypeDefinition(h).Name));
            return string.Join(",", names.Where(n => n != "<Module>"));
        });
        Report("metadata", "decode a method signature", () =>
        {
            using var r = new PEReader(new MemoryStream(pe));
            var m = r.GetMetadataReader();
            var md = FindMethod(m, "Get");
            var sig = m.GetMethodDefinition(md).DecodeSignature(new NameProvider(), null!);
            return $"{sig.ReturnType} ({string.Join(", ", sig.ParameterTypes)})";
        });
        Report("metadata", "read a method body", () =>
        {
            using var r = new PEReader(new MemoryStream(pe));
            var m = r.GetMetadataReader();
            var md = FindMethod(m, "AddThenDouble");
            var body = r.GetMethodBody(m.GetMethodDefinition(md).RelativeVirtualAddress);
            var il = body.GetILBytes()!;
            return $"{il.Length} IL bytes, maxStack={body.MaxStack}, " +
                   $"first bytes {string.Join(" ", il.Take(6).Select(b => b.ToString("x2")))}";
        });
        Report("metadata", "read the blob heap", () =>
        {
            using var r = new PEReader(new MemoryStream(pe));
            var m = r.GetMetadataReader();
            var td = m.GetTypeDefinition(m.TypeDefinitions.First());
            return $"blob heap size {m.GetHeapSize(HeapIndex.Blob)} bytes, first typedef {m.GetString(td.Name)}";
        });
        Report("metadata", "assembly bytes served to the page", () =>
        {
            var loc = typeof(Probe).Assembly.Location;
            return string.IsNullOrEmpty(loc) ? "Assembly.Location is empty" : loc;
        });

        Report("reflection", "reflect over a framework type", () =>
            $"{typeof(Dictionary<string, int>).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Length} private instance fields");
        Report("reflection", "invoke a method by reflection", () =>
            typeof(Probe).GetMethod(nameof(Doubled), BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { 21 })!.ToString()!);
        Report("reflection", "load an assembly from a byte array", () =>
            Assembly.Load(pe).GetName().Name!);
        Report("reflection", "a collectible load context", () =>
        {
            var alc = new AssemblyLoadContext("probe", isCollectible: true);
            var a = alc.LoadFromStream(new MemoryStream(pe));
            var name = a.GetName().Name!;
            alc.Unload();
            return $"loaded {name} into a collectible context and unloaded it";
        });

        Report("layout", "Unsafe.SizeOf<Point>", () => Unsafe.SizeOf<Point>().ToString());
        Report("layout", "Marshal.SizeOf<Point>", () => Marshal.SizeOf<Point>().ToString());
        Report("layout", "Marshal.OffsetOf Point.Y", () => Marshal.OffsetOf<Point>(nameof(Point.Y)).ToString());
        Report("layout", "pin an object", () =>
        {
            var s = "pin me";
            var h = GCHandle.Alloc(s, GCHandleType.Pinned);
            try
            {
                return $"pinned at 0x{h.AddrOfPinnedObject():x}";
            }
            finally
            {
                h.Free();
            }
        });
        Report("layout", "read the first word of an object", () =>
        {
            object o = new Point();
            var h = GCHandle.Alloc(o, GCHandleType.Pinned);
            try
            {
                var first = Marshal.ReadIntPtr(h.AddrOfPinnedObject() - IntPtr.Size);
                return $"0x{first:x}";
            }
            finally
            {
                h.Free();
            }
        });
        Report("layout", "does that word equal the type handle", () =>
        {
            object o = new Point();
            var handle = typeof(Point).TypeHandle.Value;
            var h = GCHandle.Alloc(o, GCHandleType.Pinned);
            try
            {
                var first = Marshal.ReadIntPtr(h.AddrOfPinnedObject() - IntPtr.Size);
                return first == handle
                    ? $"yes, both 0x{first:x}, which is what CoreCLR does"
                    : $"no, object word 0x{first:x} against type handle 0x{handle:x}";
            }
            finally
            {
                h.Free();
            }
        });
        Report("layout", "the words around a boxed value", () =>
        {
            object o = new Point { X = 0x11111111, Y = 0x22222222 };
            var h = GCHandle.Alloc(o, GCHandleType.Pinned);
            try
            {
                var p = h.AddrOfPinnedObject();
                var parts = new List<string>();
                for (var off = -2 * IntPtr.Size; off <= IntPtr.Size; off += IntPtr.Size)
                {
                    parts.Add($"{off,+3}:0x{Marshal.ReadIntPtr(p + off):x}");
                }

                return string.Join("  ", parts);
            }
            finally
            {
                h.Free();
            }
        });
        Report("layout", "string's private fields", () =>
            string.Join(",", typeof(string)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(f => f.Name)));
        Report("layout", "unmanaged allocation", () =>
        {
            var p = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.WriteInt32(p, 0, 1234);
                return $"wrote and read back {Marshal.ReadInt32(p, 0)} at 0x{p:x}";
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        });
        Report("layout", "RuntimeHelpers.GetHashCode", () =>
            RuntimeHelpers.GetHashCode(new object()).ToString());
        Report("layout", "stackalloc and pointer arithmetic", () =>
        {
            Span<int> span = stackalloc int[4];
            span[3] = 7;
            return $"span of {span.Length}, last {span[3]}";
        });

        Report("gc", "GC.GetGeneration", () => GC.GetGeneration(new object()).ToString());
        Report("gc", "GC.CollectionCount", () =>
            $"gen0={GC.CollectionCount(0)}, gen1={GC.CollectionCount(1)}, gen2={GC.CollectionCount(2)}");
        Report("gc", "force a collection", () =>
        {
            var before = GC.CollectionCount(0);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return $"gen0 count {before} then {GC.CollectionCount(0)}";
        });
        Report("gc", "allocate 50 MB and look again", () =>
        {
            var before = GC.CollectionCount(0);
            for (var i = 0; i < 400_000; i++)
            {
                Keep = new byte[128];
            }

            return $"gen0 count {before} then {GC.CollectionCount(0)}";
        });
        Report("gc", "does a finalizer run", () =>
        {
            Finalized = false;
            MakeGarbage();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return Finalized ? "yes" : "no, the finalizer had not run when the check returned";
        });
        Report("gc", "IsServerGC against the app switch", () =>
        {
            AppContext.TryGetSwitch("System.GC.Server", out var sw);
            return $"GCSettings.IsServerGC={GCSettings.IsServerGC}, AppContext switch={sw}";
        });
        Report("gc", "GCSettings.IsServerGC", () => GCSettings.IsServerGC.ToString());
        Report("gc", "GCSettings.LatencyMode", () => GCSettings.LatencyMode.ToString());
        Report("gc", "GC.GetGCMemoryInfo", () =>
        {
            var info = GC.GetGCMemoryInfo();
            return $"heap {info.HeapSizeBytes} bytes, generations reported {info.GenerationInfo.Length}";
        });
        Report("gc", "GC.GetTotalAllocatedBytes", () => GC.GetTotalAllocatedBytes().ToString());

        Report("codegen", "DynamicMethod and ILGenerator", () =>
        {
            var dm = new DynamicMethod("add", typeof(int), new[] { typeof(int), typeof(int) });
            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ret);
            var f = (Func<int, int, int>)dm.CreateDelegate(typeof(Func<int, int, int>));
            return $"emitted and called, 20 + 22 = {f(20, 22)}";
        });
        Report("codegen", "a persisted assembly builder", () =>
        {
            var ab = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("probe.dynamic"), AssemblyBuilderAccess.Run);
            return $"defined {ab.GetName().Name}";
        });
        Report("codegen", "compile an expression tree", () =>
        {
            var p = Expression.Parameter(typeof(int));
            var f = Expression.Lambda<Func<int, int>>(Expression.Multiply(p, Expression.Constant(3)), p).Compile();
            return $"compiled and called, 14 * 3 = {f(14)}";
        });
        Report("codegen", "Vector128.IsHardwareAccelerated", () => Vector128.IsHardwareAccelerated.ToString());
        Report("codegen", "PackedSimd.IsSupported", () => PackedSimd.IsSupported.ToString());
        Report("codegen", "read a runtime knob", () =>
        {
            var tiering = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation");
            var data = AppContext.GetData("System.GC.Server");
            return $"DOTNET_TieredCompilation={tiering ?? "(unset)"}, System.GC.Server={data ?? "(unset)"}";
        });

        Report("observation", "capture a stack trace", () =>
        {
            var t = new StackTrace(fNeedFileInfo: true);
            return $"{t.FrameCount} frames, top {t.GetFrame(0)?.GetMethod()?.Name}";
        });
        Report("observation", "start a thread", () =>
        {
            var seen = 0;
            var t = new Thread(() => Interlocked.Increment(ref seen));
            t.Start();
            t.Join();
            return $"thread ran, counter {seen}";
        });
        Report("observation", "Stopwatch resolution", () =>
            $"frequency {Stopwatch.Frequency}, highres {Stopwatch.IsHighResolution}");
        Report("observation", "Environment.ProcessorCount", () => Environment.ProcessorCount.ToString());
        Report("observation", "Process.GetCurrentProcess", () =>
            $"id {Process.GetCurrentProcess().Id}");
        Report("observation", "an EventSource with a listener", () =>
        {
            using var listener = new CountingListener();
            ProbeSource.Log.Ping(7);
            return $"listener saw {listener.Count} event(s)";
        });

        Console.WriteLine("PROBE|END");
    }

    static int Doubled(int n) => n * 2;

    static byte[]? Keep;
    static bool Finalized;

    sealed class Garbage
    {
        ~Garbage() => Finalized = true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void MakeGarbage()
    {
        var g = new Garbage();
        GC.KeepAlive(g);
    }

    static byte[] LoadSample()
    {
        using var s = typeof(Probe).Assembly.GetManifestResourceStream("BrowserProbe.Sample.dll")!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    static MethodDefinitionHandle FindMethod(MetadataReader m, string name) =>
        m.MethodDefinitions.First(h => m.GetString(m.GetMethodDefinition(h).Name) == name);

    static void Report(string group, string name, Func<string> check)
    {
        string status, detail;
        try
        {
            detail = check();
            status = "ok";
        }
        catch (Exception e)
        {
            status = "threw";
            detail = $"{e.GetType().Name}: {Flatten(e.Message)}";
        }

        Console.WriteLine($"PROBE|{group}|{name}|{status}|{detail}");
    }

    static string Flatten(string s)
    {
        var one = s.Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
        return one.Length > 160 ? one[..160] + "..." : one;
    }

    sealed class NameProvider : ISignatureTypeProvider<string, object>
    {
        public string GetArrayType(string e, ArrayShape shape) => $"{e}[{new string(',', shape.Rank - 1)}]";
        public string GetByReferenceType(string e) => $"{e}&";
        public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
        public string GetGenericInstantiation(string g, System.Collections.Immutable.ImmutableArray<string> a) => $"{g}<{string.Join(",", a)}>";
        public string GetGenericMethodParameter(object g, int i) => $"!!{i}";
        public string GetGenericTypeParameter(object g, int i) => $"!{i}";
        public string GetModifiedType(string m, string u, bool required) => u;
        public string GetPinnedType(string e) => $"pinned {e}";
        public string GetPointerType(string e) => $"{e}*";
        public string GetPrimitiveType(PrimitiveTypeCode code) => code.ToString().ToLowerInvariant();
        public string GetSZArrayType(string e) => $"{e}[]";
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte rawKind) => r.GetString(r.GetTypeDefinition(h).Name);
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte rawKind) => r.GetString(r.GetTypeReference(h).Name);
        public string GetTypeFromSpecification(MetadataReader r, object g, TypeSpecificationHandle h, byte rawKind) => "typespec";
    }

    sealed class ProbeSource : System.Diagnostics.Tracing.EventSource
    {
        public static readonly ProbeSource Log = new();
        ProbeSource() : base("Probe") { }

        [System.Diagnostics.Tracing.Event(1)]
        public void Ping(int value) => WriteEvent(1, value);
    }

    sealed class CountingListener : System.Diagnostics.Tracing.EventListener
    {
        public int Count;

        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
        {
            if (source.Name == "Probe")
            {
                EnableEvents(source, System.Diagnostics.Tracing.EventLevel.LogAlways);
            }
        }

        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs data) => Count++;
    }
}
