using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using TsiYuki.Core.Menus.Editor;
using TsiYuki.Core.Textures.Editor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(TsiYuki.Materials.Editor.MaterialPlugin))]

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// Turns the catalogues and the menus that point into them into their result
    /// on the build copy, and removes the components.
    ///
    /// A slot no menu drives simply wears its own look, applied to the slot. A
    /// menu gets one material per state per slot, one animator layer that swaps
    /// them, and Modular Avatar menu items.
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
                // Core settles menu placement once every TsiYuki tool has run,
                // so this has to be done by then.
                .BeforePlugin("moe.tsiyuki.core.menus")
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("Apply material edits", Execute)
                // NDMF only runs a filter a pass asks for; without this the
                // Scene view shows nothing at all.
                .PreviewingWith(new MaterialPreview());
        }

        static void Execute(BuildContext ctx)
        {
            var catalogues = ctx.AvatarRootObject.GetComponentsInChildren<YukiMaterial>(true);
            var menuComponents = ctx.AvatarRootObject.GetComponentsInChildren<YukiMaterialMenu>(true);
            if (catalogues.Length == 0 && menuComponents.Length == 0) return;

            var set = MaterialSet.Resolve(ctx.AvatarRootTransform);
            foreach (var warning in set.Warnings)
                MaterialText.Errors.Report(ErrorSeverity.NonFatal, warning.Key, warning.Context, warning.Args);
            foreach (var error in set.Errors)
                MaterialText.Errors.Report(ErrorSeverity.Error, error.Key, error.Context, error.Args);

            // Textures and materials made here are handed to NDMF, so the baker
            // must not own them; it is deliberately never disposed.
            var baker = new TextureBaker(compress: true);
            var patches = new Dictionary<Renderer, Dictionary<int, UnityEngine.Material>>();
            int count = 0;

            foreach (var menu in set.Menus)
            {
                var built = new Dictionary<ResolvedSlot, Dictionary<ResolvedState, UnityEngine.Material>>();

                foreach (var slot in menu.Slots)
                {
                    // One material per look, not per state: two states wearing
                    // the same look share it instead of baking it twice.
                    var perLook = new Dictionary<MaterialVariant, UnityEngine.Material>();
                    var perState = new Dictionary<ResolvedState, UnityEngine.Material>();

                    foreach (var state in menu.States)
                    {
                        var variant = menu.Wears(slot, state);
                        UnityEngine.Material material;
                        if (variant == null || !variant.Changes)
                        {
                            // The look that changes nothing needs nothing made
                            // for it: the material already in the slot is the
                            // answer, and a copy of it would only be dead weight
                            // in every state that is not about this slot.
                            material = slot.Original;
                        }
                        else if (!perLook.TryGetValue(variant, out material))
                        {
                            material = Bake(ctx, baker, slot, variant, MaterialSet.NameOf(slot.Target, variant));
                            perLook[variant] = material;
                        }
                        if (material != null) perState[state] = material;
                    }
                    if (perState.Count == 0) continue;
                    built[slot] = perState;

                    // The slot holds what the avatar spawns in.
                    UnityEngine.Material spawn;
                    if (menu.Default == null || !perState.TryGetValue(menu.Default, out spawn))
                        spawn = perState.Values.First();
                    Patch(patches, slot, spawn);
                    count++;
                }

                if (built.Count > 0) GenerateMenu(ctx, menu, built);
            }

            foreach (var slot in set.Permanent.ToList())
            {
                var variant = slot.Target.Find(slot.Target.baseVariant);
                if (variant == null || !variant.Changes) continue;
                var material = Bake(ctx, baker, slot, variant, MaterialSet.NameOf(slot.Target, variant));
                if (material == null) continue;
                Patch(patches, slot, material);
                count++;
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

            foreach (var config in catalogues)
                if (config != null) Object.DestroyImmediate(config);
            foreach (var menu in menuComponents)
                if (menu != null) Object.DestroyImmediate(menu);

            if (count > 0) Debug.Log($"[Yuki Material] Patched {count} material slot(s).");
        }

        static void Patch(Dictionary<Renderer, Dictionary<int, UnityEngine.Material>> patches,
                          ResolvedSlot slot, UnityEngine.Material material)
        {
            if (material == null) return;
            Dictionary<int, UnityEngine.Material> slots;
            if (!patches.TryGetValue(slot.Renderer, out slots))
                patches[slot.Renderer] = slots = new Dictionary<int, UnityEngine.Material>();
            slots[slot.Slot] = material;
        }

        /// <summary>The material for one look: property differences first, then
        /// the overlays merged into its textures.</summary>
        static UnityEngine.Material Bake(BuildContext ctx, TextureBaker baker, ResolvedSlot slot,
                                         MaterialVariant variant, string label)
        {
            var missing = new List<string>();
            var patched = MaterialDiff.Build(slot.Original, variant.edit,
                slot.Original.name + " (" + label + ")", name => missing.Add(name));
            if (patched == null) return null;

            foreach (var name in missing.Distinct())
                MaterialText.Errors.Report(ErrorSeverity.NonFatal, "warn.missing_property", slot.Original, new object[] { label, name });

            OverlayBaker.Apply(patched, variant.edit, baker, label,
                (key, args) => MaterialText.Errors.Report(key.StartsWith("info.") ? ErrorSeverity.Information : ErrorSeverity.NonFatal, key, slot.Original, args));

            ctx.AssetSaver.SaveAsset(patched);
            return patched;
        }

        static void GenerateMenu(BuildContext ctx, ResolvedMenu menu,
                                 Dictionary<ResolvedSlot, Dictionary<ResolvedState, UnityEngine.Material>> built)
        {
            void Persist(Object asset) => ctx.AssetSaver.SaveAsset(asset);

            // Generated components go on a fresh object so removing the config
            // component never matters.
            var host = new GameObject($"{menu.DisplayName} (Yuki Material)");
            host.transform.SetParent(ctx.AvatarRootTransform, false);

            var merge = host.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = MaterialAnimatorBuilder.Build(menu, ctx.AvatarRootTransform,
                (slot, state) =>
                {
                    Dictionary<ResolvedState, UnityEngine.Material> perState;
                    UnityEngine.Material material;
                    if (built.TryGetValue(slot, out perState) && perState.TryGetValue(state, out material)) return material;
                    return null;
                },
                menu.DisplayName + " FX", Persist);
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Absolute;
            merge.deleteAttachedAnimator = false;
            merge.matchAvatarWriteDefaults = false;

            var parameters = host.AddComponent<ModularAvatarParameters>();
            // One synced int, however many slots the menu moves.
            parameters.parameters = new List<ParameterConfig>
            {
                new ParameterConfig
                {
                    nameOrPrefix = menu.ParameterName,
                    syncType = ParameterSyncType.Int,
                    defaultValue = 0,
                    hasExplicitDefaultValue = true,
                    saved = menu.Saved,
                },
            };

            var menuRoot = MaterialMenuGenerator.Build(menu, host.transform);
            MenuPlacement.Place(menu.Config, menu.Config.menuParent, menuRoot, menu.DisplayName);
        }
    }
}
