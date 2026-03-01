---
name: dotnet-performance
description: .NET 10 performance best practices and patterns for C# code
user-invocable: false
---

# .NET Performance Guidelines

Follow .NET performance best practices from:
- https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/
- https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/
- https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/

Key patterns to apply:
- Before write any new code explore existing similar implementations, do not duplicate code (functions, variables, constants)
- Use Collections type with Empty semantics rather then allocating new collection with 0 elements
- Prefer `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>` over arrays for buffer operations
- Use `stackalloc` for small, fixed-size allocations
- Prefer `ValueTask<T>` over `Task<T>` when results are often synchronous
- Use `ArrayPool<T>.Shared` and `MemoryPool<T>.Shared` for temporary buffers
- Prefer `FrozenDictionary`/`FrozenSet` for read-heavy lookup tables
- Use `SearchValues<T>` for character/byte searching
- Avoid allocations in hot paths - use structs, pooling, and spans
- Use `[InlineArray]` for fixed-size inline buffers
- Prefer `string.Create` and `ISpanFormattable` over string concatenation
- Use `CompositeFormat` for repeated format string usage
- Apply `[SkipLocalsInit]` to performance-critical methods when safe
