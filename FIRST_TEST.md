# First test checklist — iteration 9

Use disposable copies of the assembly and both IPT files.

- [ ] Inventor 2027 was closed during install.
- [ ] Package `iteration9-v31` is shown by `build.ps1` and `install.ps1`.
- [ ] `build.ps1` completes without compiler errors.
- [ ] `install.ps1` completes.
- [ ] The **Шип-паз труб** panel appears on **Design** and no longer on Assemble.
- [ ] **Новое соединение** first asks for the whole male tube.
- [ ] The second selection asks for a planar female face.
- [ ] The `Properties` panel opens floating at a usable width and is not docked to the left.
- [ ] `Standard / Compact / Reinforced / Custom` presets change the manual dimensions.
- [ ] Width and height start in manual mode; Auto options are still available.
- [ ] Switching A / B / A+B highlights the corresponding male tube walls.
- [ ] A blue/green solid tenon preview and orange solid rectangular slot preview appear before pressing OK.
- [ ] The preview outline matches the editable template profiles rather than rectangular bounding boxes.
- [ ] Preview changes immediately after width, height, clearance, side, or vent changes.
- [ ] Width X and Height Y show real millimetres, not a scale coefficient.
- [ ] Width X and Height Y editors are placed directly on the colored schematic preview.
- [ ] Clearing `Auto` enables manual entry for the corresponding tenon dimension.
- [ ] There is no slot-thickness row; slot thickness follows the measured male-tube wall.
- [ ] Both wall thicknesses are shown beside the selected parts; there is no separate Geometry section.
- [ ] Rotating the male occurrence before creation does not lose the profile orientation.
- [ ] `TJ_TENON_A_*` and/or `TJ_TENON_B_*` appear according to the selected mode.
- [ ] `TJ_SLOT_*` appears in the female IPT.
- [ ] Successful creation closes the panel without an additional success message box.
- [ ] The checkbox is named **Отверстие** and creates the third cut in the middle of the female face.
- [ ] On an angled member, the tenon points toward the selected female face instead of following the male tube axis.
- [ ] Looking normal to the female face, each orange slot is centered on its corresponding blue/green tenon.
- [ ] One Ctrl+Z from the IAM removes the entire joint from both IPTs.
- [ ] A Frame Generator Notch is recognized as an existing treatment and does not trigger the Trim/Extend prompt.
- [ ] A small gap set in Trim/Notch is displayed as the A/B end gap and does not disconnect the tenon from the original tube wall.
- [ ] **Удалить все** removes all `TJ_TENON_*`, `TJ_SLOT_*`, and TubeJoint records but preserves Trim/Notch.
- [ ] One Ctrl+Z after **Удалить все** restores the deleted TubeJoint operations.
- [ ] **Открыть стандарт** opens `StandardSideTenonSlot_v6.ipt` with three closed profiles, visible `(0,0)` reference markers, and `TJ_Auto*` sizing parameters.
- [ ] After adding a small line/arc relief to the outer boundary of `TJ_MALE_PROFILE`, both preview and final tenon retain it.
- [ ] A small circular root relief stays circular when Width X or Height Y changes because its template constraint follows `TJ_TenonRelief`.
- [ ] Arbitrary concave root geometry from `TJ_MALE_PROFILE` remains visible where the replacement profile overlaps the pre-existing male tube wall.
- [ ] On an A+B joint made after Trim/Notch, both tenon roots lie on the same transverse station and neither side is shifted along the tube profile.
- [ ] The A/B anchors satisfy their respective wall plane, the selected female plane, and exactly the same longitudinal station.
- [ ] After constraining the template with `TJ_*` parameters, changing the tenon size does not affect `TJ_TenonRelief` or `TJ_SlotRelief` except through their thickness expressions.
- [ ] Changing Width X/Height Y rebuilds the constrained IPT sketches themselves; the add-in performs no external contour scaling.
- [ ] A deliberately non-proportional local feature retains its exact constrained size in preview and final geometry.
- [ ] Ellipses, elliptical arcs and B-splines in any named template contour no longer produce the old “only lines, arcs and circles” error.
- [ ] A closed Inventor **Control Vertex Spline** in `TJ_CENTER_VENT` appears in preview and creates the final cut.
- [ ] A mixed closed contour made from CV/interpolation splines plus ordinary lines/arcs appears in preview and creates the final tenon without E_INVALIDARG.
- [ ] A+B creates two independent slot Cuts from the mixed contour while one assembly Ctrl+Z removes the complete joint.
- [ ] A closed Inventor **Interpolation Spline** in `TJ_CENTER_VENT` creates the same separate Through All cut without conversion to line segments.
- [ ] The optional vent is created as its own `TJ_VENT_CUT_*` feature and is not omitted from the two-slot profile.
- [ ] The generated IPT contains that contour as one closed `SketchControlPointSpline`, not 160 short lines.
- [ ] The vent profile is prepared before the slot Cut and cuts only the selected female wall depth plus clearance.
- [ ] Creating the closed Control Vertex Spline no longer produces `E_INVALIDARG` in `Profiles.AddForSolid()`.
- [ ] Curves used in `TJ_MALE_PROFILE` remain native spline/arc/ellipse entities in `TJ_MALE_*`.
- [ ] Mixed line/arc/spline tenon contours remain one closed profile because adjacent entities share their endpoint `SketchPoint` objects.
- [ ] `Размер выреза, %` uniformly changes only the yellow vent in preview and final cut; 100% matches the solved template.
- [ ] A Trim/Notch gap larger than the negative-X root allowance is rejected instead of stretching the root contour.
- [ ] Hole X/Y values move only the yellow hole preview and the final hole cut.
- [ ] With **Отверстие** enabled, **3D-манипулятор отверстия** displays Inventor's triad on the hole; dragging in the selected-face X/Y plane updates the X/Y fields and yellow preview.
- [ ] **3D-манипулятор соединения** exposes only the safe along-face axis; dragging moves both tenons, both slots and the centered yellow hole together.
- [ ] The common 3D manipulator is already active when the command panel opens.
- [ ] If displayed A/B end gaps differ, both tenons still have the same root envelope and common station.
- [ ] The **Смещение вдоль** field follows the common manipulator and the created features match the preview.
- [ ] `↻ 90°` changes A/B labels and highlighted faces to C/D, then changes them back on the next press.
- [ ] Creating after rotation produces `TJ_TENON_C_*` and/or `TJ_TENON_D_*` on the actual second wall pair.
- [ ] After the main joint was shifted, the hole manipulator starts at the currently displayed yellow hole.
- [ ] Releasing the hole manipulator returns the visible handle to the main joint automatically.
- [ ] A very short hole drag retains its final value; switching back to the common
  manipulator does not move the joint or the hole to an earlier position.
- [ ] After manually editing an active manipulator's numeric offset, its next drag
  starts from the edited value instead of the previous triad origin.

If something fails, send the complete error text and one screenshot showing the selected members plus the model browser.
