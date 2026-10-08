# Changelog

## [2.0.0] - 2026-10-08

Now a UPM package (`com.bill.ss-outline`). Breaking: namespace and setup changed.

### Changed
- Everything is in the `BillSSOutline` namespace and the `BillSSOutline.Runtime` assembly (1.x had
  global-namespace classes in loose files).
- Selection uses **rendering layers** (`RenderingLayerMask`) instead of GameObject layers.
- Line width scales with the camera target's short side (`referenceShortSide`); per-camera multiplier
  via `OutlineCameraWidth`.
- Renderer inputs are requested only for the features in use (no DepthNormals prepass when normal
  edges are off); the selection redraw is skipped in FullScreen mode.

### Added
- `OutlineOverrides`: `MaterialDrivenLayers` (VAT/vertex-animated meshes draw their own mask pass),
  `AlphaCapableLayers` + `Shaders/OutlineAlphaMask.hlsl` (`BillOutlineAlpha()`), `AllowNormals`, and
  runtime look overrides (`Colour`, `Tint`, `Thickness`, `Hidden`).
- Object-tinted lines (`tintAmount`, `tintDarken`).
- MSAA-safe composite (the temporary target keeps the camera's sample count).
- Optional `BILL_OUTLINE_PROFILE_GUARD` define: Volumes use runtime profile copies in Play Mode.

### Fixed
- Turning the occlusion layer off at runtime no longer hands the composite a texture handle from an
  older frame.
- `OutlineOverrides.Hidden` now skips every pass instead of only the final composite.

## [1.0.0] - 2025-11-25

Initial release: screen-space outline renderer feature with depth/normal/colour edges, selection and
occlusion masks, distance and height fade.
