# Changelog

## [Unreleased] — 0.1.0

### Added
- **Overlay mode.** Drop images onto the drop area and each is merged into the material's own texture at
  build time, the way you would flatten a PSD layer onto the base — which is what texture and makeup packs
  normally ask you to do by hand in an image editor. No shader feature is used for the merge, so it works
  with any shader and survives later conversions. Each layer shows its image and needs only two settings
  (where it goes, opacity); blend mode, mask, tint, scale, offset and an explicit texture property sit
  behind Advanced, and every one of them defaults to changing nothing.
- The composited result is shown in the inspector, per target, so the effect is visible without hunting for
  it in the Scene view. The shader's own inspector is folded away by default, since it is the advanced path
  and is hundreds of rows tall.
- Where a dropped image goes is guessed from the image: marks on a black field become a glow layer added to
  emission, anything else is merged into the base colour. A glow layer switches emission on if the material
  had it off, and says so.
- A warning when an overlay's shape does not match the texture it is merged into, since overlays are drawn
  for one UV layout.
- **Override mode.** Edit any material through its own shader inspector (lilToon, Poiyomi and anything
  else with a custom `ShaderGUI`) and keep only the difference from the original. The change is applied
  to a copy at build time through NDMF: the material asset is untouched and nothing is written into
  `Assets/`.
- Targets are a list of renderer + material slot. A component added to an object fills the list from
  that object's own renderers; rows you do not want are deleted by hand.
- Scene view preview through NDMF, so an edit is visible without building.
- Errors when two components claim the same renderer and slot.
- Added from the Inspector's **Add Component > TsiYuki > Yuki Material**, or by right-clicking an
  object in the Hierarchy and choosing **TsiYuki > Edit Materials**, which also fills the target list.
- English, Chinese and Japanese UI (TsiYuki > Language).
