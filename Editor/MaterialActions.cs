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

        public static void ResetEdit(YukiMaterial config, MaterialTarget target)
        {
            UndoEdit.Begin(config, "Reset material");
            var variant = target.First;
            if (variant != null) variant.edit = new MaterialEdit();
            UndoEdit.End(config);
        }

        public static void ResetProperty(YukiMaterial config, MaterialTarget target, string propertyName)
        {
            var variant = target.First;
            if (variant == null) return;
            var property = variant.edit.Find(propertyName);
            if (property == null) return;
            UndoEdit.Begin(config, "Reset property");
            variant.edit.properties.Remove(property);
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
