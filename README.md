# TubeJoint for Autodesk Inventor 2027 — iteration 9

Prototype add-in for a reusable, editable tube tenon-and-slot standard.

## Iteration 9 focus

- package `iteration9-v31` copies each connected template contour through Inventor
  as one native set before placement; mixed lines, arcs, CV splines and interpolation
  splines keep both their entity types and their shared topological endpoints;
- opposite A/B slots are created as independent closed profiles and Cut features
  inside the same transaction, avoiding Inventor's ambiguous combined spline profile;
- the male wall is cleared inside a bounded replacement window and the exact
  `TJ_MALE_PROFILE` is joined back, preserving arbitrary concave root geometry
  without classifying arcs or generating extra relief shapes;
- coincident neighboring endpoints are merged into shared Inventor `SketchPoint`
  topology before profile creation; failures report entity composition and joint gaps;
- automatic slot thickness now accepts the measured male-wall value directly,
  without applying the removed manual 0.5 mm step validation;
- when Inventor rejects `Profiles.AddForSolid` for an otherwise connected mixed
  sketch, a fallback orders the shared endpoints and preserves every spline as one
  exact NURBS curve; preview failures are isolated per named template sketch;
- template contours may contain lines, circular arcs, circles, ellipses, elliptical
  arcs and B-splines; non-circular curves are sampled through Inventor's evaluator;
- Control Vertex Spline is handled explicitly as Inventor `BSplineCurve2d`, avoiding
  unreliable late-bound COM access and the `GetStrokes` SAFEARRAY type mismatch;
- evaluator curves use 161 preallocated `GetPointAtParam` samples, which matches the
  Inventor 31.0 `[In, Out]` array contract;
- evaluator sampling is used only by triangle preview; final IPT sketches preserve
  native entity types, and each `BSplineCurve2d` remains one `SketchFixedSpline` with
  its original order, knots, weights, periodicity and transformed control poles;
- closed Control Vertex Splines are recreated as one native
  `SketchControlPointSpline` from their transformed control polygon; this preserves
  Inventor's closed topology so the curve can form a solid cut profile;
- interpolation splines are recreated as one native `SketchSpline` from transformed
  fit points, retaining the fit method, tension and closed-loop state;
- final creation errors identify the exact Inventor stage and HRESULT instead of
  showing an unqualified COM error;
- coincident analytical tenon/slot centers no longer normalize a zero-length vector;
  Join simply tries both valid wall-normal directions in that case;
- mixed line/arc/spline contours are rebuilt as a connected chain using shared
  `SketchPoint` endpoints, preserving native curve types and solid-profile closure;
- tenon roots and female-plane slot intersections are stored separately on angled
  joints; both roots are projected transversely from one tube-axis station;
- the primary A/B wall family is selected by area, then resolved to its outermost
  plane, preventing an inner/outer pair from shifting both the second tenon and vent;
- the optional vent uses its own `TJ_VENT_*` sketch and `TJ_VENT_CUT_*` operation;
  its profile is prepared before the slot Cut changes the support face, and the vent
  is then cut only through the selected female wall depth plus clearance;
- the optional vent has an explicit `Размер выреза, %` control (10–500%, default 100%);
  this uniform scale applies only to the vent, never to the tenon or slot;
- the root-level `PARAMETRIC_TENON_SLOT_GUIDE_RU.md` documents every template
  parameter and the complete sketch-constraining workflow;
- solved tenon and slot contours are copied 1:1; compatibility X/Y scaling,
  independent relief remapping and automatic root stretching have been removed;
- every preview refresh reapplies the UI values to the template and resamples the
  Inventor-solved sketches, making the IPT the only profile-sizing authority;
- the parameter window starts floating at a stable size instead of docking to the left;
- sizing starts from `Standard / Compact / Reinforced / Custom` presets;
- automatic width, height and slot-thickness rules remain available but are off by default;
- the template receives a non-destructive schema-9 parameter contract for length, width,
  material thickness and local laser reliefs;
- local reliefs are solved directly inside the template from `TJ_TenonRelief` and
  `TJ_SlotRelief`, so their size is independent of the overall contour dimensions;
- optional-hole X/Y offsets drive preview and final geometry; the **3D-манипулятор
  отверстия** toggle starts Inventor's native triad in the selected-face coordinate system;
- the common **3D-манипулятор соединения** moves tenons, slots and the centered hole
  together along the only direction that keeps both tenons on their selected tube walls;
- the common manipulator starts automatically when the command panel opens;
- paired A/B tenons use one common root bridge based on the larger measured Trim/Notch
  gap, preventing one root envelope from appearing longitudinally shifted;
- a compact colored schematic places Width X and Height Y editors directly on the preview;
- opposite A/B roots are normalized to one profile station before preview and construction,
  removing the asymmetric shift caused by differently clipped Trim/Notch faces;
- A/B/C/D wall masks and common joint offsets are persisted; `↻ 90°` performs real
  discovery and selection of the second opposite C/D wall pair on rectangular tubes.

See `TEMPLATE_PARAMETERS.md` before converting a hand-edited sketch to fully parametric mode.

## What is different in this package

- the panel is on **Design**, not Assemble;
- select the **whole male tube occurrence**, then a planar face for the slot;
- assembly coordinates are converted through each occurrence transformation, so a rotated member keeps the joint orientation;
- the male side-view profile and the female slot come from an editable IPT template;
- the temporary base slot in `TJ_FEMALE_PROFILE` is a plain rectangle;
- all three editable sketches contain a construction cross and a center point at `(0,0)`;
- selectable side mode **A / B / A+B**; selected walls are highlighted in the assembly;
- the tenon uses explicit `Width X` and `Height Y` dimensions in millimetres;
- each tenon dimension can be automatic from the measured tube section or entered manually;
- automatic width/height formulas and rounding step are read from user parameters in the IPT template;
- slot target thickness uses the standard row `1 / 1.5 / 2 / 2.5 / 3 mm` or automatic detection;
- the default male profile has almost parallel working sides and only narrows at the rounded nose;
- an optional opening comes from a third editable template sketch;
- tenon width, height, slot thickness, and clearance use the dockable command panel;
- blue/green tenons, orange slots and the yellow optional opening are shown as real extruded template contours, not bounding boxes;
- preview rendering now uses Inventor triangle client graphics and therefore supports concave hand-edited contours more reliably than the former temporary B-Rep;
- the preview updates with side, size, thickness, clearance, and hole controls;
- the command panel is event-driven and no longer runs a nested `DoEvents` loop;
- successful creation closes silently; only errors produce a dialog;
- the panel uses one explicitly indexed layout, eliminating the duplicated editors from iteration 2;
- both wall thicknesses are measured automatically from the actual tube geometry;
- both IPT changes and the IAM record are wrapped in one global Inventor transaction for one-step Ctrl+Z;
- the selection rows show detected wall thickness, angle, and A/B end gaps; the old separate geometry section was removed;
- Frame Generator members without an existing end treatment launch the native Trim/Extend command first;
- existing Trim, Miter and Notch CUTDETAIL treatments are detected by their visible property name and left untouched;
- an intentional gap made by Trim/Notch is measured independently on A/B and the tenon root bridges it with 0.5 mm overlap;
- **Удалить все** removes all TubeJoint features and records in one undoable operation without touching Frame Generator treatments;
- the tenon direction no longer follows the incoming tube axis: it follows the selected female-face normal projected into the tenon wall;
- tenon and slot placement now shares the same wall-midpoint anchors, including Frame Generator Trim/Notch faces and intentional end gaps;
- saved zero-valued A/B placement offsets are already consumed by preview and real features, providing the data path for future 3D drag handles.

## Clean first install of this revision

Do not unpack this revision over the earlier source folder. Its archive has a unique root folder so it can be kept side by side.

1. Close Inventor.
2. Extract the ZIP.
3. Open PowerShell in the extracted source folder.
4. Run:

```powershell
.\build.ps1
.\install.ps1
```

5. Start Inventor 2027 and open an IAM assembly.
6. Use `Design → Шип-паз труб → Новое соединение`.

The installer writes to:

`%APPDATA%\Autodesk\Inventor 2027\Addins\TubeJoint`

## First geometry test

1. Work on copies of two different IPT files. Do not use two occurrences of the same source IPT.
2. Trim or Notch the male member against the female member. Existing Frame Generator CUTDETAIL is accepted, including one made with a gap. If the member has no end treatment, the add-in attempts to open native Trim/Extend and asks you to rerun the command afterward.
3. Click **Новое соединение**.
4. Pick the complete occurrence that receives the tenon.
5. Pick a planar wall face that receives the slot.
6. Choose `A`, `B`, or `A+B`; the chosen walls are highlighted and the colored solid preview shows the actual profiles from the standard file.
7. Check automatic `Width X` and `Height Y`, or clear `Auto` and enter millimetres manually.
8. Select automatic slot thickness or one of `1 / 1.5 / 2 / 2.5 / 3 mm`.
9. Enable or disable **Отверстие** and press **OK**.
10. Inspect `TJ_TENON_A_*` / `TJ_TENON_B_*` and `TJ_SLOT_*` inside the IPT files.
11. From the IAM, press Ctrl+Z once: the whole joint should disappear from both referenced IPTs.
12. Recreate one or more joints and press **Удалить все**. Confirm once; all TubeJoint operations disappear, while Frame Generator Notch/Trim remains. One Ctrl+Z restores the deletion.

This is a prototype: test on copies and save only after checking both features.

## Editing the standard

Click `Design → Шип-паз труб → Открыть стандарт`.

The first launch creates:

`%APPDATA%\Autodesk\Inventor 2027\TubeJoint\Templates\StandardSideTenonSlot_v6.ipt`

Edit these three named sketches, keeping each as one closed profile:

- `TJ_MALE_PROFILE` — side view; `(0,0)` is the marked center of the joint plane and +X points toward the mating tube;
- `TJ_FEMALE_PROFILE` — centered slot profile; the temporary default is a rectangle centered on `(0,0)`;
- `TJ_CENTER_VENT` — optional opening, also centered on `(0,0)`; the internal name is retained so an existing standard file remains compatible.

The construction cross and center point are reference geometry. Keep their
intersection at `(0,0)`. Construction entities are ignored when the add-in copies
the solved closed working contour.

The add-in does not scale the tenon or slot contour. It writes the requested dimensions,
thickness and clearance into the template, updates Inventor, and copies the solved
male and female profiles 1:1. The optional vent alone may receive the explicit uniform
percentage selected in the UI. The same contours drive preview and final features.

For a fully parametric `TJ_MALE_PROFILE`, constrain the root/joint plane to `X=0`,
drive the root-to-nose length with `TJ_TenonLength`, the transverse size with
`TJ_TenonAcrossWidth`, and every local root relief radius with `TJ_TenonRelief`.
`TJ_TenonThickness` is the material thickness used by the relief expression; it is
not an in-sketch extrusion dimension. `TJ_ProfileIsParametric` remains only as a
compatibility marker; the add-in always uses the solved sketch 1:1. Legacy aliases `TJ_TenonWidth` and `TJ_TenonHeight`
remain synchronized by the add-in but should not be used for new sketch dimensions.

For a small laser relief, edit the outer boundary of `TJ_MALE_PROFILE` using lines
and arcs and keep it as one closed contour. Do not create the relief as a second
inner loop. The marked `(0,0)` remains the joint plane; keep the root at or left
of `X=0`, and draw the working tenon toward `+X`.

For a Trim/Notch gap, keep some of `TJ_MALE_PROFILE` to the left of the marked `X=0` axis. The add-in stretches only that root region far enough to cross the measured gap; the requested tenon width to the right of `X=0` stays unchanged.

The automatic sizing rule is editable in the same IPT through these user parameters:

- `TJ_AutoWidthFactor`, `TJ_AutoWidthMinimum`, `TJ_AutoWidthMaximum`;
- `TJ_AutoHeightFactor`;
- `TJ_DimensionStep`.

Default rule: `Width X = 0.50 × tube X`, limited to `8–40 mm`; `Height Y = 0.55 × tube Y`; both are rounded to `0.5 mm`.

## Requirements

- Autodesk Inventor 2027
- .NET 8 SDK
- Inventor installed at `C:\Program Files\Autodesk\Inventor 2027` or pass `InventorInstallDir` to `dotnet build`

## Known limits for iteration 9

- only planar female faces and straight prismatic tube members;
- one or two opposite tenons and corresponding slots per command;
- wall thickness is inferred from the nearest parallel inner/outer faces of each tube;
- angled direction now follows the selected-face normal projected into the male wall; exact skewed-profile and assembly-clearance compensation is not yet applied (see `ANGLED_JOINTS.md`);
- the optional hole has a free planar triad; the common joint triad is intentionally
  limited to along-face translation so its tenons stay on the selected A/B walls;
- the 90-degree button switches between real A/B and C/D wall selections; it remains
  unavailable only when a second orthogonal pair cannot be identified on the member;
- the supported Inventor API exposes DockableWindow but not Autodesk's private built-in Property Panel controls; the command therefore uses a close visual and behavioural analogue;
- **Проверить соединения** validates saved records and does not yet rebuild existing geometry.
- **Удалить все** is intentionally assembly-wide in this prototype; selective deletion is planned later.
