using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace TsiYuki.Materials
{
    public enum PropertyKind
    {
        Float = 0,
        Color = 1,
        Vector = 2,
        Texture = 3,
    }

    /// <summary>
    /// One property that differs from the original material. Only the field
    /// matching <see cref="kind"/> is meaningful.
    /// </summary>
    [Serializable]
    public class MaterialPropertyValue
    {
        public string name = "";
        public PropertyKind kind = PropertyKind.Float;
        public float floatValue;
        public Color colorValue = Color.white;
        public Vector4 vectorValue;
        public Texture texture;
        public Vector4 textureScaleOffset = new Vector4(1, 1, 0, 0);
    }

    /// <summary>Where an overlay is composited.</summary>
    public enum OverlayTarget
    {
        BaseColor = 0,
        Emission = 1,
    }

    /// <summary>lilBlendColor's modes, used by the compositor.</summary>
    public enum OverlayBlend
    {
        Normal = 0,
        Add = 1,
        Screen = 2,
        Multiply = 3,
    }

    /// <summary>
    /// One image merged into a texture of the material, the way you would merge
    /// a layer onto the base in an image editor.
    ///
    /// Everything here is relative: every default means "change nothing", so
    /// opening the advanced settings and touching something can never silently
    /// discard what the material already had.
    /// </summary>
    [Serializable]
    public class OverlayLayer
    {
        public string id = "";
        public bool enabled = true;
        public string displayName = "";

        public Texture texture;
        public OverlayTarget target = OverlayTarget.BaseColor;
        [Range(0f, 1f)] public float opacity = 1f;

        // ---- advanced; the defaults are all neutral ----
        public OverlayBlend blend = OverlayBlend.Normal;
        // Limits the layer to the white parts of this image. Null = everywhere.
        public Texture mask;
        public Color tint = Color.white;
        public Vector2 scale = Vector2.one;
        public Vector2 offset = Vector2.zero;
        // Empty resolves from `target` against the material's shader.
        public string property = "";

        public bool IsActive => enabled && texture != null && opacity > 0.001f;
    }

    /// <summary>
    /// The difference between an edited material and the one it was edited
    /// from. Everything not listed here keeps the original's value, so editing
    /// the original later carries through.
    /// </summary>
    [Serializable]
    public class MaterialEdit
    {
        // null keeps the original shader.
        public Shader shader;
        public List<MaterialPropertyValue> properties = new List<MaterialPropertyValue>();
        public bool overrideRenderQueue;
        public int renderQueue = -1;
        public List<string> enableKeywords = new List<string>();
        public List<string> disableKeywords = new List<string>();

        // Applied after the property differences, on top of whatever the
        // material's textures are by then.
        public List<OverlayLayer> overlays = new List<OverlayLayer>();

        public bool IsEmpty =>
            shader == null && properties.Count == 0 && !overrideRenderQueue &&
            enableKeywords.Count == 0 && disableKeywords.Count == 0 && overlays.Count == 0;

        public bool HasOverlays
        {
            get
            {
                foreach (var o in overlays) if (o != null && o.IsActive) return true;
                return false;
            }
        }

        public int Count =>
            properties.Count + (overrideRenderQueue ? 1 : 0) + (shader != null ? 1 : 0) +
            enableKeywords.Count + disableKeywords.Count + overlays.Count;

        public MaterialPropertyValue Find(string propertyName)
        {
            foreach (var p in properties)
                if (p != null && p.name == propertyName) return p;
            return null;
        }
    }

    /// <summary>
    /// One version of a material slot. Until menus exist a target has exactly
    /// one variant, which is always applied.
    /// </summary>
    [Serializable]
    public class MaterialVariant
    {
        public string id = "";
        public string displayName = "";
        public Texture2D icon;
        public int value = 1;
        public MaterialEdit edit = new MaterialEdit();
    }

    /// <summary>One material slot on one renderer, and the versions of it.</summary>
    [Serializable]
    public class MaterialTarget
    {
        public string id = "";
        public Renderer renderer;
        public int slot;

        // The material that was in the slot when this target was added. Kept so
        // the editor can diff against it and warn when the slot changes.
        public Material source;

        public string displayName = "";
        public Texture2D icon;

        public List<MaterialVariant> variants = new List<MaterialVariant>();
        public string defaultVariant = "";
        public int nextValue = 1;

        public MaterialVariant First => variants.Count > 0 ? variants[0] : null;
    }

    [AddComponentMenu("TsiYuki/Yuki Material")]
    [DisallowMultipleComponent]
    public class YukiMaterial : MonoBehaviour, IEditorOnly
    {
        public string id = "";

        // Menu label; empty uses the object name. Unused until menus exist.
        public string displayName = "";
        public Texture2D icon;

        // Reserved: when true the variants become an expression menu instead of
        // being applied permanently.
        public bool asMenu = false;
        public bool saved = true;
        public string parameterName = "";

        // Where this menu is installed; null is the avatar's root menu. Takes a
        // VRCExpressionsMenu asset, an object carrying a Modular Avatar menu
        // item, or another TsiYuki component that makes a menu. Held loosely so
        // the runtime assembly needs none of those types.
        public UnityEngine.Object menuParent;

        public List<MaterialTarget> targets = new List<MaterialTarget>();

        public MaterialTarget FindTarget(string targetId)
        {
            foreach (var t in targets)
                if (t != null && t.id == targetId) return t;
            return null;
        }

        /// <summary>
        /// Gives the component, every target and every variant a stable id, and
        /// makes sure each target has at least one variant. Returns true when
        /// anything changed.
        /// </summary>
        public bool EnsureIds()
        {
            bool changed = false;
            if (string.IsNullOrEmpty(id)) { id = NewId(); changed = true; }

            var used = new HashSet<string>();
            foreach (var target in targets)
            {
                if (target == null) continue;
                if (string.IsNullOrEmpty(target.id) || !used.Add(target.id))
                {
                    target.id = Unique(used);
                    changed = true;
                }
                if (target.variants.Count == 0)
                {
                    target.variants.Add(new MaterialVariant { id = NewId(), value = target.nextValue++ });
                    changed = true;
                }

                var variantIds = new HashSet<string>();
                foreach (var variant in target.variants)
                {
                    if (variant == null) continue;
                    if (string.IsNullOrEmpty(variant.id) || !variantIds.Add(variant.id))
                    {
                        variant.id = Unique(variantIds);
                        changed = true;
                    }
                    if (variant.edit == null) { variant.edit = new MaterialEdit(); changed = true; }
                    var overlayIds = new HashSet<string>();
                    foreach (var overlay in variant.edit.overlays)
                    {
                        if (overlay == null) continue;
                        if (string.IsNullOrEmpty(overlay.id) || !overlayIds.Add(overlay.id))
                        {
                            overlay.id = Unique(overlayIds);
                            changed = true;
                        }
                    }
                    if (variant.value <= 0) { variant.value = target.nextValue++; changed = true; }
                }
                if (target.nextValue <= 0) { target.nextValue = 1; changed = true; }
            }
            return changed;
        }

        public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 6);

        static string Unique(HashSet<string> used)
        {
            string candidate;
            do candidate = NewId(); while (!used.Add(candidate));
            return candidate;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (EnsureIds()) UnityEditor.EditorUtility.SetDirty(this);
        }

        void Reset()
        {
            id = NewId();
        }
#endif
    }
}
