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

        public bool IsEmpty =>
            shader == null && properties.Count == 0 && !overrideRenderQueue &&
            enableKeywords.Count == 0 && disableKeywords.Count == 0;

        public int Count =>
            properties.Count + (overrideRenderQueue ? 1 : 0) + (shader != null ? 1 : 0) +
            enableKeywords.Count + disableKeywords.Count;

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
