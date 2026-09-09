# Inventor-style UI boundary

TubeJoint uses a real Inventor `DockableWindow`, but Inventor's private Property
Panel controls are not exposed for add-in composition. The child HWND therefore
remains WinForms and follows the visual/behavioral contract below.

## Theme

- Read `BrowserPane_BackgroundColor` and `BrowserPane_TextColor` through
  `ThemeManager.GetComponentThemeColor` when a command opens.
- Read the active highlight color when available.
- Derive editor, section, border, and muted-text shades from those base colors.
- Retain a dark fallback palette if theme access fails.

## Layout

- Default floating width: 330 px; minimum width: 285 px.
- Permit normal left/right docking.
- Do not overwrite Inventor's remembered user size or docking state.
- Use a top-to-bottom property workflow with compact 21 px section headers and
  25-27 px parameter rows.
- Keep the diagram responsive and small enough for a native-width property pane.

## Interaction

- Modeless joint input stays hosted in Inventor and owns no model transaction.
- Preview and manipulators continue to use the existing command events.
- Theme/layout changes must not alter joint geometry or stored parameters.
