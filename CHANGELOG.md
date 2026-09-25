# Changelog

## Unreleased

- Undo-safe editing and the FX layer's states and entry transitions now come from TsiYuki Core 0.4.0
  instead of this package's own copies. The generated controller is the same. Requires TsiYuki Core 0.4.0.
- Build warnings and errors are reported to NDMF through TsiYuki Core's `YukiNdmfReport`. The messages are
  unchanged.
- Editor window: one spacing scale throughout, with padding at the edges; the list
  on the left sits on its own ground with a divider between it and the detail.
- Editor window: list rows are all one height, selection is a highlight rather
  than a box, and the counts line up on the right.
- Editor window: each pane opens with a title, a line saying where it lives,
  and its actions at the right; sections have room above them.
- Editor window: the state table has a header strip and alternating rows, and is
  tall enough for its rows without a scrollbar of its own.
- Editor window: look and state icons are a small picture you click or drop a
  texture on, instead of a field too narrow to read.

## [0.2.0] - 2026-09-22

Rebuilt around two ideas: a component says what one object's materials **can** look like, and a menu
says **when** they look like it. They are separate because the interesting cases cross objects — the hair
and the ears turning pink together — and an object's own component can never reach another object.

### Added
- **Menus are their own component.** `Yuki Material Menu`, usually on the avatar root, refers to material
  slots wherever they live and switches them together. One menu is one synced int and one submenu,
  however many slots on however many objects it moves.
- **The table.** A menu's states run down it and the slots it drives run across; each cell says what that
  slot wears in that state. "Pink" is one row that says what the hair wears and what the ears wear — which
  no inspector could ever show, because the two live on different objects.
- **The material a slot already has is always a choice**, in every cell, without being stored as anything.
  It is what a state that is not about a slot means, and it is the way back once a menu has switched away
  from what the avatar shipped with.
- **The panel is where the work happens.** Editing a material, making looks, wiring them into menus,
  naming things, install-into, the parameter and what it costs: all of it is on one screen, with the
  avatar's objects and menus listed down the side.
- Adding a slot to a menu offers every material slot on the avatar, set up or not; one that is not set up
  is set up as it is added. Nothing has to be prepared on the other object first.
- A look can be made from inside the table: the last entry in every cell makes one and wears it.
- **Drag instead of picking from a list.** An object dragged in from the Hierarchy is set up where it
  lands: on the list it becomes a catalogue, on a menu's table it becomes columns for every slot it has
  going spare. A slot dragged out of the list onto a table joins that menu. The bar that takes the drop
  is also the button that opens the list, for when dragging is the longer way round.
- Try on switches the whole menu in the Scene view, so both halves of "hair and ears" move at once.
- **The difference shows its values.** What the material had and what the look puts there instead, side by
  side: thumbnails for textures, swatches for colours (with the HDR multiplier and alpha spelled out),
  numbers for the rest. Two textures that share a file name and are different files — the usual result of
  an avatar having been duplicated — show enough of their paths to tell them apart, which a list of
  property names hid completely.

### Changed
- **The component inspectors are now summaries.** A catalogue lists its slots, a menu lists its states and
  the slots it drives; both open the panel. Nothing is edited from the Inspector any more.
- A slot with no menu on it wears whichever look is set as its own, applied on upload. That replaces the
  old "apply permanently" mode of the component, and it is now a per-slot choice rather than a per-object
  one.
- Two components on one slot, or two menus on one slot, are reported rather than resolved.

### Removed
- Versions, groups and the "make a menu" switch on `Yuki Material`. Switching lives in the menu component,
  and grouping is what a menu's columns are. Configurations from 0.1.0 are not carried over.
