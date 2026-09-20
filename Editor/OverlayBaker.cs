using System;
using System.Collections.Generic;
using System.Linq;
using TsiYuki.Core.Editor;
using UnityEngine;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// Merges overlay images into the material's own textures, the way you
    /// would flatten a layer onto the base in an image editor. The result is a
    /// new texture assigned to the material; no shader feature is used for the
    /// merge, so this works with any shader and survives later conversions.
    /// </summary>
    public static class OverlayBaker
    {
        /// <summary>Texture properties tried for each target, in order.</summary>
        static readonly string[] BaseColorProperties = { "_MainTex", "_BaseMap", "_BaseColorMap", "_MainTexture", "_Albedo" };
        static readonly string[] EmissionProperties = { "_EmissionMap", "_EmissionMap0", "_EmissionTexture", "_Emissive" };

        public static string ResolveProperty(UnityEngine.Material material, OverlayLayer layer)
        {
            if (layer == null || material == null) return null;
            if (!string.IsNullOrWhiteSpace(layer.property))
                return material.HasProperty(layer.property.Trim()) ? layer.property.Trim() : null;
            var candidates = layer.target == OverlayTarget.Emission ? EmissionProperties : BaseColorProperties;
            foreach (var name in candidates)
                if (material.HasProperty(name)) return name;
            return null;
        }

        /// <summary>Active layers of one edit, grouped by the property they land in.</summary>
        static List<KeyValuePair<string, List<OverlayLayer>>> Group(UnityEngine.Material material, MaterialEdit edit,
                                                                   string label, Action<string, object[]> warn)
        {
            var groups = new List<KeyValuePair<string, List<OverlayLayer>>>();
            foreach (var layer in edit.overlays)
            {
                if (layer == null || !layer.IsActive) continue;
                var property = ResolveProperty(material, layer);
                if (property == null)
                {
                    warn?.Invoke("warn.no_texture_slot", new object[] { label, layer.target.ToString() });
                    continue;
                }
                var bucket = groups.FirstOrDefault(p => p.Key == property);
                if (bucket.Value == null)
                {
                    bucket = new KeyValuePair<string, List<OverlayLayer>>(property, new List<OverlayLayer>());
                    groups.Add(bucket);
                }
                bucket.Value.Add(layer);
            }
            return groups;
        }

        static BaseBakeSpec Spec(UnityEngine.Material material, string property, List<OverlayLayer> layers,
                                 string label, Action<string, object[]> warn)
        {
            var spec = new BaseBakeSpec
            {
                Name = label + " " + property.TrimStart('_'),
                MainTexture = material.GetTexture(property),
            };

            // With no texture to merge into, the compositor starts from white.
            // That is the neutral for a base colour, but for a glow map it means
            // "everything glows", and an additive layer cannot bring it back
            // down. Emission starts from black instead, so only what the layer
            // draws lights up.
            if (spec.MainTexture == null && layers[0].target == OverlayTarget.Emission)
                spec.Color = new Color(0f, 0f, 0f, 1f);

            foreach (var layer in layers)
            {
                if (spec.MainTexture != null && layer.texture != null && Mismatched(spec.MainTexture, layer.texture))
                    warn?.Invoke("warn.uv_mismatch", new object[] { label, layer.texture.name });

                spec.Layers.Add(new BaseBakeSpec.Layer
                {
                    Texture = layer.texture,
                    // Alpha carries the opacity: the compositor multiplies it by
                    // the image's own alpha and by the mask.
                    Color = new Color(layer.tint.r, layer.tint.g, layer.tint.b, layer.tint.a * Mathf.Clamp01(layer.opacity)),
                    BlendMask = layer.mask,
                    BlendMode = (int)layer.blend,
                    AlphaMode = 0,       // never touch the base alpha
                    EnableLighting = 1f, // the compositor's overall layer weight
                    Scale = layer.scale == Vector2.zero ? Vector2.one : layer.scale,
                    Offset = layer.offset,
                });
            }
            return spec;
        }

        /// <summary>
        /// Composites every active layer of <paramref name="edit"/> into
        /// <paramref name="material"/>. The baker keeps ownership of the
        /// textures it creates.
        /// </summary>
        /// <param name="warn">key + args, matching the localization table.</param>
        public static void Apply(UnityEngine.Material material, MaterialEdit edit, TextureBaker baker,
                                 string label, Action<string, object[]> warn = null)
        {
            if (material == null || edit == null || baker == null || !edit.HasOverlays) return;

            bool needEmission = false;
            foreach (var entry in Group(material, edit, label, warn))
            {
                if (entry.Value[0].target == OverlayTarget.Emission) needEmission = true;

                var scale = material.GetTextureScale(entry.Key);
                var offset = material.GetTextureOffset(entry.Key);

                bool baked;
                var result = baker.BakeBase(Spec(material, entry.Key, entry.Value, label, warn), out baked);
                if (result == null) continue;

                material.SetTexture(entry.Key, result);
                material.SetTextureScale(entry.Key, scale);
                material.SetTextureOffset(entry.Key, offset);
            }

            if (needEmission) EnableEmission(material, label, warn);
        }

        /// <summary>
        /// The composited texture for one target, without touching the material.
        /// Used to show the result in the inspector; returns null when nothing
        /// would change.
        /// </summary>
        public static Texture Composite(UnityEngine.Material material, MaterialEdit edit, TextureBaker baker, OverlayTarget target)
        {
            if (material == null || edit == null || baker == null || !edit.HasOverlays) return null;
            foreach (var entry in Group(material, edit, "preview", null))
            {
                if (entry.Value[0].target != target) continue;
                bool baked;
                return baker.BakeBase(Spec(material, entry.Key, entry.Value, "preview", null), out baked);
            }
            return null;
        }

        /// <summary>True when any active layer lands on this target.</summary>
        public static bool Targets(UnityEngine.Material material, MaterialEdit edit, OverlayTarget target)
        {
            if (material == null || edit == null) return false;
            foreach (var layer in edit.overlays)
                if (layer != null && layer.IsActive && layer.target == target) return true;
            return false;
        }

        /// <summary>
        /// An emission layer is pointless on a material with emission switched
        /// off, so the switch is flipped. This is the one place an overlay
        /// changes something besides a texture, and it is reported.
        /// </summary>
        static void EnableEmission(UnityEngine.Material material, string label, Action<string, object[]> warn)
        {
            var flipped = new List<string>();

            foreach (var toggle in new[] { "_UseEmission", "_EnableEmission", "_EmissionEnabled" })
                if (material.HasProperty(toggle) && material.GetFloat(toggle) < 0.5f)
                {
                    material.SetFloat(toggle, 1f);
                    flipped.Add(toggle);
                }

            if (material.HasProperty("_EmissionColor") && material.GetColor("_EmissionColor").maxColorComponent <= 0.001f)
            {
                material.SetColor("_EmissionColor", Color.white);
                flipped.Add("_EmissionColor");
            }

            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags &= ~MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            if (flipped.Count > 0)
                warn?.Invoke("info.emission_enabled", new object[] { label, string.Join(", ", flipped.ToArray()) });
        }

        /// <summary>
        /// An overlay is drawn for one UV layout; a different shape is a strong
        /// hint it was made for another model. Not proof, so this only warns.
        /// </summary>
        static bool Mismatched(Texture main, Texture overlay)
        {
            if (main.width <= 0 || main.height <= 0 || overlay.width <= 0 || overlay.height <= 0) return false;
            var a = main.width / (float)main.height;
            var b = overlay.width / (float)overlay.height;
            return Mathf.Abs(a - b) > 0.01f;
        }

        /// <summary>
        /// What a dropped image most likely is, so the common case needs no
        /// configuring. Guesses only the two fields the simple UI shows.
        /// </summary>
        public static void Guess(Texture texture, out OverlayTarget target, out OverlayBlend blend)
        {
            target = OverlayTarget.BaseColor;
            blend = OverlayBlend.Normal;
            if (texture == null) return;

            var pixels = TextureBaker.ReadAt(texture, 128);
            if (pixels == null) return;

            int count = pixels.Width * pixels.Height;
            if (count == 0) return;

            bool hasAlpha = false, colored = false;
            int black = 0;
            for (int i = 0; i < count; i++)
            {
                var c = pixels.Get(i);
                if (c.a < 0.99f) hasAlpha = true;
                var max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                var min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                if (max - min > 0.04f) colored = true;
                if (max < 0.02f) black++;
            }

            // A glow map is marks drawn on black: opaque, colourless, and mostly
            // pure black. Counting black pixels rather than averaging is what
            // separates it from a flat grey image, whose average is also lowish
            // but which has no black in it at all.
            if (!hasAlpha && !colored && black >= count * 0.6)
            {
                target = OverlayTarget.Emission;
                blend = OverlayBlend.Add;
            }
        }
    }
}
