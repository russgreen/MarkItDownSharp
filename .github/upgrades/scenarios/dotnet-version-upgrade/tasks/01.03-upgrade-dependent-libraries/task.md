# 01.03-upgrade-dependent-libraries: Retarget dependent libraries

## Objective
Retarget MarkItDownSharp.Converters and MarkItDownSharp.Worker from net8.0 to net11.0 after Core is upgraded.

## Scope
Update both dependent library project files in dependency order, then restore and build each project and its required references. No package versions are defined in these projects.

## Done when
Both library projects target net11.0, consume the upgraded Core reference successfully, restore, and build with zero warnings or errors.

## Research Findings

- Converters and Worker each define `TargetFramework` locally as `net8.0`, have no package references or imported local targets, and reference the now-`net11.0` Core project.
- They are peers in the dependency graph, so both can be retargeted together and built independently against Core after the framework replacement.
- The assessment reports no package or API compatibility incidents for either library beyond the required framework change.
