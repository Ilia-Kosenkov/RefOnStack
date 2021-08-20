# Allocating* reference types on stack**
- <sup>*</sup> actually, copying
- <sup>**</sup> anywhere where one can get a sufficiently large `Span<byte>`
  
This small demo is inspired by ridiculously cool series of blog posts by  Adam Furmanek (see his first part [here](https://blog.adamfurmanek.pl/2016/04/23/custom-memory-allocation-in-c-part-1/), or watch his amazing [talk](https://www.youtube.com/watch?v=H0DeuoIbyrs)).

## Ok, but why?
Well, because. Because it is interesting to see what programming language allows to do. It is unlikely that this approach (in its current form) has any real-life applications, apart from, *probably*, storing multiple instances of some class tightly packed in an array (rather than storing references like it happens for all reference types).

## Example one
Take an object, copy its data to some buffer, then obtain a valid reference (of type `T`), which points to the data in the buffer.
Modify objects and demonstrate they are two different things. Use `GC.GetGeneration(object)` to determine `GC` generation, which acts as a proxy to the type of memory where the object is allocated. Generation is (likely) determined by inspecting `Gen0` and `Gen1`. If object is not found there, it should be in `Gen2`... or, in our case, in some weird block of unmanaged heap or stack memory.

<details>
<summary>Sample output</summary>

```
NOTE: In this example, GC Gen 0 is a signature of heap object, while GC Gen 2 is a signature of non-heap object


Running `StackExample`
--------------------|--------------------|--------------------
                Prop|                Item|             Example
--------------------|--------------------|--------------------
IntField            |                  42|                 342
LngField            |                   7|                 356
Dbl1                |                   0|                 NaN
--------------------|--------------------|--------------------
GC Gen              |                   2|                   0
--------------------|--------------------|--------------------
Item:
	Obj #0: (42, 7, 0), GC 2
Example:
	Obj #0: (342, 356, NaN), GC 0



Running `ArrayPoolExample`
--------------------|--------------------|--------------------
                Prop|                Item|             Example
--------------------|--------------------|--------------------
IntField            |                  42|                 342
LngField            |                   7|                 356
Dbl1                |                   0|                 NaN
--------------------|--------------------|--------------------
GC Gen              |                   0|                   0
--------------------|--------------------|--------------------
Item:
	Obj #1: (42, 7, 0), GC 0
Example:
	Obj #1: (342, 356, NaN), GC 0



Running `UnmanagedExample`
--------------------|--------------------|--------------------
                Prop|                Item|             Example
--------------------|--------------------|--------------------
IntField            |                  42|                 342
LngField            |                   7|                 356
Dbl1                |                   0|                 NaN
--------------------|--------------------|--------------------
GC Gen              |                   2|                   0
--------------------|--------------------|--------------------
Item:
	Obj #2: (42, 7, 0), GC 2
Example:
	Obj #2: (342, 356, NaN), GC 0
```

</details>


## Example two
Store multiple object side by side in an array.
With the ability to write any object to an arbitrary block of `Span<byte>` memory, it is easy to emplace multiple objects next to each other (knowing the actual size of underlying data).
The challenge is to retrieve those objects.
Two methods are provided: `RefReadObj<T>(Span<byte>, ref T?)` and `CopyObj<T>(Span<byte>)`.
The first one assigns to `ref T` value, which data pointer points to `Span<byte>`, directly,
In principle, if the layout of class is known (which is difficult to predict), one can write to `Span<byte>` and observe changes in returned `ref T`.
The second one returns a shallow copy of the object stored in `Span<byte>`. This newly-constructed object is a regular object residing in managed heap, yet all its fields have their values set from the data stored in the memory chunk.
Speaking in terms of structs, `RefReadObj<T>` acts as ref indexer (`ref T[]` when `T` is a struct), while `CopyObj<T>` acts as just indexer (`T[]` when `T` is a struct, performing copy).

<details>
<summary>Sample output</summary>

```


Writing 0 object: Obj #3: (0, 0, 0), GC 0
Writing 1 object: Obj #4: (1, 0, 0), GC 0
Writing 2 object: Obj #5: (2, 0, 0), GC 0
Writing 3 object: Obj #6: (3, 0, 0), GC 0
Writing 4 object: Obj #7: (4, 0, 0), GC 0
Writing 5 object: Obj #8: (5, 0, 0), GC 0
Writing 6 object: Obj #9: (6, 0, 0), GC 0
Writing 7 object: Obj #10: (7, 0, 0), GC 0
Writing 8 object: Obj #11: (8, 0, 0), GC 0
Writing 9 object: Obj #12: (9, 0, 0), GC 0
Modifying in place 0 object: Obj #3: (0, 42, 0), GC 2
Modifying in place 1 object: Obj #4: (1, 84, 0), GC 2
Modifying in place 2 object: Obj #5: (2, 126, 0), GC 2
Modifying in place 3 object: Obj #6: (3, 168, 0), GC 2
Modifying in place 4 object: Obj #7: (4, 210, 0), GC 2
Modifying in place 5 object: Obj #8: (5, 252, 0), GC 2
Modifying in place 6 object: Obj #9: (6, 294, 0), GC 2
Modifying in place 7 object: Obj #10: (7, 336, 0), GC 2
Modifying in place 8 object: Obj #11: (8, 378, 0), GC 2
Modifying in place 9 object: Obj #12: (9, 420, 0), GC 2
Modified in place 0 object: Obj #3: (0, 42, 1.7976931348623157E+308), GC 2
Copied            0 object: Obj #3: (0, 42, 0), GC 0
Modified in place 1 object: Obj #4: (1, 84, 1.7976931348623157E+308), GC 2
Copied            1 object: Obj #4: (1, 84, 0), GC 0
Modified in place 2 object: Obj #5: (2, 126, 1.7976931348623157E+308), GC 2
Copied            2 object: Obj #5: (2, 126, 0), GC 0
Modified in place 3 object: Obj #6: (3, 168, 1.7976931348623157E+308), GC 2
Copied            3 object: Obj #6: (3, 168, 0), GC 0
Modified in place 4 object: Obj #7: (4, 210, 1.7976931348623157E+308), GC 2
Copied            4 object: Obj #7: (4, 210, 0), GC 0
Modified in place 5 object: Obj #8: (5, 252, 1.7976931348623157E+308), GC 2
Copied            5 object: Obj #8: (5, 252, 0), GC 0
Modified in place 6 object: Obj #9: (6, 294, 1.7976931348623157E+308), GC 2
Copied            6 object: Obj #9: (6, 294, 0), GC 0
Modified in place 7 object: Obj #10: (7, 336, 1.7976931348623157E+308), GC 2
Copied            7 object: Obj #10: (7, 336, 0), GC 0
Modified in place 8 object: Obj #11: (8, 378, 1.7976931348623157E+308), GC 2
Copied            8 object: Obj #11: (8, 378, 0), GC 0
Modified in place 9 object: Obj #12: (9, 420, 1.7976931348623157E+308), GC 2
Copied            9 object: Obj #12: (9, 420, 0), GC 0


```

</details>