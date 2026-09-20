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
        MaterialTarget _bound;
        UnityEngine.Material _base;
        UnityEngine.Material _reference;
        UnityEngine.Material _work;
        MaterialEditor _panel;
        string _lastJson = "";

        static readonly PropertyInfo FirstInspected =
            typeof(UnityEditor.Editor).GetProperty("firstInspectedEditor", BindingFlags.Instance | BindingFlags.NonPublic);

        void OnDisable() => Release();

        void Release()
        {
            if (_panel != null) DestroyImmediate(_panel);
            if (_work != null) DestroyImmediate(_work);
            if (_reference != null) DestroyImmediate(_reference);
            _panel = null;
            _work = null;
            _reference = null;
            _base = null;
            _bound = null;
            _lastJson = "";
        }

        // ------------------------------------------------------------ binding

        void Bind(MaterialTarget target)
        {
            Release();
            if (target == null || target.renderer == null) return;
            var slots = target.renderer.sharedMaterials;
            if (target.slot < 0 || target.slot >= slots.Length || slots[target.slot] == null) return;

            _bound = target;
            _base = slots[target.slot];
            var edit = target.First != null ? target.First.edit : new MaterialEdit();

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
            var variant = _bound.First;
            if (variant == null) return;

            // A shader swap in the panel invalidates the cached reference.
            if (_reference != null && _reference.shader != _work.shader)
            {
                DestroyImmediate(_reference);
                _reference = MaterialDiff.Reference(_base, _work.shader != _base.shader ? _work.shader : null);
            }

            var edit = MaterialDiff.Extract(_work, _base, _reference);
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
            if (_bound != target) Bind(target);
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

            var edit = target.First != null ? target.First.edit : null;
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
                                MaterialActions.ResetProperty(config, target, property.name);
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
                        MaterialActions.ResetEdit(config, target);
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
            YukiGUI.Section(L["ui.edit_material"]);
            EditorGUILayout.LabelField(L["ui.edit_material.help"], YukiGUI.WrapMini);
            EditorGUILayout.Space(4);

            // The shader's own inspector, drawn on the working copy.
            try
            {
                _panel.OnInspectorGUI();
            }
            catch (System.Exception e)
            {
                EditorGUILayout.HelpBox(e.Message, MessageType.Error);
            }

            if (Event.current.type == EventType.Repaint) Capture();
        }
    }
}
