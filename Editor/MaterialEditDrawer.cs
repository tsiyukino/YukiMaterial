using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TsiYuki.Core.Editor;
using TsiYuki.Core.Textures.Editor;
using UnityEditor;
using UnityEngine;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// Editing one look: the shader's own inspector drawn on a throwaway copy,
    /// the images merged into its textures, and the list of what came out
    /// different.
    ///
    /// It lives here rather than in a custom inspector because the panel is
    /// where material editing happens now. Nothing about it assumes an
    /// inspector, so it draws the same wherever it is put.
    /// </summary>
    internal sealed class MaterialEditDrawer : IDisposable
    {
        static YukiLocalizer L => MaterialText.L;

        static readonly PropertyInfo FirstInspected =
            typeof(UnityEditor.Editor).GetProperty("firstInspectedEditor", BindingFlags.Instance | BindingFlags.NonPublic);

        YukiMaterial _config;
        MaterialTarget _target;
        MaterialVariant _variant;

        // _work is what the shader inspector edits, _base is what sits in the
        // slot, and _reference is _base already switched to the look's shader,
        // so diffing costs no allocation per repaint.
        UnityEngine.Material _base;
        UnityEngine.Material _reference;
        UnityEngine.Material _work;
        MaterialEditor _panel;
        string _lastJson = "";

        TextureBaker _previewBaker;
        int _pickerId;
        bool _shaderOpen = true;
        bool _changesOpen;
        readonly HashSet<string> _openAdvanced = new HashSet<string>();

        TextureBaker PreviewBaker => _previewBaker ??= new TextureBaker(compress: false, maxSize: 256);

        public MaterialVariant Variant => _variant;

        public void Bind(YukiMaterial config, MaterialTarget target, MaterialVariant variant)
        {
            if (_config == config && _target == target && _variant == variant && _work != null) return;
            Release();
            _config = config;
            _target = target;
            _variant = variant;
            if (config == null || target == null || variant == null || target.renderer == null) return;

            var slots = target.renderer.sharedMaterials;
            if (target.slot < 0 || target.slot >= slots.Length || slots[target.slot] == null) return;

            _base = slots[target.slot];
            var edit = variant.edit ?? new MaterialEdit();

            _work = MaterialDiff.Build(_base, edit, _base.name);
            _work.hideFlags = HideFlags.HideAndDontSave;
            _reference = MaterialDiff.Reference(_base, edit.shader);

            _panel = (MaterialEditor)UnityEditor.Editor.CreateEditor(_work);
            // MaterialEditor draws nothing unless it believes it is the first
            // editor in an inspector; the setter is internal.
            if (FirstInspected != null && FirstInspected.CanWrite) FirstInspected.SetValue(_panel, true);

            _lastJson = JsonUtility.ToJson(edit);
        }

        public void Release()
        {
            if (_panel != null) UnityEngine.Object.DestroyImmediate(_panel);
            if (_work != null) UnityEngine.Object.DestroyImmediate(_work);
            if (_reference != null) UnityEngine.Object.DestroyImmediate(_reference);
            if (_previewBaker != null) { _previewBaker.Dispose(); _previewBaker = null; }
            _panel = null;
            _work = null;
            _reference = null;
            _base = null;
            _config = null;
            _target = null;
            _variant = null;
            _lastJson = "";
        }

        public void Dispose() => Release();

        /// <summary>Reads the shader inspector's material back into the look.</summary>
        void Capture()
        {
            if (_variant == null || _work == null || _base == null) return;

            // A shader swap in the panel invalidates the cached reference.
            if (_reference != null && _reference.shader != _work.shader)
            {
                UnityEngine.Object.DestroyImmediate(_reference);
                _reference = MaterialDiff.Reference(_base, _work.shader != _base.shader ? _work.shader : null);
            }

            var edit = MaterialDiff.Extract(_work, _base, _reference);
            // Overlays are authored here, not discovered by diffing the shader
            // panel, so carry them across or every repaint drops them.
            edit.overlays = _variant.edit.overlays;
            var json = JsonUtility.ToJson(edit);
            if (json == _lastJson) return;

            UndoEdit.Begin(_config, "Edit material");
            _variant.edit = edit;
            UndoEdit.End(_config);
            _lastJson = json;
        }

        /// <summary>The overlay list is not part of what Capture() diffs, so the
        /// snapshot it compares against is refreshed by hand after an edit
        /// here.</summary>
        void OnOverlayChanged()
        {
            if (_variant != null) _lastJson = JsonUtility.ToJson(_variant.edit);
        }

        // ---------------------------------------------------------------- draw

        public void Draw()
        {
            if (_variant == null || _work == null || _panel == null)
            {
                EditorGUILayout.HelpBox(L["ui.no_material"], MessageType.Info);
                return;
            }

            // Editing comes first. It is what this pane is for, and burying it
            // under a list of what has already been changed is how a panel ends
            // up looking as though it has no way to change anything.
            DrawOverlays();

            EditorGUILayout.Space(MaterialWindow.Block * 2);
            _shaderOpen = EditorGUILayout.Foldout(_shaderOpen, L["ui.edit_material"], true, EditorStyles.foldoutHeader);
            if (_shaderOpen)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(L["ui.edit_material.help"], YukiGUI.WrapMini);
                EditorGUILayout.Space(MaterialWindow.Block);
                try
                {
                    _panel.OnInspectorGUI();
                }
                catch (Exception e)
                {
                    EditorGUILayout.HelpBox(e.Message, MessageType.Error);
                }
            }

            // The difference is the record of what was done, so it reads after
            // the doing — and folded, because it can run to dozens of rows.
            EditorGUILayout.Space(MaterialWindow.Block * 2);
            DrawChanges();

            if (Event.current.type == EventType.Repaint) Capture();
        }

        void DrawChanges()
        {
            var edit = _variant.edit;
            if (edit == null || edit.IsEmpty)
            {
                EditorGUILayout.LabelField(L["ui.no_changes"], YukiGUI.WrapMini);
                return;
            }

            _changesOpen = EditorGUILayout.Foldout(_changesOpen, L.Tr("ui.changed_list_n", edit.Count), true, EditorStyles.foldoutHeader);
            if (!_changesOpen) return;

            using (new EditorGUILayout.VerticalScope(MaterialWindow.Styles.Card))
            {
                EditorGUILayout.LabelField(L["ui.changed_list.help"], YukiGUI.WrapMini);
                foreach (var property in edit.properties.ToList())
                {
                    // A fixed row height keeps the names lined up with their
                    // values; a texture row is taller than a number row.
                    using (new EditorGUILayout.HorizontalScope(GUILayout.Height(18)))
                    {
                        GUILayout.Label(new GUIContent(property.name, property.name), GUILayout.Width(180));
                        DrawValue(property);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(new GUIContent(L["ui.reset"], L["ui.reset_property"]), EditorStyles.miniButton, GUILayout.Width(60)))
                        {
                            MaterialActions.ResetProperty(_config, _variant, property.name);
                            Release();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                foreach (var line in MaterialDiff.Describe(edit).Skip(edit.properties.Count))
                    GUILayout.Label(line, EditorStyles.miniLabel);

                if (GUILayout.Button(L["ui.reset_all"], GUILayout.Width(140)) &&
                    EditorUtility.DisplayDialog(L["ui.title"], L["ui.reset_all.confirm"], L["ui.ok"], L["ui.cancel"]))
                {
                    MaterialActions.ResetEdit(_config, _variant);
                    Release();
                    GUIUtility.ExitGUI();
                }
            }
        }

        /// <summary>
        /// What the material had, and what this look puts there instead. The
        /// name of a property says nothing about what was done to it, and two
        /// textures can share a name and be different files — which is exactly
        /// the case a list of names hides.
        /// </summary>
        void DrawValue(MaterialPropertyValue property)
        {
            bool known = _base != null && _base.HasProperty(property.name);
            switch (property.kind)
            {
                case PropertyKind.Texture:
                {
                    var from = known ? _base.GetTexture(property.name) : null;
                    // Same name, different file: show enough of the path to tell
                    // them apart, or the row reads as a change that changes nothing.
                    bool ambiguous = from != null && property.texture != null &&
                                     from.name == property.texture.name && from != property.texture;
                    Chip(from, ambiguous);
                    Arrow();
                    Chip(property.texture, ambiguous);

                    var scale = known ? _base.GetTextureScale(property.name) : Vector2.one;
                    var offset = known ? _base.GetTextureOffset(property.name) : Vector2.zero;
                    var wanted = property.textureScaleOffset;
                    if (!Mathf.Approximately(scale.x, wanted.x) || !Mathf.Approximately(scale.y, wanted.y) ||
                        !Mathf.Approximately(offset.x, wanted.z) || !Mathf.Approximately(offset.y, wanted.w))
                        GUILayout.Label(L.Tr("ui.value.tiling", Num(wanted.x) + "×" + Num(wanted.y),
                                             Num(wanted.z) + "," + Num(wanted.w)), EditorStyles.miniLabel);
                    break;
                }
                case PropertyKind.Color:
                {
                    var from = known ? _base.GetColor(property.name) : Color.clear;
                    Swatch(from);
                    Arrow();
                    Swatch(property.colorValue);
                    GUILayout.Label(new GUIContent(Describe(property.colorValue), Exact(from) + "  →  " + Exact(property.colorValue)),
                                    EditorStyles.miniLabel, GUILayout.Width(150));
                    break;
                }
                case PropertyKind.Vector:
                {
                    var from = known ? (Vector4)_base.GetVector(property.name) : Vector4.zero;
                    GUILayout.Label(Describe(from) + "  →  " + Describe(property.vectorValue), EditorStyles.miniLabel);
                    break;
                }
                default:
                {
                    float from = known ? _base.GetFloat(property.name) : 0f;
                    GUILayout.Label(Num(from) + "  →  " + Num(property.floatValue), EditorStyles.miniLabel, GUILayout.Width(120));
                    break;
                }
            }
        }

        static void Arrow() => GUILayout.Label("→", EditorStyles.miniLabel, GUILayout.Width(16));

        /// <summary>A texture as a thumbnail and a name, clickable to find the
        /// file it came from.</summary>
        static void Chip(Texture texture, bool withFolder)
        {
            var rect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16), GUILayout.Height(16));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);

            var path = texture != null ? AssetDatabase.GetAssetPath(texture) : "";
            var label = texture == null ? "—"
                      : withFolder ? Folder(path) + "/" + texture.name
                      : texture.name;
            if (GUILayout.Button(new GUIContent(label, string.IsNullOrEmpty(path) ? label : path),
                                 EditorStyles.miniLabel, GUILayout.Width(150)) && texture != null)
                EditorGUIUtility.PingObject(texture);
        }

        static string Folder(string path)
        {
            if (string.IsNullOrEmpty(path)) return "?";
            var directory = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) return "?";
            return System.IO.Path.GetFileName(directory.Replace('\\', '/'));
        }

        static void Swatch(Color color)
        {
            var rect = GUILayoutUtility.GetRect(28, 16, GUILayout.Width(28), GUILayout.Height(16));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
            var inner = new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2);
            // An HDR colour would draw as white and hide its own intensity.
            var shown = new Color(Mathf.Clamp01(color.r), Mathf.Clamp01(color.g), Mathf.Clamp01(color.b), 1f);
            EditorGUI.DrawRect(inner, shown);
            if (color.a < 0.999f)
            {
                var alpha = new Rect(inner.x, inner.yMax - 3, inner.width * Mathf.Clamp01(color.a), 3);
                EditorGUI.DrawRect(new Rect(inner.x, inner.yMax - 3, inner.width, 3), new Color(0f, 0f, 0f, 0.6f));
                EditorGUI.DrawRect(alpha, Color.white);
            }
        }

        static string Num(float value) => value.ToString("0.###");

        static string Describe(Color color)
        {
            var text = "#" + ColorUtility.ToHtmlStringRGB(color);
            float intensity = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            if (intensity > 1.001f) text += " ×" + Num(intensity);
            if (color.a < 0.999f) text += "  α " + Num(color.a);
            return text;
        }

        static string Exact(Color c) => Num(c.r) + ", " + Num(c.g) + ", " + Num(c.b) + ", " + Num(c.a);

        static string Describe(Vector4 v) => "(" + Num(v.x) + ", " + Num(v.y) + ", " + Num(v.z) + ", " + Num(v.w) + ")";

        // ------------------------------------------------------------ overlays

        void DrawOverlays()
        {
            var overlays = _variant.edit.overlays;

            MaterialWindow.Heading(L["ui.overlays"], L["ui.overlays.help"]);

            DrawResult(_variant.edit);
            DrawDropZone();
            EditorGUILayout.Space(MaterialWindow.Block);

            if (overlays.Count == 0) return;
            if (overlays.Count > 1) { EditorGUILayout.LabelField(L["ui.overlay.order"], YukiGUI.WrapMini); EditorGUILayout.Space(MaterialWindow.Gap); }

            OverlayLayer remove = null, moving = null;
            int move = 0;

            foreach (var layer in overlays.ToList())
            {
                if (layer == null) continue;
                using (new EditorGUILayout.VerticalScope(MaterialWindow.Styles.Card))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        Thumbnail(layer.texture, 52);

                        using (new EditorGUILayout.VerticalScope())
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                EditorGUI.BeginChangeCheck();
                                var enabled = EditorGUILayout.ToggleLeft(
                                    layer.texture != null ? layer.texture.name : L["ui.overlay.empty"],
                                    layer.enabled, EditorStyles.boldLabel);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    UndoEdit.Begin(_config, "Toggle overlay");
                                    layer.enabled = enabled;
                                    UndoEdit.End(_config);
                                    OnOverlayChanged();
                                }
                                if (overlays.Count > 1)
                                {
                                    using (new EditorGUI.DisabledScope(overlays.IndexOf(layer) == 0))
                                        if (GUILayout.Button(new GUIContent("↑", L["ui.overlay.move_up"]), EditorStyles.miniButton, GUILayout.Width(22))) { moving = layer; move = -1; }
                                    using (new EditorGUI.DisabledScope(overlays.IndexOf(layer) == overlays.Count - 1))
                                        if (GUILayout.Button(new GUIContent("↓", L["ui.overlay.move_down"]), EditorStyles.miniButton, GUILayout.Width(22))) { moving = layer; move = 1; }
                                }
                                if (GUILayout.Button("×", GUILayout.Width(22))) remove = layer;
                            }

                            EditorGUI.BeginChangeCheck();
                            var targetKind = (OverlayTarget)EditorGUILayout.Popup(L["ui.overlay.target"], (int)layer.target,
                                Enum.GetNames(typeof(OverlayTarget)).Select(n => L["ui.overlay.target." + n]).ToArray());
                            var opacity = EditorGUILayout.Slider(L["ui.overlay.opacity"], layer.opacity, 0f, 1f);
                            if (EditorGUI.EndChangeCheck())
                            {
                                UndoEdit.Begin(_config, "Edit overlay");
                                layer.target = targetKind;
                                layer.opacity = opacity;
                                UndoEdit.End(_config);
                                OnOverlayChanged();
                            }
                        }
                    }

                    bool open = _openAdvanced.Contains(layer.id);
                    var now = EditorGUILayout.Foldout(open, L["ui.overlay.advanced"], true);
                    if (now != open) { if (now) _openAdvanced.Add(layer.id); else _openAdvanced.Remove(layer.id); }
                    if (!now) continue;

                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        var texture = (Texture)EditorGUILayout.ObjectField(L["ui.overlay.image"], layer.texture, typeof(Texture), false);
                        var blend = (OverlayBlend)EditorGUILayout.Popup(L["ui.overlay.blend"], (int)layer.blend,
                            Enum.GetNames(typeof(OverlayBlend)).Select(n => L["ui.overlay.blend." + n]).ToArray());
                        var mask = (Texture)EditorGUILayout.ObjectField(new GUIContent(L["ui.overlay.mask"], L["ui.overlay.mask.tip"]), layer.mask, typeof(Texture), false);
                        var tint = EditorGUILayout.ColorField(L["ui.overlay.tint"], layer.tint);
                        var scale = EditorGUILayout.Vector2Field(L["ui.overlay.scale"], layer.scale);
                        var offset = EditorGUILayout.Vector2Field(L["ui.overlay.offset"], layer.offset);
                        var property = EditorGUILayout.TextField(new GUIContent(L["ui.overlay.property"], L["ui.overlay.property.tip"]), layer.property);
                        if (EditorGUI.EndChangeCheck())
                        {
                            UndoEdit.Begin(_config, "Edit overlay");
                            layer.texture = texture;
                            layer.blend = blend;
                            layer.mask = mask;
                            layer.tint = tint;
                            layer.scale = scale;
                            layer.offset = offset;
                            layer.property = property;
                            UndoEdit.End(_config);
                            OnOverlayChanged();
                        }
                    }
                }
            }

            if (moving != null) { MaterialActions.MoveOverlay(_config, _variant, moving, move); OnOverlayChanged(); GUIUtility.ExitGUI(); }
            if (remove != null) { _openAdvanced.Remove(remove.id); MaterialActions.RemoveOverlay(_config, _variant, remove); OnOverlayChanged(); GUIUtility.ExitGUI(); }
        }

        /// <summary>What the layers add up to, so the result is visible here
        /// rather than only in the Scene view.</summary>
        void DrawResult(MaterialEdit edit)
        {
            if (_work == null || !edit.HasOverlays) return;

            var targets = new List<OverlayTarget>();
            foreach (OverlayTarget t in Enum.GetValues(typeof(OverlayTarget)))
                if (OverlayBaker.Targets(_work, edit, t)) targets.Add(t);
            if (targets.Count == 0) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var t in targets)
                {
                    Texture composite = null;
                    try { composite = OverlayBaker.Composite(_work, edit, PreviewBaker, t); }
                    catch { /* a broken layer must not take the panel down */ }
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(96)))
                    {
                        Thumbnail(composite, 88);
                        GUILayout.Label(L["ui.overlay.target." + t], EditorStyles.miniLabel);
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label(L["ui.overlay.result"], YukiGUI.WrapMini);
            }
            EditorGUILayout.Space(2);
        }

        static void Thumbnail(Texture texture, float size)
        {
            var rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
        }

        /// <summary>Drop images here; one layer per image, settings guessed.</summary>
        void DrawDropZone()
        {
            var rect = GUILayoutUtility.GetRect(0, 54, GUILayout.ExpandWidth(true));
            var hover = rect.Contains(Event.current.mousePosition);
            var dragging = DragAndDrop.objectReferences.Any(o => o is Texture);

            EditorGUI.DrawRect(rect, hover && dragging ? new Color(0.3f, 0.5f, 0.8f, 0.35f) : new Color(0f, 0f, 0f, 0.15f));
            var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true };
            GUI.Label(rect, L["ui.overlay.drop"], style);

            var e = Event.current;
            if (hover)
            {
                if (e.type == EventType.DragUpdated && dragging)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    e.Use();
                }
                else if (e.type == EventType.DragPerform && dragging)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var o in DragAndDrop.objectReferences.OfType<Texture>())
                        AddLayer(o);
                    e.Use();
                    GUIUtility.ExitGUI();
                }
                else if (e.type == EventType.MouseDown && e.button == 0)
                {
                    _pickerId = GUIUtility.GetControlID(FocusType.Passive);
                    EditorGUIUtility.ShowObjectPicker<Texture>(null, false, "", _pickerId);
                    e.Use();
                }
            }

            if (e.type == EventType.ExecuteCommand && e.commandName == "ObjectSelectorClosed" &&
                EditorGUIUtility.GetObjectPickerControlID() == _pickerId && _pickerId != 0)
            {
                var picked = EditorGUIUtility.GetObjectPickerObject() as Texture;
                _pickerId = 0;
                if (picked != null) { AddLayer(picked); e.Use(); GUIUtility.ExitGUI(); }
            }
        }

        void AddLayer(Texture texture)
        {
            var layer = MaterialActions.AddOverlay(_config, _variant);
            if (layer == null) return;
            OverlayTarget guessedTarget;
            OverlayBlend guessedBlend;
            OverlayBaker.Guess(texture, out guessedTarget, out guessedBlend);
            UndoEdit.Begin(_config, "Add overlay");
            layer.texture = texture;
            layer.target = guessedTarget;
            layer.blend = guessedBlend;
            UndoEdit.End(_config);
            OnOverlayChanged();
        }
    }
}
