using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// Turns an edited material into the sparse difference from the material it
    /// was edited from, and back again.
    ///
    /// The difference — not a copy — is what the component stores, so editing
    /// the original material later carries through to every edit made from it.
    /// </summary>
    public static class MaterialDiff
    {
        /// <summary>
        /// Bookkeeping a shader inspector writes on its own. Thry (Poiyomi) marks
        /// every material it has opened, and keeps one "&lt;property&gt;Animated"
        /// flag per property for its shader locking, which means nothing here.
        /// </summary>
        public static bool IsBookkeeping(string propertyName) =>
            propertyName == "shader_is_using_thry_editor" ||
            propertyName.EndsWith("Animated", StringComparison.Ordinal) ||
            propertyName.EndsWith("AnimatedAlt", StringComparison.Ordinal);

        /// <summary>
        /// The material an edit starts from: a copy of <paramref name="source"/>,
        /// already switched to <paramref name="shader"/> when the edit replaces it.
        /// Both <see cref="Extract"/> and <see cref="Apply"/> measure against this,
        /// so a shader swap records only what the user changed afterwards rather
        /// than every value Unity carried over.
        /// </summary>
        public static Material Reference(Material source, Shader shader)
        {
            var reference = new UnityEngine.Material(source) { hideFlags = HideFlags.HideAndDontSave };
            if (shader != null && shader != source.shader) reference.shader = shader;
            return reference;
        }

        public static PropertyKind KindOf(ShaderUtil.ShaderPropertyType type)
        {
            switch (type)
            {
                case ShaderUtil.ShaderPropertyType.TexEnv: return PropertyKind.Texture;
                case ShaderUtil.ShaderPropertyType.Color: return PropertyKind.Color;
                case ShaderUtil.ShaderPropertyType.Vector: return PropertyKind.Vector;
                default: return PropertyKind.Float; // Float, Range and Int
            }
        }

        static bool Same(UnityEngine.Material a, UnityEngine.Material b, string name, PropertyKind kind)
        {
            switch (kind)
            {
                case PropertyKind.Texture:
                    return a.GetTexture(name) == b.GetTexture(name)
                        && a.GetTextureScale(name) == b.GetTextureScale(name)
                        && a.GetTextureOffset(name) == b.GetTextureOffset(name);
                case PropertyKind.Color: return a.GetColor(name) == b.GetColor(name);
                case PropertyKind.Vector: return a.GetVector(name) == b.GetVector(name);
                default:
                    var x = a.GetFloat(name);
                    var y = b.GetFloat(name);
                    return x.Equals(y) || Mathf.Approximately(x, y);
            }
        }

        static MaterialPropertyValue Capture(UnityEngine.Material material, string name, PropertyKind kind)
        {
            var value = new MaterialPropertyValue { name = name, kind = kind };
            switch (kind)
            {
                case PropertyKind.Texture:
                    value.texture = material.GetTexture(name);
                    var scale = material.GetTextureScale(name);
                    var offset = material.GetTextureOffset(name);
                    value.textureScaleOffset = new Vector4(scale.x, scale.y, offset.x, offset.y);
                    break;
                case PropertyKind.Color: value.colorValue = material.GetColor(name); break;
                case PropertyKind.Vector: value.vectorValue = material.GetVector(name); break;
                default: value.floatValue = material.GetFloat(name); break;
            }
            return value;
        }

        /// <summary>
        /// Everything <paramref name="edited"/> has that <paramref name="source"/>
        /// does not. <paramref name="edited"/> is normally a working copy the user
        /// has been editing through the shader's own inspector.
        /// </summary>
        public static MaterialEdit Extract(UnityEngine.Material edited, UnityEngine.Material source) =>
            Extract(edited, source, null);

        /// <summary>
        /// As <see cref="Extract(UnityEngine.Material,UnityEngine.Material)"/>, but reusing a reference
        /// built earlier by <see cref="Reference"/>. The editor keeps one alive so
        /// that diffing on every repaint costs no allocation; pass null to have one
        /// built and thrown away here.
        /// </summary>
        public static MaterialEdit Extract(UnityEngine.Material edited, UnityEngine.Material source, UnityEngine.Material cached)
        {
            var edit = new MaterialEdit();
            if (edited == null || source == null) return edit;

            if (edited.shader != source.shader) edit.shader = edited.shader;
            var reference = cached != null && cached.shader == edited.shader ? cached : Reference(source, edit.shader);
            bool owned = !ReferenceEquals(reference, cached);
            try
            {
                var shader = edited.shader;
                int count = ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < count; i++)
                {
                    var name = ShaderUtil.GetPropertyName(shader, i);
                    if (IsBookkeeping(name)) continue;
                    var kind = KindOf(ShaderUtil.GetPropertyType(shader, i));
                    if (!reference.HasProperty(name) || !Same(edited, reference, name, kind))
                        edit.properties.Add(Capture(edited, name, kind));
                }

                if (edited.renderQueue != reference.renderQueue)
                {
                    edit.overrideRenderQueue = true;
                    edit.renderQueue = edited.renderQueue;
                }

                var before = new HashSet<string>(reference.shaderKeywords);
                var after = new HashSet<string>(edited.shaderKeywords);
                edit.enableKeywords.AddRange(after.Where(k => !before.Contains(k)).OrderBy(k => k));
                edit.disableKeywords.AddRange(before.Where(k => !after.Contains(k)).OrderBy(k => k));
            }
            finally
            {
                if (owned) UnityEngine.Object.DestroyImmediate(reference);
            }
            return edit;
        }

        /// <summary>
        /// Writes <paramref name="edit"/> onto <paramref name="material"/> in place.
        /// <paramref name="onMissing"/> is called for each property the shader no
        /// longer has, which happens when the material's shader was updated after
        /// the edit was made.
        /// </summary>
        public static void Apply(UnityEngine.Material material, MaterialEdit edit, Action<string> onMissing = null)
        {
            if (material == null || edit == null) return;

            if (edit.shader != null && edit.shader != material.shader) material.shader = edit.shader;

            foreach (var property in edit.properties)
            {
                if (property == null || string.IsNullOrEmpty(property.name)) continue;
                if (!material.HasProperty(property.name)) { onMissing?.Invoke(property.name); continue; }
                switch (property.kind)
                {
                    case PropertyKind.Texture:
                        material.SetTexture(property.name, property.texture);
                        material.SetTextureScale(property.name, new Vector2(property.textureScaleOffset.x, property.textureScaleOffset.y));
                        material.SetTextureOffset(property.name, new Vector2(property.textureScaleOffset.z, property.textureScaleOffset.w));
                        break;
                    case PropertyKind.Color: material.SetColor(property.name, property.colorValue); break;
                    case PropertyKind.Vector: material.SetVector(property.name, property.vectorValue); break;
                    default: material.SetFloat(property.name, property.floatValue); break;
                }
            }

            foreach (var keyword in edit.disableKeywords) material.DisableKeyword(keyword);
            foreach (var keyword in edit.enableKeywords) material.EnableKeyword(keyword);

            if (edit.overrideRenderQueue) material.renderQueue = edit.renderQueue;
        }

        /// <summary>A fresh material: <paramref name="source"/> with the edit applied.</summary>
        public static UnityEngine.Material Build(UnityEngine.Material source, MaterialEdit edit, string name, Action<string> onMissing = null)
        {
            if (source == null) return null;
            var result = new UnityEngine.Material(source) { name = string.IsNullOrEmpty(name) ? source.name : name };
            Apply(result, edit, onMissing);
            return result;
        }

        /// <summary>
        /// True when the material's numeric and toggle properties were baked into
        /// the shader by Poiyomi's optimizer, so changing them does nothing.
        /// Texture references are still live.
        /// </summary>
        public static bool IsLockedPoiyomi(UnityEngine.Material material) =>
            material != null && material.shader != null &&
            material.shader.name.StartsWith("Hidden/", StringComparison.Ordinal) &&
            !string.IsNullOrEmpty(material.GetTag("OriginalShader", false, ""));

        /// <summary>Human-readable summary of what an edit touches, longest first.</summary>
        public static IEnumerable<string> Describe(MaterialEdit edit)
        {
            if (edit == null) yield break;
            if (edit.shader != null) yield return MaterialText.L.Tr("ui.shader_changed", edit.shader.name);
            foreach (var property in edit.properties) yield return property.name;
            if (edit.overrideRenderQueue) yield return MaterialText.L["ui.render_queue"] + " " + edit.renderQueue;
            if (edit.enableKeywords.Count > 0 || edit.disableKeywords.Count > 0)
                yield return MaterialText.L["ui.keywords_changed"];
        }
    }
}
