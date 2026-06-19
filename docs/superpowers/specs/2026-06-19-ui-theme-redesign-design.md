# Llamashot — Full UI Theme Redesign (gradient, light/dark, 3D, responsive)

**Date:** 2026-06-19
**Branch:** feature/file-tools
**Status:** Design — pending user review
**Interactive mockup:** `docs/ui-redesign/mockup.html`

## 1. Goal

Give the entire Llamashot suite one cohesive, modern look:

- **Gradient UI** on backgrounds, headers and cards, in **both light and dark**.
- **Matching colours everywhere** — every window driven by one shared token palette.
- **3D-on-hover** effect (cards tilt toward the cursor with a specular glare + lift).
- **Fully responsive** for all screen sizes (min-sizes, reflowing layouts, scrollers).
- **Global theme setting, default System** (live-follows the Windows light/dark setting).
- A **user-facing accent/gradient customizer in Settings** with **Reset to defaults**.

## 2. Approved design tokens (from the mockup)

| Token | Value |
|---|---|
| Accent gradient | `#2DD4BF → #34D399` (teal → emerald) |
| Gradient angle | `135°` |
| Gradient intensity | **Vivid** (visible accent wash on window/header/cards) |
| Window/header tint | on (~8–15% accent) |
| 3D effect | card tilt on hover (rotateX/Y to cursor + glare + lift), card-only |
| Capture/recording overlays | stay **dark frosted glass** in both themes; accent only follows palette |
| Default theme | **System** |

`accent-text` is theme-correct (dark ink on bright teal in dark theme; white on the darker teal used in light theme). The mockup's forced `#FFFFFF` is a mockup simplification only.

## 3. Current state (the problem)

The app lives in **two theming worlds**:

- **Theme-aware (DynamicResource):** FileToolsWindow, ToolWorkspaceWindow, PasswordDialog, ColorPickerDialog, ConfirmDialog — but FileTools/Workspace still leak ~30 hardcoded brand/separator/shadow hex values.
- **Hardcoded dark (no theme at all):** HistoryWindow, SettingsWindow, AboutWindow, OcrResultWindow, GifQualityDialog, SignatureDialog, ScrollPreviewWindow, PinWindow, and every capture/recording window (OverlayWindow, RecordingOverlay, RecordingBorder, RecordingAnnotation, PreviewWindow, DelayedCaptureWindow).

Accent is also inconsistent: `#42A5F5` sky-blue in the dark windows vs `#6E8BFF` indigo in the themed ones.

`ThemeManager` swaps a merged `ResourceDictionary` and fires `ThemeChanged`; "System" is only honoured at startup (no live OS-change follow). The theme control today is a 2-state toggle button in FileTools/Workspace — there is **no Light/Dark/System picker** and **no accent customizer**.

## 4. Architecture

### 4.1 Token layer (`Themes/LightTheme.xaml`, `DarkTheme.xaml`)
Extend both dictionaries with the new keys (existing solid brushes stay for compatibility):

- **Accent:** `AccentBrush`, `AccentColor`, `Accent2Color`, `AccentTextBrush`, plus `AccentGradientBrush` (a `LinearGradientBrush` from `AccentColor→Accent2Color` at the chosen angle).
- **Surface gradients:** `WindowGradientBrush`, `HeaderGradientBrush`, `SurfaceGradientBrush` (LinearGradients; vivid = accent-tinted stops).
- **Glare:** `GlareBrush` (white-ish radial used by the 3D hover).
- Keep `WindowBrush`, `SurfaceBrush`, etc. as the solid fallbacks.

Concrete teal values per theme defined in the dictionaries; `AssemblyInfo`/`Shared.xaml` updated where gradients belong app-wide.

### 4.2 ThemeManager + accent customization (`Core/ThemeManager.cs`)
- Add **live System follow**: subscribe to `SystemEvents.UserPreferenceChanged`; when mode is System, re-resolve and re-apply on OS theme change.
- Add `ApplyAccent(startHex, endHex, angle, tint)`: rebuilds the accent + gradient brushes at runtime and overrides the dictionary entries, then fires `ThemeChanged`. Re-applied after every Light/Dark swap so custom accent survives theme changes.
- `ResetAccent()` restores the teal defaults.
- Persist via `AppSettings`.

### 4.3 Settings model (`Core/AppSettings.cs`)
New persisted fields (with teal defaults):
- `ThemeMode` (reuse existing `FileToolsTheme`: System|Light|Dark).
- `AccentStart="#2DD4BF"`, `AccentEnd="#34D399"`, `GradientAngle=135`, `WindowTint=true`.

### 4.4 3D hover (`Core/TiltBehavior.cs` — new attached property)
- Attached property `Tilt.IsEnabled="True"` on a card `Border`.
- On `MouseMove`: compute pointer fraction, apply a `PlaneProjection` (RotationX/Y up to ~6°) + `ScaleTransform` (~1.03) + deeper `DropShadowEffect`; move a radial `GlareBrush` overlay to the pointer.
- On `MouseLeave`: animate back to flat. GPU-cheap; respects a global on/off (tie to a setting later if desired).
- Applied to FileTools tool cards and History items.

### 4.5 Responsiveness
- Set sensible `MinWidth`/`MinHeight` on every window.
- Replace fixed card columns with `UniformGrid`/`WrapPanel` star-sizing already partly present; ensure content is inside `ScrollViewer`s.
- SettingsWindow: convert from fixed `500×700` `NoResize` to resizable with a min-size + scroll.

## 5. Settings ▸ Appearance (new, customizer + Reset)
A themed Appearance section at the top of Settings:
- **Theme** segmented picker: Light / Dark / **System**.
- **Accent gradient**: start + end colour (swatch opens the existing `ColorPickerDialog`), angle slider, **preset strip** (curated combos incl. the teal default), **"Tint window & header"** toggle.
- **↺ Reset** restores teal defaults.
- Live-applies via `ThemeManager.ApplyAccent` and persists on Save.

## 6. Migration scope (full parity — every window themed)

**Phase A — Token & engine:** extend theme dictionaries; ThemeManager accent/gradient + live System; AppSettings fields; TiltBehavior.

**Phase B — Already-themed cleanup:** FileToolsWindow + ToolWorkspaceWindow — remove the ~30 hardcoded hex (per-tool brand colours move to a themed accent-tinted scheme or keep brand hue but theme-safe), add gradients + tilt.

**Phase C — Migrate hardcoded-dark chrome windows to DynamicResource + gradients:** HistoryWindow, SettingsWindow (+ Appearance section), AboutWindow, OcrResultWindow.

**Phase D — Dialogs:** GifQualityDialog, SignatureDialog (+ keep ink surfaces white), PinWindow, ScrollPreviewWindow. PasswordDialog/ColorPickerDialog/ConfirmDialog already themed — just adopt new gradient tokens.

**Phase E — Capture/recording overlays:** OverlayWindow, RecordingOverlay, RecordingBorder, RecordingAnnotation, PreviewWindow, DelayedCaptureWindow. Keep dark frosted glass; route their accent (selection border, active tool, colour dot, Start button) through `AccentColor`/`AccentBrush` so they track the palette.

**Phase F — Verify:** harness modes per window group (light + dark + custom-accent screenshots via the existing `ShotRtb` reflection harness); manual pass.

## 7. Out of scope / deferred
- Per-element tilt beyond cards (kept card-only).
- Animated theme cross-fade transition (nice-to-have).
- Document ink colours (page-number/watermark/signature ink) stay independent of UI accent.

## 8. Risks
- WinForms type ambiguities in any new code-behind (use existing `GlobalUsings` aliases).
- Capture/recording windows use `WDA_EXCLUDEFROMCAPTURE` and Win32 click-through — retheme colours only, do not disturb the interop.
- Per-tool brand colours: must stay distinguishable in light theme (verify contrast).
- Version bump (csproj + AboutWindow.xaml) on the eventual commit; confirm before committing.
