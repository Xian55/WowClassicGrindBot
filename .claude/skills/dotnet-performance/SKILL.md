---
name: dotnet-performance
description: .NET 10 performance best practices and patterns for C# code
user-invocable: true
---

# .NET Performance Guidelines

Reference material:
- https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/
- https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/
- https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/
- https://devblogs.microsoft.com/dotnet/performance_improvements_in_net_7/
- https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-6/
- https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-5/
- https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-core-3-0/
- https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-core-2-1/
- https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-core/

## General Principles

- Before writing new code, explore existing similar implementations — do not duplicate code
- Avoid allocations in hot paths — use structs, pooling, spans, and stackalloc
- Prefer `sealed` classes by default — enables devirtualization and inlining
- Prefer `readonly struct` when structs don't need mutation
- Use `StringComparison.Ordinal` / `OrdinalIgnoreCase` explicitly — unlocks vectorized fast paths
- Watch for hidden boxing: cast-to-`object`, struct-to-interface cast, `params object[]` (e.g. `string.Format("{0}", intValue)`), enum dict keys pre-.NET 5
- Prefer generic constraints (`where T : struct, IFoo`) over casting structs to interfaces — JIT specialises per `T`, no box
- Mark lambdas `static` (C# 9+) when they don't need captures — prevents accidental capture and lets the JIT cache a single delegate instance
- Avoid lambda captures in hot paths; cache the delegate in a `readonly` field instead of re-creating it per call site

## Memory & Buffers

- Prefer `Span<T>` / `ReadOnlySpan<T>` / `Memory<T>` over arrays for buffer operations
- `stackalloc` for small, fixed-size temporary buffers
- `ArrayPool<T>.Shared` / `MemoryPool<T>.Shared` for larger temporary buffers — always return rentals
- `Microsoft.Extensions.ObjectPool.ObjectPool<T>` for expensive-to-build reference types (StringBuilder, parsers, regex state) — pool maintained with fallback construction under contention
- `ref struct` for stack-only aggregates (parsers, builders) — cannot escape to heap, no boxing, no field storage in classes, no generic type-argument use
- Pass large structs as `in` / `ref` / `out` parameters — avoids the copy without heap allocation
- `[InlineArray]` for fixed-size inline buffers without heap allocation
- `ReadOnlySpan<byte> x = new byte[] { ... }` — compiler embeds in PE data section, zero heap allocation
- `BinaryPrimitives.ReadInt32LittleEndian(span)` (and family) for endian-aware reads/writes without unsafe code
- `MemoryMarshal.Cast<TFrom, TTo>(span)` to reinterpret spans for bulk processing
- `[SkipLocalsInit]` on performance-critical methods when safe
- Use `params ReadOnlySpan<T>` overloads instead of `params T[]` to avoid array allocation

## Collections

- `FrozenDictionary` / `FrozenSet` for read-heavy lookup tables populated once
- `SearchValues<T>` for repeated character/byte searching (pre-computed SIMD strategy)
- `SearchValues<string>` for multi-substring search with SIMD optimization
- `Dictionary.GetAlternateLookup<ReadOnlySpan<char>>()` for span-based lookups without string allocation
- `CollectionsMarshal.GetValueRefOrAddDefault()` for insert-or-update without double lookup
- `CollectionsMarshal.AsSpan(list)` returns a span over the internal `T[]` — alloc-free iteration, also lets you mutate elements in-place when `T` is a struct
- Pre-size collections at construction (`new List<T>(expectedCount)`, `new Dictionary<K,V>(capacity: 64)`) to skip resize allocations
- Use collection types with `Empty` semantics rather than allocating zero-element collections
- Prefer `OrderedDictionary<K,V>` over the non-generic `OrderedDictionary`

## Strings & Text

- `string.Create<TState>(length, state, action)` to build strings directly into their buffer
- `ISpanFormattable` / `IUtf8SpanFormattable` over string concatenation or interpolation on hot paths
- `CompositeFormat.Parse(fmt)` for repeated `string.Format` usage — parse once, format many
- `TryFormat(span, out written, ...)` instead of `ToString()` to format into pre-allocated buffers
- `Utf8Formatter.TryFormat()` / `Utf8Parser.TryParse()` for direct UTF-8 formatting without string intermediaries
- `Ascii` utility class for vectorized ASCII-only text processing
- `Base64Url` for URL-safe Base64 encoding/decoding without manual char replacement
- `[GeneratedRegex(...)]` source generator instead of `new Regex()` — compile-time, AOT-compatible
- `Regex.EnumerateMatches()` for zero-allocation match enumeration

## Async & Threading

- Prefer `ValueTask<T>` over `Task<T>` when results are often synchronous
- `System.Threading.Lock` (C# 13) instead of `lock(object)` — ~25% faster, type-safe
- `System.Threading.Channels` for high-performance producer-consumer patterns
- `PeriodicTimer` with `WaitForNextTickAsync()` for async periodic work
- `CancellationTokenSource.TryReset()` to reuse instead of allocating new per operation
- `Parallel.ForEachAsync()` for async-aware parallel iteration with concurrency control
- Implement `IValueTaskSource<T>` for completely alloc-free async I/O after warm-up (sockets, pipelines) — reuses one completion source instead of allocating per await
- Use `ConfigureAwait(false)` on library / non-UI code to avoid capturing `SynchronizationContext` and `TaskScheduler` — saves cross-thread marshalling cost and a small allocation

## I/O

- `System.IO.Pipelines` for network protocol parsing with backpressure and zero-copy reads
- `RandomAccess` + `File.OpenHandle()` for offset-based, thread-safe file I/O without FileStream overhead
- `Stream.ReadExactlyAsync()` / `ReadAtLeastAsync()` instead of manual read loops

## LINQ

- Avoid LINQ in hot paths — enumerators, state machines, captured-variable closures and intermediate collections all allocate; rewrite as plain `for` / `foreach`. The bullets below apply when LINQ is unavoidable.
- `CountBy()` / `AggregateBy()` instead of `GroupBy` + aggregate — single pass, fewer allocations
- `Order()` / `OrderDescending()` for self-comparable elements without key selector
- `LeftJoin()` / `RightJoin()` instead of verbose `GroupJoin` + `SelectMany` + `DefaultIfEmpty`

## Numeric & Parsing

- `INumber<T>` / `ISpanParsable<T>` for generic numeric algorithms without type-specific overloads
- `IUtf8SpanParsable<T>` for parsing numeric types directly from UTF-8 byte spans
- `Enum.HasFlag()` is now a JIT intrinsic — safe for hot paths (simple bitwise test)
- `Math.DivRem()` when both quotient and remainder are needed

## Serialization

- `System.Text.Json` source generators (`JsonSerializerContext`) — eliminates reflection, enables AOT
- `[JsonSerializable(typeof(T))]` for types included in source-generated serialization

## Attributes & Compiler Hints

- `[MethodImpl(MethodImplOptions.AggressiveInlining)]` on small hot-path helper methods
- `[SkipLocalsInit]` on performance-critical methods when safe
- `typeof(T).IsValueType` in generic methods — JIT replaces with compile-time constant
- `EqualityComparer<T>.Default.Equals()` — JIT devirtualizes for value types; do not cache in a local

## Measurement & Diagnostics

- Measure before optimising — profile first, don't guess. Hot-path means hot in *your* workload
- For this repo specifically: use the `/ppather-profile` skill (`.claude/skills/ppather-profile/SKILL.md`) — wraps `dotnet-trace` / `dotnet-counters` / `dotnet-gcdump` / `dotnet-dump` and the `Benchmarks` project against a live PathingAPI or bake process
- `BenchmarkDotNet` with `[MemoryDiagnoser]` — surfaces per-invocation allocation bytes and Gen0/1/2 counts; catches regressions in CI
- `GC.GetAllocatedBytesForCurrentThread()` to assert zero-allocation in unit tests:
  ```csharp
  long before = GC.GetAllocatedBytesForCurrentThread();
  HotPath(input);
  Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
  ```
- `dotnet-trace` / PerfView for ETW GC traces; dotMemory for heap snapshots and allocation trees; Roslyn analyzers (e.g. ClrHeapAllocationAnalyzer) for compile-time boxing / closure detection
- Reference link: https://slicker.me/dotnet/allocation-free.html (foundational allocation-free patterns)
