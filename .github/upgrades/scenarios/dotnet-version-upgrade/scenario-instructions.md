# .NET Version Upgrade

## Strategy
**Selected**: All-at-Once
**Rationale**: Five SDK-style projects already on .NET 8, a shallow dependency graph, and no binary or source-incompatible APIs make a single atomic upgrade the lowest-overhead approach.

### Execution Constraints
- Upgrade all five projects and package references in one atomic implementation task.
- Add Central Package Management before updating package references.
- Convert the `.sln` file to `.slnx` and remove the obsolete solution file.
- Complete a full warning-free solution build and run the existing test suite before completion.

## Upgrade Options

### Strategy
- Upgrade Strategy: All-at-Once

### Project Structure
- Package Management: Central Package Management (CPM)

## Preferences
- **Flow Mode**: Automatic
- **Target Framework**: net11.0
- **Solution Format**: Convert the existing solution to SLNX
- **Commit Strategy**: Single Commit at End

## Source Control
- **Source Branch**: main
- **Working Branch**: upgrade-dotnet-11
- **Commit Strategy**: Single Commit at End
- **Branch Sync**: Auto (Merge)
