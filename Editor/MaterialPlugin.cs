using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using TsiYuki.Core.Editor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(TsiYuki.Materials.Editor.MaterialPlugin))]

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// Turns every YukiMaterial component into its result on the build copy and
    /// removes the component.
    ///
    /// A component that applies its change permanently just patches the slot. A
    /// component that makes a menu gets one material per version, an animator
    /// layer that swaps them, and Modular Avatar menu items.
    ///
    /// Runs in Generating before Modular Avatar, like the wardrobe, so MA picks
    /// up the components generated here; that is also well before the Optimizing
    /// passes (NonToon conversion, Avatar Optimizer) which read the materials.
    /// </summary>
    public class MaterialPlugin : Plugin<MaterialPlugin>
    {
        public override string QualifiedName => "moe.tsiyuki.material";
        public override string DisplayName => "Yuki Material";

        protected override void Configure()
        {
            InPhase(BuildPhase.Generating)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("Apply material edits", Execute);
        }

        static void Execute(BuildContext ctx)
        {
            var configs = ctx.AvatarRootObject.GetComponentsInChildren<YukiMaterial>(true);
            if (configs.Length == 0) return;

            var set = MaterialSet.Resolve(ctx.AvatarRootTransform);

            foreach (var model in set.Models)
                foreach (var warning in model.Warnings)
                    Report(ErrorSeverity.NonFatal, warning.Key, warning.Context, warning.Args);
            foreach (var conflict in set.Conflicts())
                Report(ErrorSeverity.Error, conflict.Key, conflict.Context, conflict.Args);

            // Textures and materials made here are handed to NDMF, so the baker
            // must not own them; it is deliberately never disposed.
            var baker = new TextureBaker(compress: true);
            var claimed = new HashSet<string>();
            var patches = new Dictionary<Renderer, Dictionary<int, UnityEngine.Material>>();
            int count = 0;

            foreach (var model in set.Models)
            {
                // Two components patching one slot: the first in hierarchy order
                // wins and the rest are dropped, so the result never depends on
                // ordering we do not control.
                var mine = model.Targets.Where(t => claimed.Add(t.Key)).ToList();
                if (mine.Count == 0) continue;

                var built = new Dictionary<ResolvedTarget, Dictionary<MaterialVariant, UnityEngine.Material>>();

                foreach (var target in mine)
                {
                    var perVariant = new Dictionary<MaterialVariant, UnityEngine.Material>();
                    foreach (var variant in model.AsMenu ? target.Variants : new List<MaterialVariant> { target.Variants.FirstOrDefault() })
                    {
                        if (variant == null) continue;
                        var material = Bake(ctx, baker, target, variant, model.AsMenu);
                        if (material != null) perVariant[variant] = material;
                    }
                    if (perVariant.Count == 0) continue;
                    built[target] = perVariant;

                    // The slot holds what the avatar spawns with: the default
                    // version for a menu, the only one otherwise.
                    var spawn = model.AsMenu && target.Default != null && perVariant.ContainsKey(target.Default)
                        ? perVariant[target.Default]
                        : perVariant.Values.First();
                    if (!patches.TryGetValue(target.Renderer, out var slots))
                        patches[target.Renderer] = slots = new Dictionary<int, UnityEngine.Material>();
                    slots[target.Slot] = spawn;
                    count++;
                }

                if (model.AsMenu && built.Count > 0) GenerateMenu(ctx, model, built);
            }

            foreach (var pair in patches)
            {
                var materials = pair.Key.sharedMaterials;
                foreach (var slot in pair.Value)
                    if (slot.Key >= 0 && slot.Key < materials.Length) materials[slot.Key] = slot.Value;
                pair.Key.sharedMaterials = materials;
            }

            foreach (var texture in baker.Generated)
                if (texture != null) ctx.AssetSaver.SaveAsset(texture);

            foreach (var config in configs)
                if (config != null) Object.DestroyImmediate(config);

            if (count > 0) Debug.Log($"[Yuki Material] Patched {count} material slot(s).");
        }

        /// <summary>The material for one version: property differences first,
        /// then the overlays merged into its textures.</summary>
        static UnityEngine.Material Bake(BuildContext ctx, TextureBaker baker, ResolvedTarget target, MaterialVariant variant, bool named)
        {
            var missing = new List<string>();
            var label = named ? target.DisplayName + " " + target.VariantName(variant) : target.DisplayName;
            var patched = MaterialDiff.Build(target.Original, variant.edit,
                target.Original.name + " (" + label + ")", name => missing.Add(name));
            if (patched == null) return null;

            foreach (var name in missing.Distinct())
                Report(ErrorSeverity.NonFatal, "warn.missing_property", target.Original, new object[] { label, name });

            OverlayBaker.Apply(patched, variant.edit, baker, label,
                (key, args) => Report(key.StartsWith("info.") ? ErrorSeverity.Information : ErrorSeverity.NonFatal, key, target.Original, args));

            ctx.AssetSaver.SaveAsset(patched);
            return patched;
        }

        static void GenerateMenu(BuildContext ctx, MaterialModel model,
                                 Dictionary<ResolvedTarget, Dictionary<MaterialVariant, UnityEngine.Material>> built)
        {
            void Persist(Object asset) => ctx.AssetSaver.SaveAsset(asset);

            // Generated components go on a fresh object so removing the config
            // component (and its DisallowMultiple rule) never matters.
            var host = new GameObject($"{model.MenuName} (Yuki Material)");
            host.transform.SetParent(ctx.AvatarRootTransform, false);

            var targets = built.Keys.ToList();
            var merge = host.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = MaterialAnimatorBuilder.Build(targets, ctx.AvatarRootTransform,
                (t, v) => built[t].TryGetValue(v, out var m) ? m : null, model.MenuName + " FX", Persist);
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Absolute;
            merge.deleteAttachedAnimator = false;
            merge.matchAvatarWriteDefaults = false;

            var parameters = host.AddComponent<ModularAvatarParameters>();
            parameters.parameters = targets.Select(t => new ParameterConfig
            {
                nameOrPrefix = t.ParameterName,
                syncType = ParameterSyncType.Int,
                defaultValue = 0,
                hasExplicitDefaultValue = true,
                saved = model.Saved,
            }).ToList();

            var trimmed = new MaterialModel
            {
                Config = model.Config,
                MenuName = model.MenuName,
                MenuIcon = model.MenuIcon,
                AsMenu = true,
                Saved = model.Saved,
                Targets = targets,
            };
            MaterialMenuGenerator.Build(trimmed, host.transform);
        }

        static void Report(ErrorSeverity severity, string key, Object context, object[] args)
        {
            // Strings fill the message; a trailing Unity object becomes a
            // clickable reference in NDMF's error window.
            var all = (args ?? new object[0]).Select(a => (object)(a?.ToString() ?? "")).ToList();
            if (context != null) all.Add(context);
            ErrorReport.ReportError(MaterialText.Ndmf, severity, key, all.ToArray());
        }
    }
}
