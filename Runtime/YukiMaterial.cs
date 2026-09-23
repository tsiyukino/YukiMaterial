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

        public bool IsActive { get { return enabled && texture != null && opacity > 0.001f; } }
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

        public bool IsEmpty
        {
            get
            {
                return shader == null && properties.Count == 0 && !overrideRenderQueue &&
                       enableKeywords.Count == 0 && disableKeywords.Count == 0 && overlays.Count == 0;
            }
        }

        public bool HasOverlays
        {
            get
            {
                foreach (var o in overlays) if (o != null && o.IsActive) return true;
                return false;
            }
        }

        public int Count
        {
            get
            {
                return properties.Count + (overrideRenderQueue ? 1 : 0) + (shader != null ? 1 : 0) +
                       enableKeywords.Count + disableKeywords.Count + overlays.Count;
            }
        }

        public MaterialPropertyValue Find(string propertyName)
        {
            foreach (var p in properties)
                if (p != null && p.name == propertyName) return p;
            return null;
        }
    }

    /// <summary>
    /// One look a material slot can wear: a name and the difference from the
    /// material the slot already holds.
    ///
    /// A look whose difference is empty is that material itself. Every slot can
    /// always wear it without one being stored, which is why the original is
    /// spelled as the empty id rather than as an entry in the list: nothing can
    /// delete it, rename it, or edit it into something else.
    /// </summary>
    [Serializable]
    public class MaterialVariant
    {
        /// <summary>The id of the untouched material: no look at all.</summary>
        public const string Original = "";

        public string id = "";
        public string displayName = "";
        public Texture2D icon;
        public MaterialEdit edit = new MaterialEdit();

        public bool Changes { get { return edit != null && !edit.IsEmpty; } }
    }

    /// <summary>
    /// One material slot on one renderer, and the looks made for it.
    ///
    /// The slot knows nothing about menus. It is a catalogue: this slot can look
    /// like this, or like that. What picks between them — a menu somewhere else
    /// on the avatar, or nothing at all — is not its business.
    /// </summary>
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

        /// <summary>
        /// What the slot wears when nothing switches it: the empty id is the
        /// material it already holds. A slot no menu drives wears this on the
        /// built avatar, which is how a change is made permanent.
        /// </summary>
        public string baseVariant = MaterialVariant.Original;

        public MaterialVariant Find(string variantId)
        {
            if (string.IsNullOrEmpty(variantId)) return null;
            foreach (var variant in variants)
                if (variant != null && variant.id == variantId) return variant;
            return null;
        }

        /// <summary>Whether anything here would change the built avatar on its
        /// own, menus aside.</summary>
        public bool ChangesOnItsOwn
        {
            get
            {
                var variant = Find(baseVariant);
                return variant != null && variant.Changes;
            }
        }
    }

    /// <summary>
    /// What one object's material slots can look like.
    ///
    /// This component is a catalogue and nothing else: slots, and the looks made
    /// for them. Switching between looks in game is a menu's job, and a menu
    /// refers to these slots from wherever it lives — so the hair and the ears
    /// keep their own looks on their own objects and still change together.
    /// </summary>
    [AddComponentMenu("TsiYuki/Yuki Material")]
    [DisallowMultipleComponent]
    public class YukiMaterial : MonoBehaviour, IEditorOnly
    {
        public string id = "";

        public List<MaterialTarget> targets = new List<MaterialTarget>();

        public MaterialTarget FindTarget(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return null;
            foreach (var target in targets)
                if (target != null && target.id == targetId) return target;
            return null;
        }

        /// <summary>Gives the component, every slot and every look a stable id.
        /// Returns true when anything changed.</summary>
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
                }

                // A look that was deleted must not leave the slot pointing at it.
                if (!string.IsNullOrEmpty(target.baseVariant) && target.Find(target.baseVariant) == null)
                {
                    target.baseVariant = MaterialVariant.Original;
                    changed = true;
                }
            }
            return changed;
        }

        public static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 6); }

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
