using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using TsiYuki.Core.Editor;
using UnityEngine;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// Shows material edits in the Scene and Game views without building: NDMF
    /// renders proxy copies of the renderers and this filter gives the proxies
    /// patched materials. Toggle it from NDMF's preview menu.
    /// </summary>
    internal sealed class MaterialPreview : IRenderFilter
    {
        internal static readonly TogglablePreviewNode Toggle =
            TogglablePreviewNode.Create(() => MaterialText.L["preview.name"], "moe.tsiyuki.material/preview", true);

        public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes()
        {
            yield return Toggle;
        }

        public bool IsEnabled(ComputeContext context) => context.Observe(Toggle.IsEnabled);

        /// <summary>
        /// The version the Scene view should show for one slot, or null when
        /// there is nothing to show.
        ///
        /// A component that applies its change permanently previews it as soon
        /// as it is set up — there is only one answer, so asking would be
        /// pointless. A component whose versions become a menu previews the one
        /// the avatar spawns with, and only shows another when Try on says so.
        /// </summary>
        internal static MaterialVariant Shows(YukiMaterial config, MaterialTarget target)
        {
            if (target == null || target.variants.Count == 0) return null;

            if (!config.asMenu)
            {
                var only = target.First;
                return only != null && only.edit != null && !only.edit.IsEmpty ? only : null;
            }

            var chosenId = MaterialPreviewState.Current(config, target);
            var variant = chosenId != null
                ? target.variants.FirstOrDefault(v => v != null && v.id == chosenId)
                : null;
            if (variant == null)
                variant = target.variants.FirstOrDefault(v => v != null && v.id == target.defaultVariant)
                          ?? target.First;

            return variant != null && variant.edit != null && !variant.edit.IsEmpty ? variant : null;
        }

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            var groups = ImmutableList.CreateBuilder<RenderGroup>();
            foreach (var root in context.GetAvatarRoots())
            {
                foreach (var component in context.GetComponentsInChildren<YukiMaterial>(root, true))
                {
                    if (component == null) continue;
                    // Re-run whenever any field of the component changes, or when
                    // Try on points at a different version.
                    context.Observe(component, c => JsonUtility.ToJson(c), (a, b) => a == b);
                    MaterialPreviewState.Observe(context);

                    var renderers = component.targets
                        .Where(t => t != null && t.renderer != null && Shows(component, t) != null)
                        .Select(t => t.renderer)
                        .Distinct()
                        .ToList();
                    if (renderers.Count == 0) continue;

                    groups.Add(RenderGroup.For(renderers).WithData(component, (a, b) => a == b));
                }
            }
            return groups.ToImmutable();
        }

        public Task<IRenderFilterNode> Instantiate(RenderGroup group, IEnumerable<(Renderer, Renderer)> proxyPairs, ComputeContext context)
        {
            var component = group.GetData<YukiMaterial>();
            var node = new Node();
            foreach (var (original, proxy) in proxyPairs)
            {
                foreach (var material in original.sharedMaterials)
                    if (material != null) context.Observe(material);
                node.Prepare(component, original, proxy);
            }
            return Task.FromResult<IRenderFilterNode>(node);
        }

        private sealed class Node : IRenderFilterNode
        {
            readonly Dictionary<Renderer, UnityEngine.Material[]> _results = new Dictionary<Renderer, UnityEngine.Material[]>();
            readonly List<UnityEngine.Material> _owned = new List<UnityEngine.Material>();
            // Small textures: this runs on every edit and has to stay responsive.
            readonly TextureBaker _baker = new TextureBaker(compress: false, maxSize: 1024);

            public RenderAspects WhatChanged => RenderAspects.Material;

            public void Prepare(YukiMaterial component, Renderer original, Renderer proxy)
            {
                if (proxy == null || component == null) return;
                var materials = proxy.sharedMaterials;
                bool any = false;

                foreach (var target in component.targets)
                {
                    if (target == null || target.renderer != original) continue;
                    var variant = Shows(component, target);
                    if (variant == null) continue;
                    if (target.slot < 0 || target.slot >= materials.Length) continue;
                    var source = materials[target.slot];
                    if (source == null) continue;

                    var patched = MaterialDiff.Build(source, variant.edit, source.name + " (preview)");
                    patched.hideFlags = HideFlags.HideAndDontSave;
                    OverlayBaker.Apply(patched, variant.edit, _baker, source.name);
                    _owned.Add(patched);
                    materials[target.slot] = patched;
                    any = true;
                }

                if (!any) return;
                _results[original] = materials;
                proxy.sharedMaterials = materials;
            }

            public void OnFrame(Renderer original, Renderer proxy)
            {
                if (proxy != null && _results.TryGetValue(original, out var materials)) proxy.sharedMaterials = materials;
            }

            public void Dispose()
            {
                foreach (var material in _owned)
                    if (material != null) Object.DestroyImmediate(material);
                _owned.Clear();
                _results.Clear();
                _baker.Dispose();
            }
        }
    }
}
