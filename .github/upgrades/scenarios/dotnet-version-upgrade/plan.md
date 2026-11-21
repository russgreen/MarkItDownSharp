# .NET Version Upgrade Plan

## Overview

**Target**: Upgrade the entire solution from .NET 8 to .NET 11 and convert the solution file from `.sln` to `.slnx`.

**Scope**: Five SDK-style projects, six NuGet packages, and the solution file.

### Selected Strategy
**All-At-Once** — All projects upgraded simultaneously in a single operation.

**Rationale**: Five projects already target modern .NET, use SDK-style project files, have a shallow dependency graph, and show no binary- or source-incompatible APIs. A single atomic pass avoids unnecessary multi-targeting overhead.

Projects in scope: MarkItDownSharp.Core, MarkItDownSharp.Converters, MarkItDownSharp.Worker, MarkItDownSharp.Desktop, and MarkItDownSharp.Tests.

## Upgrade Options

| Option | Selected | Why |
|--------|----------|-----|
| Upgrade Strategy | All-at-Once | The small modern-to-modern solution has a shallow graph and low assessed migration complexity. |
| Package Management | Central Package Management (CPM) | All five SDK-style projects are moving together on a modern .NET target, so package versions can be centralized without framework-migration friction. |

## Tasks

### 01-upgrade-solution: Upgrade the solution to .NET 11 and SLNX

Verify the required .NET 11 toolchain and any `global.json` constraints, then add Central Package Management and update the Core, Converters, Worker, Desktop, and Tests projects atomically. Retarget the cross-platform projects to `net11.0`, retarget Desktop to the assessment's .NET 11 Windows target, update package references including the incompatible Windows App SDK and deprecated xUnit package, and address any compilation or behavior-sensitive compatibility issues identified by the assessment.

Convert the existing `MarkItDownSharp.sln` to `MarkItDownSharp.slnx`, remove the obsolete `.sln` file, and ensure all repository tooling references the new solution format. Preserve Windows packaging behavior and project relationships while applying the framework and package changes.

**Done when**: the .NET 11 SDK is validated, all five projects target the intended .NET 11 frameworks, package versions are centralized and compatible, the solution is represented only by `MarkItDownSharp.slnx`, repository tooling uses the SLNX file, the whole solution builds with zero warnings or errors, and the full existing test suite passes.
