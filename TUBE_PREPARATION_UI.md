# Tube preparation UI boundary

This branch owns presentation only. Tube geometry remains in `TubeAnalysis`, and
Inventor mutations remain in `TubePreparationService`.

## First screen

The preparation command uses one review window containing:

- a row per unique recognized IPT;
- current file name and proposed file name;
- exact measured section and nominal section;
- wall thickness and assembly quantity;
- recognition status or typed failure;
- a per-row include checkbox;
- one global **Переименовать файлы** checkbox, unchecked by default;
- fixed target orientation **Продольная ось → +Z**;
- **Применить** and **Отмена** actions.

## Data contract

The UI receives immutable view rows prepared by an application service. A row must
not contain `Face`, `SurfaceBody`, `ComponentOccurrence`, or other live B-Rep COM
objects. Stable document identity and occurrence count are sufficient for display;
the application service resolves live documents again immediately before Apply.

The UI must never call `GenericSolidTubeAnalyzer` directly. It consumes the same
cached `ITubeAnalyzer` instance as the command.

## Execution rules

- Scanning is read-only and may be cancelled before Apply.
- Inventor API calls stay on Inventor's STA thread. A background task must not walk
  B-Rep COM collections.
- Apply uses one global transaction for model and occurrence transforms.
- The assembly range-box verification remains mandatory before commit.
- File SaveAs runs only when the rename checkbox is enabled and geometry has passed
  verification.
- Typed analysis failures are shown per row instead of silently hiding every
  unsupported component.
- Closing the window before Apply changes nothing.

## Separation from existing joint UI

This window is dedicated to batch tube preparation. It must not be merged into the
joint-properties dockable window: their lifetimes, selections, and transactions are
independent.
