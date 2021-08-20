// Inspired by
// See https://blog.adamfurmanek.pl/2016/04/23/custom-memory-allocation-in-c-part-1/
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers;

Examples.StackExample();
Examples.ArrayPoolExample();
Examples.UnmanagedExample();

internal static class Examples 
{
    
    // Generate new instance with the same public data every time
    private static TestClass GetExample() => new() 
    {
        IntField = 342, 
        LngField = 356,
        Dbl1 = double.NaN
    };

    private static void RunExample(Span<byte> buff, [CallerMemberName]string name = "")
    {
        // Get a new example. Same public fields/props, different internal counter
        var example = GetExample();
        // Same as `example`
        var item = example;
        
        // `item` now points to a shallow copy of `example` located within `buff`
        Implementation.CopyToBuff(ref item, buff);
        item.IntField = 42;
        item.LngField = 7;
        item.Dbl1 = 0.0;

        CompareToExample(example, item, name);
    }

    public static void StackExample() => RunExample(stackalloc byte[Implementation.SizeOf<TestClass>()]);
    public static void ArrayPoolExample() {
        var array = ArrayPool<byte>.Shared.Rent(Implementation.SizeOf<TestClass>());

        try 
        {
            RunExample(array);
        }
        finally
        {
            if(array is not null) 
            {
                ArrayPool<byte>.Shared.Return(array, true);
            }
        }
    }

    public static unsafe void UnmanagedExample() 
    {
        var size = Implementation.SizeOf<TestClass>();
        void* ptr = NativeMemory.AllocZeroed((nuint)size);
        try 
        {
            RunExample(new Span<byte>(ptr, size));
        }
        finally
        {
            if (ptr != null) 
            {
                NativeMemory.Free(ptr);
            }
        }
    }

    private static void CompareToExample(TestClass example, TestClass item, string name) 
    {

        Console.WriteLine();
        Console.WriteLine($"Running `{name}`");
        Console.WriteLine("{0,20}|{0,20}|{0,20}", new string('-', 20));
        Console.WriteLine($"{("Prop"),20}|{("Item"),20}|{("Example"),20}");
        Console.WriteLine("{0,20}|{0,20}|{0,20}", new string('-', 20));
        Console.WriteLine($"{nameof(TestClass.IntField),-20}|{item.IntField,20}|{example.IntField,20}");
        Console.WriteLine($"{nameof(TestClass.LngField),-20}|{item.LngField,20}|{example.LngField,20}");
        Console.WriteLine($"{nameof(TestClass.Dbl1),-20}|{item.Dbl1,20}|{example.Dbl1,20}");
        Console.WriteLine("{0,20}|{0,20}|{0,20}", new string('-', 20));
        Console.WriteLine($"{("GC Gen"),-20}|{GC.GetGeneration(item),20}|{GC.GetGeneration(example),20}");
        Console.WriteLine("{0,20}|{0,20}|{0,20}", new string('-', 20));
        Console.Write($"Item:{Environment.NewLine}\t");
        Console.WriteLine(item.ToString());
        Console.Write($"Example:{Environment.NewLine}\t");
        Console.WriteLine(example.ToString());
        Console.WriteLine();
        Console.WriteLine();

    }
}

internal static class Implementation
{
    // Treat pointer as reference. Similar to `void*` -> `ref T`
    public static unsafe ref T AsRef<T>(this IntPtr ptr) => ref Unsafe.AsRef<T>(ptr.ToPointer());

    // Treat reference as pointer. Similar to `ref T` -> `void*`
    public static unsafe nint AsPtr<T>(ref T @ref) => (nint)Unsafe.AsPointer<T>(ref @ref);
    
    // https://docs.microsoft.com/en-us/dotnet/csharp/misc/cs1601
    // By-ref not allowed for `TypedReference`
    // Get a `void**`, take `&(void**) == (void***)`, cast to `nint**`, then cast `*(nint**) == nint*` to `ref nint`
    public static unsafe ref nint PtrToPtr(TypedReference data) => ref Unsafe.AsRef<nint>((void*)*(nint**) &data);


    // Read an int32 at an offset within RTTI
    public static int SizeOf<T>() where T : class => 
        Unsafe.ReadUnaligned<int>(
            ref Unsafe.Add<byte>(
                ref typeof(T).TypeHandle.Value.AsRef<byte>(), 
                sizeof(int)
            )
        );

    public static void CopyToBuff<T>(ref T value, Span<byte> buffer) where T : class
    {
        _ = value ?? throw new ArgumentNullException();
        var size = SizeOf<T>();
        if(buffer.Length < size)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer));
        }

        // Equivalent to something like 
        // `object` -> `(object*, RuntimeTypeHandle)`
        TypedReference typedRef = __makeref(value);

        // `ref nint` is basically `void**`
        ref nint nintPtr = ref PtrToPtr(typedRef);

        // Use pointer to reach actual object stored in heap, go back by one ptrsize,
        // return as managed reference.
        // This reference points *directly* to the beginning of the object in the heap.
        ref var dataRef = ref Unsafe.Subtract<byte>(ref AsRef<byte>(nintPtr), IntPtr.Size);

        // Now, replace heap pointer to the actual object with pointer to the buffer.
        // Offset is the same as above.
        // Equivalent to assignment `*(void**) = void*`
        nintPtr = AsPtr(ref buffer[IntPtr.Size]);
    
        // View over the actual block of memory in heap
        MemoryMarshal.CreateSpan(ref dataRef, size)
        // Copy to new location
            .CopyTo(buffer);

        // Constructs a reference to object of type `T`, but using
        // `TypedReference` that points to `buffer` instead of heap.
        // Assigns this new object instance to `value`.
        // Equivalent to `(object*, RuntimeTypeHandle)` -> `object`
        value = __refvalue(typedRef, T);
    }
}

internal record TestClass 
{

    private static int Count = -1;

    public double Dbl1;
    private readonly int _id;
    private int _intField1;
    private long _lngField;
    public int IntField
    {
        get => _intField1;
        set => _intField1 = value;
    }

    public long LngField 
    {
        get => _lngField;
        set => _lngField = value;
    }


    public override string ToString() => $"An overloaded method in #{_id}: ({IntField}, {LngField}, {Dbl1})";

    public TestClass() => _id = System.Threading.Interlocked.Increment(ref Count);
}

