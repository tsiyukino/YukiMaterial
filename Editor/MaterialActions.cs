using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// Every change the UI makes to a component, in one place and undoable.
    /// The panel and the inspectors call these; neither writes to a component
    /// itself, so there is one answer to what each operation means.
    /// </summary>
    static class MaterialActions
    {
        // ------------------------------------------------------- catalogues

        /// <summary>
        /// Adds one row per material slot of every renderer on <paramref name="source"/>
        /// (and, when it has none of its own, its children). Slots already in the
        /// list are left alone. Returns how many rows were added.
        /// </summary>
        public static int FillFrom(YukiMaterial config, GameObject source, bool includeChildren)
        {
            var renderers = new List<Renderer>();
            renderers.AddRange(source.GetComponents<Renderer>());
            if (renderers.Count == 0 || includeChildren)
                renderers.AddRange(source.GetComponentsInChildren<Renderer>(true).Where(r => !renderers.Contains(r)));

            int added = 0;
            UndoEdit.Begin(config, "Fill material slots");
            foreach (var renderer in renderers)
            {
                if (!(renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) continue;
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null) continue;
                    if (config.targets.Any(t => t != null && t.renderer == renderer && t.slot == i)) continue;
                    config.targets.Add(NewTarget(renderer, i, slots[i]));
                    added++;
                }
            }
            config.EnsureIds();
            UndoEdit.End(config);
            return added;
        }

        /// <summary>The component that owns this object's slots, made if there
        /// is none. One per object, so a slot has one home.</summary>
        public static YukiMaterial CatalogueFor(GameObject go)
        {
            var config = go.GetComponent<YukiMaterial>();
            if (config != null) return config;
            config = Undo.AddComponent<YukiMaterial>(go);
            config.EnsureIds();
            return config;
        }

        public static MaterialTarget AddSlot(YukiMaterial config, Renderer renderer, int slot)
        {
            UndoEdit.Begin(config, "Add material slot");
            var material = renderer != null && slot >= 0 && slot < renderer.sharedMaterials.Length
                ? renderer.sharedMaterials[slot]
                : null;
            var target = NewTarget(renderer, slot, material);
            config.targets.Add(target);
            config.EnsureIds();
            UndoEdit.End(config);
            return target;
        }

        public static void RemoveSlot(YukiMaterial config, MaterialTarget target)
        {
            if (target == null) return;
            foreach (var menu in MenusOf(config)) DropSlot(menu, config, target.id);
            UndoEdit.Begin(config, "Remove material slot");
            config.targets.Remove(target);
            UndoEdit.End(config);
        }

        /// <summary>
        /// Another look for this slot. It starts as a copy of nothing — the
        /// material as it is — so adding one and leaving it alone is the way a
        /// state says "this slot stays as it was".
        /// </summary>
        public static MaterialVariant AddVariant(YukiMaterial config, MaterialTarget target, string name = null)
        {
            if (target == null) return null;
            UndoEdit.Begin(config, "Add look");
            var variant = new MaterialVariant
            {
                id = YukiMaterial.NewId(),
                displayName = name ?? "",
            };
            target.variants.Add(variant);
            UndoEdit.End(config);
            return variant;
        }

        /// <summary>A copy of an existing look, which is how a second version of
        /// a careful edit gets made without redoing it.</summary>
        public static MaterialVariant DuplicateVariant(YukiMaterial config, MaterialTarget target, MaterialVariant source)
        {
            if (target == null || source == null) return null;
            UndoEdit.Begin(config, "Duplicate look");
            var copy = JsonUtility.FromJson<MaterialVariant>(JsonUtility.ToJson(source));
            copy.id = YukiMaterial.NewId();
            copy.displayName = MaterialSet.NameOf(target, source) + " ✱";
            foreach (var overlay in copy.edit.overlays)
                if (overlay != null) overlay.id = YukiMaterial.NewId();
            target.variants.Insert(target.variants.IndexOf(source) + 1, copy);
            UndoEdit.End(config);
            return copy;
        }

        public static void RemoveVariant(YukiMaterial config, MaterialTarget target, MaterialVariant variant)
        {
            if (target == null || variant == null) return;

            // Anything wearing it goes back to the slot's own material rather
            // than to a look that no longer exists.
            foreach (var menu in MenusOf(config))
            {
                var key = YukiMaterialMenu.KeyOf(config, target.id);
                bool touched = false;
                foreach (var state in menu.states)
                {
                    if (state == null || state.Wears(key) != variant.id) continue;
                    if (!touched) { UndoEdit.Begin(menu, "Remove look"); touched = true; }
                    state.Wear(key, MaterialVariant.Original);
                }
                if (touched) UndoEdit.End(menu);
            }

            UndoEdit.Begin(config, "Remove look");
            target.variants.Remove(variant);
            if (target.baseVariant == variant.id) target.baseVariant = MaterialVariant.Original;
            UndoEdit.End(config);
        }

        /// <summary>What the slot wears when no menu switches it.</summary>
        public static void SetBaseVariant(YukiMaterial config, MaterialTarget target, string variantId)
        {
            if (target == null || target.baseVariant == variantId) return;
            UndoEdit.Begin(config, "Set the slot's own look");
            target.baseVariant = variantId ?? MaterialVariant.Original;
            UndoEdit.End(config);
        }

        public static void Rename(Object owner, System.Action apply, string label = "Rename")
        {
            UndoEdit.Begin(owner, label);
            apply();
            UndoEdit.End(owner);
        }

        public static void ResetEdit(YukiMaterial config, MaterialVariant variant)
        {
            if (variant == null) return;
            UndoEdit.Begin(config, "Reset look");
            variant.edit = new MaterialEdit();
            UndoEdit.End(config);
        }

        public static void ResetProperty(YukiMaterial config, MaterialVariant variant, string propertyName)
        {
            if (variant == null) return;
            var property = variant.edit.Find(propertyName);
            if (property == null) return;
            UndoEdit.Begin(config, "Reset property");
            variant.edit.properties.Remove(property);
            UndoEdit.End(config);
        }

        // ------------------------------------------------------------ menus

        public static YukiMaterialMenu CreateMenu(GameObject host, string name)
        {
            var menu = Undo.AddComponent<YukiMaterialMenu>(host);
            menu.id = YukiMaterialMenu.NewId();
            menu.displayName = name ?? "";
            menu.EnsureIds();
            // A menu with one state is a button that does nothing, so it starts
            // with the two every switch needs: as it was, and something else.
            AddState(menu, MaterialText.L["ui.original"]);
            AddState(menu, null);
            menu.defaultState = menu.states[0].id;
            EditorUtility.SetDirty(menu);
            return menu;
        }

        public static MaterialState AddState(YukiMaterialMenu menu, string name)
        {
            UndoEdit.Begin(menu, "Add state");
            var state = new MaterialState
            {
                id = YukiMaterialMenu.NewId(),
                displayName = name ?? "",
                value = menu.nextValue++,
            };
            menu.states.Add(state);
            if (menu.states.Count == 1) menu.defaultState = state.id;
            UndoEdit.End(menu);
            return state;
        }

        public static MaterialState DuplicateState(YukiMaterialMenu menu, MaterialState source)
        {
            if (source == null) return null;
            UndoEdit.Begin(menu, "Duplicate state");
            var copy = JsonUtility.FromJson<MaterialState>(JsonUtility.ToJson(source));
            copy.id = YukiMaterialMenu.NewId();
            copy.value = menu.nextValue++;
            copy.displayName = MaterialSet.NameOf(source, menu.states.IndexOf(source) + 1) + " ✱";
            menu.states.Insert(menu.states.IndexOf(source) + 1, copy);
            UndoEdit.End(menu);
            return copy;
        }

        public static void RemoveState(YukiMaterialMenu menu, MaterialState state)
        {
            if (state == null || menu.states.Count <= 1) return;
            UndoEdit.Begin(menu, "Remove state");
            menu.states.Remove(state);
            if (menu.defaultState == state.id && menu.states.Count > 0) menu.defaultState = menu.states[0].id;
            UndoEdit.End(menu);
        }

        public static void MoveState(YukiMaterialMenu menu, MaterialState state, int delta)
        {
            int from = menu.states.IndexOf(state);
            int to = from + delta;
            if (from < 0 || to < 0 || to >= menu.states.Count) return;
            UndoEdit.Begin(menu, "Reorder states");
            menu.states.RemoveAt(from);
            menu.states.Insert(to, state);
            UndoEdit.End(menu);
        }

        public static void SetDefaultState(YukiMaterialMenu menu, MaterialState state)
        {
            if (state == null || menu.defaultState == state.id) return;
            UndoEdit.Begin(menu, "Set the state it starts in");
            menu.defaultState = state.id;
            UndoEdit.End(menu);
        }

        /// <summary>Points the menu at one more slot: a column of the table.</summary>
        public static MaterialSlotRef AddSlotRef(YukiMaterialMenu menu, YukiMaterial config, MaterialTarget target)
        {
            if (config == null || target == null) return null;
            var existing = menu.FindSlot(config, target.id);
            if (existing != null) return existing;

            UndoEdit.Begin(menu, "Add a slot to the menu");
            var slot = new MaterialSlotRef
            {
                key = YukiMaterialMenu.KeyOf(config, target.id),
                source = config,
                targetId = target.id,
            };
            menu.slots.Add(slot);
            UndoEdit.End(menu);
            return slot;
        }

        public static void RemoveSlotRef(YukiMaterialMenu menu, MaterialSlotRef slot)
        {
            if (slot == null) return;
            UndoEdit.Begin(menu, "Remove a slot from the menu");
            foreach (var state in menu.states)
                if (state != null) state.Forget(slot.key);
            menu.slots.Remove(slot);
            UndoEdit.End(menu);
        }

        public static void MoveSlotRef(YukiMaterialMenu menu, MaterialSlotRef slot, int delta)
        {
            int from = menu.slots.IndexOf(slot);
            int to = from + delta;
            if (from < 0 || to < 0 || to >= menu.slots.Count) return;
            UndoEdit.Begin(menu, "Reorder slots");
            menu.slots.RemoveAt(from);
            menu.slots.Insert(to, slot);
            UndoEdit.End(menu);
        }

        /// <summary>What one slot wears in one state: the cell of the table.</summary>
        public static void Wear(YukiMaterialMenu menu, MaterialState state, string slotKey, string variantId)
        {
            if (state == null || string.IsNullOrEmpty(slotKey)) return;
            if (state.Wears(slotKey) == (variantId ?? MaterialVariant.Original)) return;
            UndoEdit.Begin(menu, "Change what a slot wears");
            if (string.IsNullOrEmpty(variantId)) state.Forget(slotKey);
            else state.Wear(slotKey, variantId);
            UndoEdit.End(menu);
        }

        /// <summary>Every menu on the same avatar as this component.</summary>
        public static IEnumerable<YukiMaterialMenu> MenusOf(Component component)
        {
            if (component == null) yield break;
            var root = nadena.dev.ndmf.runtime.RuntimeUtil.FindAvatarInParents(component.transform);
            var scope = root != null ? root.gameObject : component.gameObject;
            foreach (var menu in scope.GetComponentsInChildren<YukiMaterialMenu>(true))
                if (menu != null) yield return menu;
        }

        static void DropSlot(YukiMaterialMenu menu, YukiMaterial config, string targetId)
        {
            var slot = menu.FindSlot(config, targetId);
            if (slot != null) RemoveSlotRef(menu, slot);
        }

        // --------------------------------------------------------- overlays

        public static OverlayLayer AddOverlay(YukiMaterial config, MaterialVariant variant)
        {
            if (variant == null) return null;
            UndoEdit.Begin(config, "Add overlay");
            var layer = new OverlayLayer { id = YukiMaterial.NewId() };
            variant.edit.overlays.Add(layer);
            UndoEdit.End(config);
            return layer;
        }

        public static void RemoveOverlay(YukiMaterial config, MaterialVariant variant, OverlayLayer layer)
        {
            if (variant == null) return;
            UndoEdit.Begin(config, "Remove overlay");
            variant.edit.overlays.Remove(layer);
            UndoEdit.End(config);
        }

        /// <summary>Moves a layer in the merge order; later layers go on top.</summary>
        public static void MoveOverlay(YukiMaterial config, MaterialVariant variant, OverlayLayer layer, int delta)
        {
            if (variant == null) return;
            var list = variant.edit.overlays;
            int from = list.IndexOf(layer);
            int to = from + delta;
            if (from < 0 || to < 0 || to >= list.Count) return;
            UndoEdit.Begin(config, "Reorder overlays");
            list.RemoveAt(from);
            list.Insert(to, layer);
            UndoEdit.End(config);
        }

        static MaterialTarget NewTarget(Renderer renderer, int slot, UnityEngine.Material material)
        {
            return new MaterialTarget
            {
                id = YukiMaterial.NewId(),
                renderer = renderer,
                slot = slot,
                source = material,
            };
        }
    }
}
