using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TsiYuki.Core.Editor;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// The component inspector. Each target is edited through the shader's own
    /// inspector, drawn on a throwaway working copy; what the user changes there
    /// is read back as a difference and stored on the component.
    /// </summary>
    [CustomEditor(typeof(YukiMaterial))]
    public class YukiMaterialEditor : UnityEditor.Editor
    {
        const string Version = "0.1.0";

        static YukiLocalizer L => MaterialText.L;

        YukiMaterial Config => (YukiMaterial)target;

        // Working state for the selected target. The panel edits _work; _base is
        // what sits in the slot and _reference is _base already switched to the
        // edit's shader, so diffing costs no allocation per repaint.
        [SerializeField] string selectedId = "";
        [SerializeField] string editingVariantId = "";
        MaterialTarget _bound;
        MaterialVariant _boundVariant;
        UnityEngine.Material _base;
        UnityEngine.Material _reference;
        UnityEngine.Material _work;
        MaterialEditor _panel;
        string _lastJson = "";

        // Composites for the inspector's own preview. Small, uncompressed, and
        // cached by the baker, so redrawing costs nothing after the first bake.
        TextureBaker _previewBaker;
        int _pickerId;
        bool _panelOpen;

        static readonly PropertyInfo FirstInspected =
            typeof(UnityEditor.Editor).GetProperty("firstInspectedEditor", BindingFlags.Instance | BindingFlags.NonPublic);

        void OnDisable() => Release();

        void Release()
        {
            if (_panel != null) DestroyImmediate(_panel);
            if (_work != null) DestroyImmediate(_work);
            if (_reference != null) DestroyImmediate(_reference);
            if (_previewBaker != null) { _previewBaker.Dispose(); _previewBaker = null; }
            _panel = null;
            _work = null;
            _reference = null;
            _base = null;
            _bound = null;
            _boundVariant = null;
            _lastJson = "";
        }

        // ------------------------------------------------------------ binding

        /// <summary>The version the inspector is editing.</summary>
        MaterialVariant Editing(MaterialTarget target)
        {
            if (target == null || target.variants.Count == 0) return null;
            return target.variants.FirstOrDefault(v => v != null && v.id == editingVariantId)
                   ?? target.variants.FirstOrDefault(v => v != null && v.id == target.defaultVariant)
                   ?? target.First;
        }

        void Bind(MaterialTarget target)
        {
            Release();
            if (target == null || target.renderer == null) return;
            var slots = target.renderer.sharedMaterials;
            if (target.slot < 0 || target.slot >= slots.Length || slots[target.slot] == null) return;

            _bound = target;
            _boundVariant = Editing(target);
            _base = slots[target.slot];
            var edit = _boundVariant != null ? _boundVariant.edit : new MaterialEdit();

            _work = MaterialDiff.Build(_base, edit, _base.name);
            _work.hideFlags = HideFlags.HideAndDontSave;
            _reference = MaterialDiff.Reference(_base, edit.shader);

            _panel = (MaterialEditor)CreateEditor(_work);
            // MaterialEditor draws nothing unless it believes it is the first
            // editor in an inspector; the setter is internal.
            if (FirstInspected != null && FirstInspected.CanWrite) FirstInspected.SetValue(_panel, true);

            _lastJson = JsonUtility.ToJson(edit);
        }

        /// <summary>Reads the panel's material back into the component.</summary>
        void Capture()
        {
            if (_bound == null || _work == null || _base == null) return;
            var variant = _boundVariant;
            if (variant == null) return;

            // A shader swap in the panel invalidates the cached reference.
            if (_reference != null && _reference.shader != _work.shader)
            {
                DestroyImmediate(_reference);
                _reference = MaterialDiff.Reference(_base, _work.shader != _base.shader ? _work.shader : null);
            }

            var edit = MaterialDiff.Extract(_work, _base, _reference);
            // Overlays are authored in this inspector, not discovered by diffing
            // the shader panel, so carry them across or every repaint drops them.
            edit.overlays = variant.edit.overlays;
            var json = JsonUtility.ToJson(edit);
            if (json == _lastJson) return;

            UndoEdit.Begin(Config, "Edit material");
            variant.edit = edit;
            UndoEdit.End(Config);
            _lastJson = json;
        }

        // --------------------------------------------------------------- draw

        public override void OnInspectorGUI()
        {
            var config = Config;
            if (config.EnsureIds()) EditorUtility.SetDirty(config);

            // Header already lays out the title, version and language popup.
            YukiGUI.Header(L["ui.title"], Version);

            if (config.GetComponentInParent<VRCAvatarDescriptor>() == null)
                EditorGUILayout.HelpBox(L["ui.not_in_avatar"], MessageType.Warning);

            DrawWarnings(config);

            EditorGUI.BeginChangeCheck();
            var asMenu = EditorGUILayout.Toggle(new GUIContent(L["ui.as_menu"], L["ui.as_menu.tip"]), config.asMenu);
            var menuName = config.asMenu ? EditorGUILayout.TextField(new GUIContent(L["ui.menu_name"], L["ui.menu_name.tip"]), config.displayName) : config.displayName;
            var saved = config.asMenu ? EditorGUILayout.Toggle(new GUIContent(L["ui.saved"], L["ui.saved.tip"]), config.saved) : config.saved;
            if (EditorGUI.EndChangeCheck())
            {
                UndoEdit.Begin(config, "Edit material component");
                config.asMenu = asMenu;
                config.displayName = menuName;
                config.saved = saved;
                UndoEdit.End(config);
                MaterialPreviewState.Clear();
                Release();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.LabelField(config.asMenu ? L["ui.as_menu.help"] : L["ui.permanent.help"], YukiGUI.WrapMini);
            EditorGUILayout.Space(4);

            YukiGUI.Section(L["ui.targets"]);
            EditorGUILayout.LabelField(L["ui.targets.help"], YukiGUI.WrapMini);
            EditorGUILayout.Space(2);
            DrawTargetList(config);

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(L["ui.fill_from_object"], L["ui.fill_from_object.tip"])))
                {
                    MaterialActions.FillFrom(config, config.gameObject, config.GetComponent<Renderer>() == null);
                    Release();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(L["ui.add_target"], GUILayout.Width(150)))
                {
                    var target = MaterialActions.Add(config, config.GetComponent<Renderer>(), 0);
                    selectedId = target.id;
                    Release();
                    GUIUtility.ExitGUI();
                }
            }

            var selected = config.FindTarget(selectedId);
            if (selected == null) { Release(); return; }

            EditorGUILayout.Space(8);
            DrawSelected(config, selected);
        }

        void DrawWarnings(YukiMaterial config)
        {
            var avatar = config.GetComponentInParent<VRCAvatarDescriptor>();
            if (avatar == null) return;
            var model = MaterialModel.Resolve(config, avatar.transform);
            foreach (var warning in model.Warnings)
                EditorGUILayout.HelpBox(warning.Message, MessageType.Warning);
        }

        void DrawTargetList(YukiMaterial config)
        {
            if (config.targets.Count == 0)
            {
                EditorGUILayout.HelpBox(L["ui.no_targets"], MessageType.Info);
                return;
            }

            MaterialTarget remove = null;
            foreach (var target in config.targets.ToList())
            {
                if (target == null) continue;
                bool isSelected = target.id == selectedId;
                using (new EditorGUILayout.HorizontalScope(isSelected ? EditorStyles.helpBox : GUIStyle.none))
                {
                    var edit = target.First != null ? target.First.edit : null;
                    int changes = edit != null ? edit.Count : 0;

                    if (GUILayout.Button(MaterialModel.NameOf(target), EditorStyles.label))
                    {
                        selectedId = isSelected ? "" : target.id;
                        Release();
                        GUIUtility.ExitGUI();
                    }
                    GUILayout.Label(target.renderer != null ? target.renderer.name : "—", EditorStyles.miniLabel, GUILayout.Width(110));
                    GUILayout.Label(L.Tr("ui.slot", target.slot), EditorStyles.miniLabel, GUILayout.Width(60));
                    YukiGUI.StatusLabel(changes > 0 ? YukiStatus.Approximate : YukiStatus.Ok,
                        changes > 0 ? L.Tr("ui.changes", changes) : L["ui.unchanged"], GUILayout.Width(90));
                    if (GUILayout.Button("×", GUILayout.Width(22))) remove = target;
                }
            }

            if (remove != null)
            {
                if (remove.id == selectedId) { selectedId = ""; Release(); }
                MaterialActions.Remove(config, remove);
                GUIUtility.ExitGUI();
            }
        }

        void DrawSelected(YukiMaterial config, MaterialTarget target)
        {
            if (_bound != target || _boundVariant != Editing(target)) Bind(target);
            if (_panel == null || _work == null)
            {
                EditorGUILayout.HelpBox(L["ui.no_targets"], MessageType.Info);
                return;
            }

            YukiGUI.Section(MaterialModel.NameOf(target));

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var name = EditorGUILayout.TextField(new GUIContent(L["ui.display_name"], L["ui.display_name.tip"]), target.displayName);
                if (EditorGUI.EndChangeCheck())
                {
                    UndoEdit.Begin(config, "Rename material slot");
                    target.displayName = name;
                    UndoEdit.End(config);
                }
                if (target.renderer != null && GUILayout.Button(L["ui.select_object"], GUILayout.Width(70)))
                    EditorGUIUtility.PingObject(Selection.activeObject = target.renderer.gameObject);
            }

            DrawVariants(config, target);

            DrawOverlays(config, target);

            EditorGUILayout.Space(6);
            var editing = Editing(target);
            var edit = editing != null ? editing.edit : null;
            if (edit != null && !edit.IsEmpty)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(L["ui.changed_list"], EditorStyles.miniBoldLabel);
                    foreach (var property in edit.properties.ToList())
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(property.name, EditorStyles.miniLabel);
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button(new GUIContent(L["ui.reset"], L["ui.reset_property"]), EditorStyles.miniButton, GUILayout.Width(60)))
                            {
                                MaterialActions.ResetProperty(config, editing, property.name);
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
                        MaterialActions.ResetEdit(config, editing);
                        Release();
                        GUIUtility.ExitGUI();
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox(L["ui.no_changes"], MessageType.Info);
            }

            EditorGUILayout.Space(6);

            // The shader's own inspector is hundreds of rows tall and is the
            // advanced path, so it stays folded away until it is wanted.
            _panelOpen = EditorGUILayout.Foldout(_panelOpen, L["ui.edit_material"], true, EditorStyles.foldoutHeader);
            if (_panelOpen)
            {
                EditorGUILayout.LabelField(L["ui.edit_material.help"], YukiGUI.WrapMini);
                EditorGUILayout.Space(4);
                try
                {
                    _panel.OnInspectorGUI();
                }
                catch (System.Exception e)
                {
                    EditorGUILayout.HelpBox(e.Message, MessageType.Error);
                }
            }

            if (Event.current.type == EventType.Repaint) Capture();
        }

        // ----------------------------------------------------------- overlays

        readonly HashSet<string> _openAdvanced = new HashSet<string>();

        TextureBaker PreviewBaker => _previewBaker ?? (_previewBaker = new TextureBaker(compress: false, maxSize: 256));

        void DrawOverlays(YukiMaterial config, MaterialTarget target)
        {
            var variant = Editing(target);
            if (variant == null) return;
            var overlays = variant.edit.overlays;

            YukiGUI.Section(L["ui.overlays"]);
            EditorGUILayout.LabelField(L["ui.overlays.help"], YukiGUI.WrapMini);
            EditorGUILayout.Space(2);

            DrawResult(variant.edit);
            DrawDropZone(config, target);

            if (overlays.Count == 0) return;
            if (overlays.Count > 1) EditorGUILayout.LabelField(L["ui.overlay.order"], EditorStyles.miniLabel);

            OverlayLayer remove = null, moving = null;
            int move = 0;

            foreach (var layer in overlays.ToList())
            {
                if (layer == null) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
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
                                    UndoEdit.Begin(config, "Toggle overlay");
                                    layer.enabled = enabled;
                                    UndoEdit.End(config);
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
                                System.Enum.GetNames(typeof(OverlayTarget)).Select(n => L["ui.overlay.target." + n]).ToArray());
                            var opacity = EditorGUILayout.Slider(L["ui.overlay.opacity"], layer.opacity, 0f, 1f);
                            if (EditorGUI.EndChangeCheck())
                            {
                                UndoEdit.Begin(config, "Edit overlay");
                                layer.target = targetKind;
                                layer.opacity = opacity;
                                UndoEdit.End(config);
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
                            System.Enum.GetNames(typeof(OverlayBlend)).Select(n => L["ui.overlay.blend." + n]).ToArray());
                        var mask = (Texture)EditorGUILayout.ObjectField(new GUIContent(L["ui.overlay.mask"], L["ui.overlay.mask.tip"]), layer.mask, typeof(Texture), false);
                        var tint = EditorGUILayout.ColorField(L["ui.overlay.tint"], layer.tint);
                        var scale = EditorGUILayout.Vector2Field(L["ui.overlay.scale"], layer.scale);
                        var offset = EditorGUILayout.Vector2Field(L["ui.overlay.offset"], layer.offset);
                        var property = EditorGUILayout.TextField(new GUIContent(L["ui.overlay.property"], L["ui.overlay.property.tip"]), layer.property);
                        if (EditorGUI.EndChangeCheck())
                        {
                            UndoEdit.Begin(config, "Edit overlay");
                            layer.texture = texture;
                            layer.blend = blend;
                            layer.mask = mask;
                            layer.tint = tint;
                            layer.scale = scale;
                            layer.offset = offset;
                            layer.property = property;
                            UndoEdit.End(config);
                            OnOverlayChanged();
                        }
                    }
                }
            }

            if (moving != null) { MaterialActions.MoveOverlay(config, variant, moving, move); OnOverlayChanged(); GUIUtility.ExitGUI(); }
            if (remove != null) { _openAdvanced.Remove(remove.id); MaterialActions.RemoveOverlay(config, variant, remove); OnOverlayChanged(); GUIUtility.ExitGUI(); }
        }

        /// <summary>
        /// The versions of one slot. Only a menu has more than one; a permanent
        /// change has nothing to pick between, so the list stays hidden.
        /// </summary>
        void DrawVariants(YukiMaterial config, MaterialTarget target)
        {
            if (!config.asMenu) return;

            YukiGUI.Section(L["ui.variants"]);
            EditorGUILayout.LabelField(L["ui.variants.help"], YukiGUI.WrapMini);

            MaterialVariant remove = null, makeDefault = null;
            var editing = Editing(target);

            foreach (var variant in target.variants.ToList())
            {
                if (variant == null) continue;
                bool isEditing = variant == editing;
                using (new EditorGUILayout.HorizontalScope(isEditing ? EditorStyles.helpBox : GUIStyle.none))
                {
                    if (GUILayout.Button(VariantName(target, variant), isEditing ? EditorStyles.boldLabel : EditorStyles.label))
                    {
                        editingVariantId = variant.id;
                        Release();
                        GUIUtility.ExitGUI();
                    }

                    bool showing = MaterialPreviewState.IsShowing(config, target, variant);
                    if (GUILayout.Button(new GUIContent(showing ? L["ui.stop_preview"] : L["ui.try_on"], L["ui.try_on.tip"]),
                                         EditorStyles.miniButton, GUILayout.Width(70)))
                        MaterialPreviewState.Show(config, target, showing ? null : variant);

                    bool isDefault = variant.id == target.defaultVariant;
                    using (new EditorGUI.DisabledScope(isDefault))
                        if (GUILayout.Button(isDefault ? L["ui.badge.default"] : L["ui.set_default"], EditorStyles.miniButton, GUILayout.Width(70)))
                            makeDefault = variant;

                    using (new EditorGUI.DisabledScope(target.variants.Count <= 1))
                        if (GUILayout.Button("\u00d7", GUILayout.Width(22))) remove = variant;
                }
            }

            if (makeDefault != null) { MaterialActions.SetDefaultVariant(config, target, makeDefault); GUIUtility.ExitGUI(); }
            if (remove != null)
            {
                if (remove == editing) editingVariantId = "";
                MaterialPreviewState.Show(config, target, null);
                MaterialActions.RemoveVariant(config, target, remove);
                Release();
                GUIUtility.ExitGUI();
            }

            if (GUILayout.Button(L["ui.add_variant"], GUILayout.Width(140)))
            {
                var added = MaterialActions.AddVariant(config, target);
                editingVariantId = added.id;
                Release();
                GUIUtility.ExitGUI();
            }

            // Renaming the version being edited, where it is being edited.
            if (editing != null)
            {
                EditorGUI.BeginChangeCheck();
                var name = EditorGUILayout.TextField(L["ui.variant_name"], editing.displayName);
                var icon = (Texture2D)EditorGUILayout.ObjectField(L["ui.icon"], editing.icon, typeof(Texture2D), false, GUILayout.Height(16));
                if (EditorGUI.EndChangeCheck())
                {
                    UndoEdit.Begin(config, "Rename version");
                    editing.displayName = name;
                    editing.icon = icon;
                    UndoEdit.End(config);
                }
            }
            EditorGUILayout.Space(4);
        }

        string VariantName(MaterialTarget target, MaterialVariant variant) =>
            MaterialModel.Fallback(variant.displayName,
                MaterialText.L.Tr("ui.variant_n", target.variants.IndexOf(variant) + 1));

        /// <summary>What the layers add up to, so the result is visible here
        /// rather than only in the Scene view.</summary>
        void DrawResult(MaterialEdit edit)
        {
            if (_work == null || !edit.HasOverlays) return;

            var targets = new List<OverlayTarget>();
            foreach (OverlayTarget t in System.Enum.GetValues(typeof(OverlayTarget)))
                if (OverlayBaker.Targets(_work, edit, t)) targets.Add(t);
            if (targets.Count == 0) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var t in targets)
                {
                    Texture composite = null;
                    try { composite = OverlayBaker.Composite(_work, edit, PreviewBaker, t); }
                    catch { /* a broken layer must not take the inspector down */ }
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

        void Thumbnail(Texture texture, float size)
        {
            var rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
        }

        /// <summary>Drop images here; one layer per image, settings guessed.</summary>
        void DrawDropZone(YukiMaterial config, MaterialTarget target)
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
                        AddLayer(config, target, o);
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
                if (picked != null) { AddLayer(config, target, picked); e.Use(); GUIUtility.ExitGUI(); }
            }
        }

        void AddLayer(YukiMaterial config, MaterialTarget target, Texture texture)
        {
            var layer = MaterialActions.AddOverlay(config, target, Editing(target));
            if (layer == null) return;
            OverlayTarget guessedTarget;
            OverlayBlend guessedBlend;
            OverlayBaker.Guess(texture, out guessedTarget, out guessedBlend);
            UndoEdit.Begin(config, "Add overlay");
            layer.texture = texture;
            layer.target = guessedTarget;
            layer.blend = guessedBlend;
            UndoEdit.End(config);
            OnOverlayChanged();
        }

        /// <summary>
        /// The overlay list is not part of what Capture() diffs, so the snapshot
        /// it compares against has to be refreshed by hand after an edit here.
        /// </summary>
        void OnOverlayChanged()
        {
            if (_boundVariant != null) _lastJson = JsonUtility.ToJson(_boundVariant.edit);
        }
    }
}
