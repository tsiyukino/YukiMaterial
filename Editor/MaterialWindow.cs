using System.Collections.Generic;
using System.Linq;
using TsiYuki.Core.Editor;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// Everything Yuki Material is doing to one avatar, on one screen.
    ///
    /// The window holds no data and is not a second editor: a row selects the
    /// object so the inspector shows the detail. What it adds is the view no
    /// inspector can give — every component at once, what each one costs, and
    /// what they collide over.
    /// </summary>
    public class MaterialWindow : EditorWindow
    {
        static YukiLocalizer L => MaterialText.L;

        [SerializeField] VRCAvatarDescriptor avatar;
        Vector2 scroll;
        MaterialSet set;
        double lastRefresh;

        static string _version;
        static string Version
        {
            get
            {
                if (_version != null) return _version;
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(MaterialWindow).Assembly);
                return _version = info != null ? "v" + info.version : "";
            }
        }

        [MenuItem(YukiMenu.Root + "Material Editor")]
        public static void ShowWindow() => Open(null);

        public static MaterialWindow Open(YukiMaterial config)
        {
            var window = GetWindow<MaterialWindow>();
            window.titleContent = new GUIContent("Yuki Material");
            if (config != null)
            {
                var found = config.GetComponentInParent<VRCAvatarDescriptor>();
                if (found != null) window.avatar = found;
            }
            window.Refresh();
            window.Show();
            return window;
        }

        void OnEnable() => EditorApplication.hierarchyChanged += OnHierarchyChanged;
        void OnDisable() => EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        void OnHierarchyChanged() { set = null; Repaint(); }

        void Refresh()
        {
            set = avatar != null ? MaterialSet.Resolve(avatar.transform) : null;
            lastRefresh = EditorApplication.timeSinceStartup;
        }

        void OnGUI()
        {
            YukiGUI.Header(L["ui.title"], Version);

            PickAvatar();
            if (avatar == null)
            {
                EditorGUILayout.HelpBox(L["ui.pick_avatar"], MessageType.Info);
                return;
            }
            // Cheap to resolve, but not every repaint.
            if (set == null || EditorApplication.timeSinceStartup - lastRefresh > 1.0) Refresh();

            var components = avatar.GetComponentsInChildren<YukiMaterial>(true).ToList();
            if (components.Count == 0)
            {
                EditorGUILayout.HelpBox(L["ui.window.empty"], MessageType.Info);
                DrawAddButton();
                return;
            }

            DrawBudget(components);
            DrawProblems();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawGroup(L["ui.window.permanent"], L["ui.permanent.help"], components.Where(c => !c.asMenu).ToList());
            DrawGroup(L["ui.window.menus"], L["ui.as_menu.help"], components.Where(c => c.asMenu).ToList());
            EditorGUILayout.EndScrollView();

            DrawAddButton();
        }

        void PickAvatar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var picked = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(L["ui.avatar"], avatar, typeof(VRCAvatarDescriptor), true);
                if (EditorGUI.EndChangeCheck()) { avatar = picked; Refresh(); }

                // Following the selection is what you want nine times out of ten.
                if (Selection.activeGameObject != null)
                {
                    var fromSelection = Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>();
                    if (fromSelection != null && fromSelection != avatar &&
                        GUILayout.Button(L["ui.window.use_selection"], EditorStyles.miniButton, GUILayout.Width(110)))
                    { avatar = fromSelection; Refresh(); }
                }
            }
        }

        void DrawBudget(List<YukiMaterial> components)
        {
            if (set == null) return;
            int bits = set.Models.Sum(m => m.TotalBits);
            int slots = set.Models.Sum(m => m.Targets.Count);
            EditorGUILayout.LabelField(
                L.Tr("ui.window.summary", components.Count, slots, bits),
                bits > 0 ? EditorStyles.boldLabel : EditorStyles.label);
        }

        void DrawProblems()
        {
            if (set == null) return;
            foreach (var warning in set.Models.SelectMany(m => m.Warnings))
                EditorGUILayout.HelpBox(warning.Message, MessageType.Warning);
            foreach (var conflict in set.Conflicts())
                EditorGUILayout.HelpBox(conflict.Message, MessageType.Error);
        }

        void DrawGroup(string title, string help, List<YukiMaterial> components)
        {
            if (components.Count == 0) return;
            YukiGUI.Section(title);
            EditorGUILayout.LabelField(help, YukiGUI.WrapMini);

            foreach (var config in components)
            {
                var model = set != null ? set.Models.FirstOrDefault(m => m.Config == config) : null;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var name = config.asMenu
                            ? MaterialModel.Fallback(config.displayName, config.gameObject.name)
                            : config.gameObject.name;
                        if (GUILayout.Button(name, EditorStyles.boldLabel))
                            EditorGUIUtility.PingObject(Selection.activeObject = config.gameObject);

                        GUILayout.FlexibleSpace();
                        if (config.asMenu && model != null)
                            GUILayout.Label(L.Tr("ui.window.bits", model.TotalBits), EditorStyles.miniLabel);
                        if (GUILayout.Button(L["ui.select_object"], EditorStyles.miniButton, GUILayout.Width(60)))
                            EditorGUIUtility.PingObject(Selection.activeObject = config.gameObject);
                    }

                    if (config.asMenu && config.menuParent != null)
                        EditorGUILayout.LabelField(L.Tr("ui.window.installs_into", config.menuParent.name), EditorStyles.miniLabel);

                    foreach (var target in config.targets)
                    {
                        if (target == null) continue;
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(12);
                            GUILayout.Label(MaterialModel.NameOf(target), GUILayout.Width(170));
                            GUILayout.Label(target.renderer != null ? target.renderer.name : "—", EditorStyles.miniLabel, GUILayout.Width(110));
                            GUILayout.Label(L.Tr("ui.slot", target.slot), EditorStyles.miniLabel, GUILayout.Width(56));

                            int changes = target.variants.Where(v => v != null && v.edit != null).Sum(v => v.edit.Count);
                            var text = config.asMenu
                                ? L.Tr("ui.window.versions", target.variants.Count) + "  " + L.Tr("ui.changes", changes)
                                : (changes > 0 ? L.Tr("ui.changes", changes) : L["ui.unchanged"]);
                            YukiGUI.StatusLabel(changes > 0 ? YukiStatus.Approximate : YukiStatus.Ok, text);
                        }
                    }
                }
            }
        }

        void DrawAddButton()
        {
            var go = Selection.activeGameObject;
            bool usable = go != null && avatar != null && go.GetComponentInParent<VRCAvatarDescriptor>() == avatar
                          && go.GetComponent<YukiMaterial>() == null;
            using (new EditorGUI.DisabledScope(!usable))
                if (GUILayout.Button(usable
                        ? L.Tr("ui.window.add_to", go.name)
                        : L["ui.window.add_hint"]))
                {
                    var config = Undo.AddComponent<YukiMaterial>(go);
                    config.EnsureIds();
                    MaterialActions.FillFrom(config, go, go.GetComponent<Renderer>() == null);
                    Selection.activeGameObject = go;
                    Refresh();
                }
        }
    }
}
