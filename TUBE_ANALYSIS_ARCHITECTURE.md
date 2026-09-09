# Tube analysis core

`src/TubeJoint.AddIn/TubeAnalysis` is a read-only boundary between Autodesk
Inventor B-Rep data and TubeJoint features.

## Contract

- `ITubeAnalyzer` accepts a `PartDocument` and returns one immutable
  `TubeAnalysisResult`.
- The result contains only numbers, enums and COM-free coordinate values. It can
  be retained by UI and commands without keeping Inventor geometry objects alive.
- Analysis never writes iProperties, creates features, changes occurrences or
  displays UI.
- Unsupported geometry fails with a typed `TubeAnalysisFailure`.

## Geometry and performance

`GenericSolidTubeAnalyzer` evaluates `OrientedMinimumRangeBox` once and traverses
the body's faces once. That pass collects both planar wall stations and axial
cylinders. Round tubes require coaxial inner/outer cylinders whose diameter spans
both transverse box dimensions, so RHS/SHS corner fillets are not mistaken for a
round section.

The exact B-Rep dimensions and nominal display dimensions are stored separately.
No rounded value is used for placement or transforms.

`CachedTubeAnalyzer` caches both successful results and typed negative results per
open `PartDocument`, then invalidates them by
`PartComponentDefinition.ModelGeometryVersion`. This prevents repeated B-Rep scans
of non-tube parts in large assemblies. The weak-key cache does not keep closed
documents alive.

## Consumer rule

Joint placement, normalization, iProperties, file naming and UI belong in adapters
or application services outside this folder. They must depend on `ITubeAnalyzer`,
not instantiate `GenericSolidTubeAnalyzer` directly.
