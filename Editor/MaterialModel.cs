using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TsiYuki.Materials.Editor
{
    public class ModelWarning
    {
        public string Key;
        public object[] Args;
        public Object Context;

        public ModelWarning(string key, Object context, params object[] args)
        {
            Key = key;
            Context = context;
            Args = args;
        }

        public string Message => MaterialText.L.Tr(Key, Args);
    }

    /// <summary>One material slot that is actually going to be patched.</summary>
    public class ResolvedTarget
    {
        public YukiMaterial Config;
        public MaterialTarget Source;
        public Renderer Renderer;
        public int Slot;

        // What sits in the slot right now. The edit is applied on top of this,
        // not on top of the material it was authored against, so a slot another
        // tool already changed still gets the edit.
        public UnityEngine.Material Original;
        public MaterialEdit Edit;
        public string DisplayName;

        // Every version of this slot, in menu order. A component that applies
        // its change permanently has exactly one and uses Edit.
        public List<MaterialVariant> Variants = new List<MaterialVariant>();
        public MaterialVariant Default;

        public string Key => Renderer.GetInstanceID() + "#" + Slot;
    }

    /// <summary>One YukiMaterial component resolved for a build or for display.</summary>
    public class MaterialModel
    {
        public YukiMaterial Config;
        public List<ResolvedTarget> Targets = new List<ResolvedTarget>();
        public List<ModelWarning> Warnings = new List<ModelWarning>();

        public static string Fallback(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        public static string NameOf(MaterialTarget target)
        {
            if (target == null) return "?";
            var material = target.source;
            return Fallback(target.displayName, material != null ? material.name : "?");
        }

        public static MaterialModel Resolve(YukiMaterial config, Transform avatarRoot)
        {
            var model = new MaterialModel { Config = config };
            if (config == null) return model;

            foreach (var target in config.targets)
            {
                if (target == null) continue;
                var name = NameOf(target);

                if (target.renderer == null)
                {
                    model.Warnings.Add(new ModelWarning("warn.missing_renderer", config));
                    continue;
                }
                if (avatarRoot != null && !target.renderer.transform.IsChildOf(avatarRoot))
                {
                    model.Warnings.Add(new ModelWarning("warn.outside_avatar", target.renderer, target.renderer.name));
                    continue;
                }

                var slots = target.renderer.sharedMaterials;
                if (target.slot < 0 || target.slot >= slots.Length)
                {
                    model.Warnings.Add(new ModelWarning("warn.slot_out_of_range", target.renderer, name, target.slot));
                    continue;
                }

                var current = slots[target.slot];
                if (current == null)
                {
                    model.Warnings.Add(new ModelWarning("warn.missing_source", target.renderer, name));
                    continue;
                }
                if (target.source != null && current != target.source)
                    model.Warnings.Add(new ModelWarning("warn.source_changed", target.renderer, name, current.name));

                var variants = target.variants.Where(v => v != null && v.edit != null).ToList();
                var variant = target.First;
                var edit = variant != null ? variant.edit : null;

                // A menu keeps its slot even when the first version changes
                // nothing, because that is exactly what an "original" option is.
                bool anything = config.asMenu ? variants.Count > 1 : (edit != null && !edit.IsEmpty);
                if (!anything) continue;

                if (MaterialDiff.IsLockedPoiyomi(current) &&
                    variants.Any(v => v.edit.properties.Any(p => p != null && p.kind != PropertyKind.Texture)))
                    model.Warnings.Add(new ModelWarning("warn.locked_shader", current, name));

                var chosen = variants.FirstOrDefault(v => v.id == target.defaultVariant) ?? variants.FirstOrDefault();

                model.Targets.Add(new ResolvedTarget
                {
                    Config = config,
                    Source = target,
                    Renderer = target.renderer,
                    Slot = target.slot,
                    Original = current,
                    Edit = edit,
                    DisplayName = name,
                    Variants = variants,
                    Default = chosen,
                });
            }
            return model;
        }
    }

    /// <summary>Every YukiMaterial on one avatar, resolved together so two of
    /// them claiming the same slot can be reported.</summary>
    public class MaterialSet
    {
        public Transform AvatarRoot;
        public List<MaterialModel> Models = new List<MaterialModel>();

        public IEnumerable<ResolvedTarget> AllTargets => Models.SelectMany(m => m.Targets);

        public static MaterialSet Resolve(Transform avatarRoot)
        {
            var set = new MaterialSet { AvatarRoot = avatarRoot };
            foreach (var config in avatarRoot.GetComponentsInChildren<YukiMaterial>(true))
            {
                config.EnsureIds();
                set.Models.Add(MaterialModel.Resolve(config, avatarRoot));
            }
            return set;
        }

        /// <summary>Slots claimed by more than one component.</summary>
        public IEnumerable<ModelWarning> Conflicts()
        {
            foreach (var group in AllTargets.GroupBy(t => t.Key))
            {
                var components = group.Select(t => t.Config).Distinct().ToList();
                if (components.Count < 2) continue;
                var first = group.First();
                yield return new ModelWarning("conflict.two_components", first.Renderer,
                    first.Renderer.name, first.Slot,
                    string.Join(", ", components.Select(c => c.gameObject.name)));
            }
        }
    }
}
