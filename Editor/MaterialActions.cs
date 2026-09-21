using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TsiYuki.Materials.Editor
{
    static class MaterialActions
    {
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

        public static MaterialTarget Add(YukiMaterial config, Renderer renderer, int slot)
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

        public static void Remove(YukiMaterial config, MaterialTarget target)
        {
            UndoEdit.Begin(config, "Remove material slot");
            config.targets.Remove(target);
            UndoEdit.End(config);
        }

        public static void ResetEdit(YukiMaterial config, MaterialVariant variant)
        {
            if (variant == null) return;
            UndoEdit.Begin(config, "Reset material");
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

        public static MaterialVariant AddVariant(YukiMaterial config, MaterialTarget target)
        {
            UndoEdit.Begin(config, "Add version");
            var variant = new MaterialVariant { id = YukiMaterial.NewId(), value = target.nextValue++ };
            target.variants.Add(variant);
            if (string.IsNullOrEmpty(target.defaultVariant)) target.defaultVariant = target.variants[0].id;
            UndoEdit.End(config);
            return variant;
        }

        public static void RemoveVariant(YukiMaterial config, MaterialTarget target, MaterialVariant variant)
        {
            if (target.variants.Count <= 1) return;
            UndoEdit.Begin(config, "Remove version");
            target.variants.Remove(variant);
            if (target.defaultVariant == variant.id) target.defaultVariant = target.variants[0].id;
            UndoEdit.End(config);
        }

        public static void SetDefaultVariant(YukiMaterial config, MaterialTarget target, MaterialVariant variant)
        {
            UndoEdit.Begin(config, "Set default version");
            target.defaultVariant = variant.id;
            UndoEdit.End(config);
        }

        public static OverlayLayer AddOverlay(YukiMaterial config, MaterialTarget target, MaterialVariant variant)
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
            var target = new MaterialTarget
            {
                id = YukiMaterial.NewId(),
                renderer = renderer,
                slot = slot,
                source = material,
            };
            target.variants.Add(new MaterialVariant { id = YukiMaterial.NewId(), value = target.nextValue++ });
            return target;
        }
    }
}
