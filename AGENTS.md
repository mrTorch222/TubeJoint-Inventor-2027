# TubeJoint for Autodesk Inventor 2027 — Codex handoff

## Goal

Develop a production-oriented Autodesk Inventor 2027 add-in that creates editable
laser-cut tube tenon-and-slot joints for Frame Generator members. The user tests
geometry in real assemblies and expects iterative fixes based on screenshots.
Communicate with the user in Russian.

## Environment and build

- Windows 10/11, Autodesk Inventor Professional 2027.
- C# / .NET 8 Windows Forms add-in.
- Main project: `src/TubeJoint.AddIn/TubeJoint.AddIn.csproj`.
- Interop reference: `C:\Program Files\Autodesk\Inventor 2027\Bin\Public Assemblies\Autodesk.Inventor.Interop.dll`.
- Official Inventor 2027 interop version is 31.0.
- Build from repository root with `./build.ps1` in PowerShell.
- Close Inventor before running `./install.ps1`.
- Install target: `%APPDATA%\Autodesk\Inventor 2027\Addins\TubeJoint`.

When working locally in VS Code, run the build after every API-signature change.
Do not send the user an archive until `build.ps1` succeeds on their Windows
installation. After a successful build, run `install.ps1`, start Inventor and
test against disposable copies of the IAM and both referenced IPT files.

## Current baseline

- Source/package line: iteration 9.
- Template file remains `StandardSideTenonSlot_v6.ipt` for compatibility.
- Template schema: 9; repository schema: 10.
- Ribbon location: Design tab, panel `Шип-паз труб`.
- Command UI is a floating Inventor `DockableWindow` containing a WinForms
  control. Inventor does not expose its private built-in Property Panel controls.
- No success dialog after creation. Show dialogs only for errors or destructive
  confirmation.
- Selection order: whole male tube occurrence, then planar female face.
- Current implemented sides are opposite A/B. A/B/C/D mask data already exists.
- Blue and green previews are tenons, orange previews are slots, yellow is the
  optional hole. Preview uses actual sampled profiles and triangle client graphics,
  not bounding boxes.
- Creation and deletion use Inventor transactions so one assembly Ctrl+Z should
  undo the complete operation.
- `Удалить все` removes all TubeJoint records/features but must preserve native
  Frame Generator Trim, Miter and Notch features.

## User's non-negotiable geometry requirements

1. Tenon and slot must share the same coordinate frame and center. One A/B side
   must not drift relative to the other, including after Trim/Notch.
2. For angled incoming tubes, tenon insertion is perpendicular to the selected
   female face, projected into the male wall plane; it must not simply follow
   the incoming tube axis.
3. Native Trim/Extend and Notch may leave a deliberate gap. Detect this and bridge
   the tenon root with a small overlap instead of rejecting the member.
4. Slot is temporarily a simple rectangle. The user will later replace its profile.
5. The template sketches must have a clear `(0,0)` construction cross and center
   point so hand-edited profiles can be aligned accurately.
6. Width and height are explicit millimetre parameters. Slot material thickness
   uses 1.0–3.0 mm in 0.5 mm steps; Auto remains available but is off by default.
7. Local laser reliefs must not stretch with overall tenon width/height or slot
   size. They depend on material thickness.

## Template contract

The editable template is stored at:

`%APPDATA%\Autodesk\Inventor 2027\TubeJoint\Templates\StandardSideTenonSlot_v6.ipt`

Never overwrite or redraw an existing user template during schema migration.
Only add missing parameters and update the schema marker.

Named sketches:

- `TJ_MALE_PROFILE`: one closed outer contour; +X from root to nose.
- `TJ_FEMALE_PROFILE`: one centered closed slot contour; currently rectangular.
- `TJ_CENTER_VENT`: one centered closed optional-hole contour.

Important parameters:

- `TJ_TenonLength`, `TJ_TenonAcrossWidth`, `TJ_TenonThickness`.
- `TJ_SlotOverallWidth`, `TJ_SlotOverallLength`, `TJ_Clearance`.
- `TJ_TenonRelief = TJ_TenonThickness * 0.35`.
- `TJ_SlotRelief = TJ_TenonThickness * 0.35`.
- `TJ_ProfileIsParametric`: `0` is compatibility scaling; `1` copies the fully
  constrained Inventor sketch without external scaling.

Iteration 9 detects small circular boundary arcs in compatibility mode and maps
them with the independent relief radius, preventing X/Y scaling from turning a
circular relief into an ellipse. Fully parametric mode is the final intended
workflow once the user's sketches are constrained to `TJ_*` parameters.

## Placement implementation

Primary files:

- `Services/OccurrenceSelectionService.cs`: occurrence/face analysis, axes,
  Trim/Notch gaps and A/B anchors.
- `Services/TemplateProfileGeometryBuilder.cs`: final sketch/extrude geometry.
- `Services/TemplateProfileSampler.cs`: profile sampling and relief tagging.
- `Services/JointPreviewService.cs`: colored solid preview.

The current A/B fix computes both analytical side-wall/female-plane intersections,
locks both anchors to one `MaleProfileYAxis` station, then computes slot centers.
Do not return to `Face.GetClosestPointTo` for placement: it clamps to trimmed face
boundaries and caused previous tenon/slot offsets.

## Manipulators

Primary files:

- `Services/JointManipulatorService.cs`.
- `UI/NativeJointInput.cs`.
- `UI/JointPropertiesControl.cs`.

The first native manipulator is for the optional hole. The checkbox
`3D-манипулятор отверстия` starts `InteractionEvents` + `TriadEvents`. It uses:

- `TriadEvents.GlobalTransform` to set the initial selected-face coordinate system.
- `DegreesOfFreedom` limited to X translation, Y translation and XY-plane translation.
- `OnMove` matrix origin projected onto the selected-face X/Y axes.
- Converted offsets update the numeric X/Y editors and live preview.

Important Inventor 2027 signature: `TriadEvents.Reposition` is
`Reposition(TriadSegmentEnum, object AlignWith)` and does **not** accept a Matrix.
Do not call `Reposition(matrix)`. Inventor 2027 exposes a read/write
`TriadEvents.GlobalTransform` property for this purpose.

Next manipulator stages requested by the user:

1. Common planar manipulator moving tenons, slots and centered hole together.
2. Hole manipulator only when `Отверстие` is enabled; otherwise hole stays centered
   on and moves with the joint.
3. Interactive wall handles for enabling/disabling and rotating placement among
   A/B/C/D. Opposite faces are the common case, but all four walls must eventually
   be selectable.
4. Individual movement toward an edge where required.

Keep preview and final geometry driven by the same stored offsets. Do not implement
a visual-only handle.

## Immediate verification tasks

1. Run `build.ps1` on Windows against the installed Inventor 2027 interop.
2. If compilation succeeds, install and confirm the add-in loads.
3. Create A+B on a straight Trim/Notch joint and verify both roots share one station.
4. Repeat with an angled and intentionally gapped Notch joint.
5. Change Width X and Height Y and verify a small circular root relief remains
   circular and follows `TJ_TenonRelief`.
6. Enable `Отверстие` and its 3D manipulator; verify only X/Y motion is available,
   fields update during drag, preview follows, and OK creates the hole at that point.
7. Verify Cancel creates nothing and one Ctrl+Z removes the completed joint.

## Known limitations

- Actual C/D face discovery and construction are not implemented yet.
- Common-joint and per-wall manipulators are not implemented yet.
- Exact skewed-profile compensation for all angled intersections remains future work.
- Existing records are validated but not rebuilt by `Проверить соединения`.
- Selective deletion is future work; current deletion is assembly-wide.

## Working style

- Preserve user-edited template geometry and unrelated files.
- Prefer small, testable iterations with unique package names.
- Do not claim a runtime fix until it has been tested in Inventor.
- When a build fails, use the exact compiler error and inspect the real 31.0 interop
  signature before patching.
- Keep Russian labels and user-facing errors concise.
- Update `VERSION.txt`, `README.md` and `FIRST_TEST.md` with each package.
