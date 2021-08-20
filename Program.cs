// Inspired by
// https://blog.adamfurmanek.pl/2016/04/23/custom-memory-allocation-in-c-part-1/
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers;

Console.WriteLine("NOTE: In this example, GC Gen 0 is a signature of heap object, while GC Gen 2 is a signature of non-heap object");
Console.WriteLine();

Examples.StackExample();
Examples.ArrayPoolExample();
Examples.UnmanagedExample();

Examples.TestCollection(10);

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

    public static void TestCollection(int n) 
    {
        // Don't over-use stack
        if (n < 0 || n > 20) 
        {
            throw new ArgumentOutOfRangeException(nameof(n));
        }
        
        var size = Implementation.SizeOf<TestClass>();
        // Allocate memory enough to store actual object *data*, side by side.
        // This is how structs are stored in the array.
        Span<byte> buffer = stackalloc byte[size * n];

        // Create new regular object, fill in fields and 'store' it
        // in the array (copy data block)
        for (var i = 0; i < n; i++) 
        {
            var obj = new TestClass() 
            {
                IntField = i
            };
            Implementation.WriteObj(
                buffer.Slice(size * i),
                obj
            );

            Console.WriteLine($"Writing {i} object: {obj}");
        }

        // Extract object by ref, meaning obtain an instance of `TestClass`,
        // which data pointer points to a block within stack-allocated array.
        // Equivalent would be a `ref array[i]` for array of structs.
        // Modify the object, which translates to modifications within allocated stack memory.
        for (var i = 0; i < n; i++) 
        {
            TestClass? obj = null;
            Implementation.RefReadObj(buffer.Slice(size * i), ref obj);
            obj.LngField = 42 * (i + 1);
            Console.WriteLine($"Modifying in place {i} object: {obj}");
        }

        // Now get a ref to an object in the stack block, and its copy.
        // Modify stack ref and observe no changes in the copy.
        // Copy is a shallow copy of the data stored on the stack.
        // The same thing happens when struct is (non-ref) read from an array.
        for (var i = 0; i < n; i++) 
        {
            TestClass? refRead = null;
            Implementation.RefReadObj(buffer.Slice(size * i), ref refRead);
            TestClass copied = Implementation.CopyObj<TestClass>(buffer.Slice(size * i));

            refRead.Dbl1 = double.MaxValue;
            Console.WriteLine($"Modified in place {i} object: {refRead}");
            Console.WriteLine($"Copied            {i} object: {copied}");
        }
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
    public static unsafe ref nint PtrToPtr(TypedReference data) {
        // `data` is tight ref struct of the following shape
        // `(void**, type)`, where `void**` is pointer to metadata that contains a pointer to actual data.
        // We need to be able to rewrite pointer to data.
        // Because `void**` is essentially `nint*`, we need a `ref nint` to be able to replace said pointer.

        // ptr is `((void**, Type)*)` (one extra indirection because of `&`)
        ref nint addrOfData = ref Unsafe.AsRef<nint>(&data);

        // `*(((void**, Type))*)` roughly translates to `void**`, 
        // because `void**` is the first element within the struct
        nint objPtr = addrOfData;

        // `objPtr` is equivalent to `void**`, so we reinterpret is as
        // `nint*` (pointer to an integer which holds the address we want to replace),
        // which is in turn converted to `ref nint`.
        return ref objPtr.AsRef<nint>();
    }


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
        WriteObj<T>(buffer, value);
        RefReadObj<T>(buffer, ref value!); // Just to avoid unnecessary warnings
    }

    public static void WriteObj<T>(Span<byte> buffer, T value) where T : class 
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
    
        // View over the actual block of memory in heap
        MemoryMarshal.CreateSpan(ref dataRef, size)
        // Copy to new location
            .CopyTo(buffer);
    }

    
    // Replaces reference of the existing object
    public static void RefReadObj<T>(
        Span<byte> buffer, 
        // Can accept `null` but always assigns something upon exit
        [System.Diagnostics.CodeAnalysis.NotNull] ref T? value
    ) where T : class 
    {
        TypedReference typedRef = __makeref(value);
        ref nint nintPtr = ref PtrToPtr(typedRef);

        // Now, replace heap (null-)pointer to the actual object with pointer to the buffer.
        // Offset is the same as above.
        // Equivalent to assignment `*(void**) = void*`
        nintPtr = AsPtr(ref buffer[IntPtr.Size]);
        
        // To satisfy nullchecks and throw if ptr magic failed
        _ = value ?? throw new NullReferenceException();
    }

    // Copies actual data to a new object
    public static T CopyObj<T>(Span<byte> buffer) where T : class, new()
    {
        // Allocates a placeholder
        T value = new();
        TypedReference typedRef = __makeref(value);

        // Address of the actual block of heap memory, allocated for `value`.
        // Filled with whatever default constructor writes
        // No need for refs, only actual pointer data
        nint nintPtr = PtrToPtr(typedRef);

        // Copy block of memory from `buffer` directly ti where `value`'s memory resides.
        // This effectively replaces contents of `value` with that stored in `buffer`
        Unsafe.CopyBlockUnaligned(
            ref Unsafe.Subtract<byte>(ref nintPtr.AsRef<byte>(), IntPtr.Size), 
            ref buffer[0],
            (uint) SizeOf<T>()
        );

        // `value` is a valid object living on the heap.
        // It contains a copy of what is stored within `buffer`.
        return value;
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


    public override string ToString() => $"Obj #{_id}: ({IntField}, {LngField}, {Dbl1}), GC {GC.GetGeneration(this)}";

    public TestClass() => _id = System.Threading.Interlocked.Increment(ref Count);
}

