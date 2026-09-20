# Changelog

## [Unreleased] — 0.1.0

### Added
- **Override mode.** Edit any material through its own shader inspector (lilToon, Poiyomi and anything
  else with a custom `ShaderGUI`) and keep only the difference from the original. The change is applied
  to a copy at build time through NDMF: the material asset is untouched and nothing is written into
  `Assets/`.
- Targets are a list of renderer + material slot. A component added to an object fills the list from
  that object's own renderers; rows you do not want are deleted by hand.
- Scene view preview through NDMF, so an edit is visible without building.
- Errors when two components claim the same renderer and slot.
- English, Chinese and Japanese UI (TsiYuki > Language).
