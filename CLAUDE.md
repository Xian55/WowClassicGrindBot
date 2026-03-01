# WowClassicGrindBot - Claude Code Guidelines

## bash commands
* don't pipe to /dev/nul. we run git bash on windows and it doesn't work.
* in case the file exists use `rm -f "./WowClassicGrindBot/nul"`

## Project Overview
Multi-project .NET 10 solution (MasterOfPuppets.sln) with Blazor Server frontend, SignalR communication, and various utility projects.

## Language & Framework
- **Target:** .NET 10 (`net10.0`) with C# 14 (`LangVersion: preview`)
- **SDK:** 10.0.100 (defined in `global.json`)
- Use latest C# 14 features: primary constructors, collection expressions, `field` keyword, extension types, etc.
- Nullable reference types are enabled project-wide

## Build & Test Commands
```bash
dotnet build MasterOfPuppets.sln
dotnet test
dotnet run --project BlazorServer
dotnet run --project Benchmarks -c Release
```

## Constants
  - Replace magic strings/numbers with `public const` fields for cross-file discoverability
  - Place constants in the owning class to enable Find All References and compile-time safety

## User facing API changes
- When the `Core\Requirement\RequirementFactory.cs` is changed, a user facing API is added, removed, renamed make sure to update the `README.md` file

## Code Style
Existing `.editorconfig` defines style rules. Key conventions:
- File-scoped namespaces
- Explicit types preferred over `var`
- Expression-bodied properties/indexers/accessors, but not methods/constructors
- Use pattern matching and null propagation
- Prefer braces for control statements

## Project Structure
- `BlazorServer/` - Main web application entry point
- `Core/` - Core business logic
- `Game/` - Game interaction layer
- `Frontend/` - Blazor UI components
- `PPather/` - Pathfinding implementation
- `Benchmarks/` - BenchmarkDotNet performance tests
- `SharedLib/` - Shared utilities
- `WinAPI/` - Windows API interop

## Dependencies
Central package management via `Directory.Packages.props`:
- **Logging:** Serilog with structured logging
- **Serialization:** Newtonsoft.Json, MessagePack, MemoryPack
- **UI:** Blazor Bootstrap, MatBlazor
- **Benchmarking:** BenchmarkDotNet

## Architecture Patterns
- Constructor-based dependency injection via `Microsoft.Extensions.DependencyInjection`
- SignalR for real-time client communication (MessagePack protocol)
- Async/await throughout - avoid blocking calls

## Testing
- Run benchmarks in Release mode: `dotnet run --project Benchmarks -c Release`
- Use BenchmarkDotNet `[Benchmark]` attributes for performance testing

## Git Workflow
- Main branch: `dev`
- Create feature branches from `dev`