using System.Linq;
using TsiYuki.Core.Editor;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// What this object's material slots can look like, and nothing else.
    ///
    /// The inspector deliberately stays a list: editing a material, and wiring
    /// looks into a menu, both happen in the panel, where the other objects
    /// involved are visible too. What is worth having here is the answer to
    /// "what is this component doing to this object", at a glance.
    /// </summary>
    [CustomEditor(typeof(YukiMaterial))]
    public class YukiMaterialEditor : UnityEditor.Editor
    {
        static YukiLocalizer L => MaterialText.L;

        YukiMaterial Config => (YukiMaterial)target;

        public override void OnInspectorGUI()
        {
            var config = Config;
            if (config.EnsureIds()) EditorUtility.SetDirty(config);

            YukiGUI.Header(L["ui.title"], "");

            if (config.GetComponentInParent<VRCAvatarDescriptor>() == null)
                EditorGUILayout.HelpBox(L["ui.not_in_avatar"], MessageType.Warning);

            if (GUILayout.Button(L["ui.open_panel"], GUILayout.Height(24)))
            {
                MaterialWindow.Open(config);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.LabelField(L["ui.open_panel.help"], YukiGUI.WrapMini);
            EditorGUILayout.Space(4);

            if (config.targets.Count == 0)
            {
                EditorGUILayout.HelpBox(L["ui.no_targets"], MessageType.Info);
                if (GUILayout.Button(new GUIContent(L["ui.fill_from_object"], L["ui.fill_from_object.tip"])))
                {
                    MaterialActions.FillFrom(config, config.gameObject, config.GetComponent<Renderer>() == null);
                    GUIUtility.ExitGUI();
                }
                return;
            }

            var menus = MaterialActions.MenusOf(config).ToList();
            foreach (var slot in config.targets)
            {
                if (slot == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(MaterialSet.NameOf(slot), GUILayout.Width(150));
                    GUILayout.Label(L.Tr("ui.slot", slot.slot), EditorStyles.miniLabel, GUILayout.Width(52));
                    GUILayout.Label(L.Tr("ui.looks_n", slot.variants.Count), EditorStyles.miniLabel, GUILayout.Width(70));

                    var driver = menus.FirstOrDefault(m => m.Drives(config, slot.id));
                    GUILayout.Label(driver != null
                            ? L.Tr("ui.slot.driven_by", MaterialSet.NameOf(driver))
                            : (slot.ChangesOnItsOwn ? L["ui.slot.permanent"] : L["ui.unchanged"]),
                        EditorStyles.miniLabel);
                }
            }
        }
    }

    /// <summary>
    /// One menu, summarised. Everything about it — the states, the slots it
    /// drives, what each one wears — is a table, and a table does not fit in an
    /// inspector column, so it lives in the panel.
    /// </summary>
    [CustomEditor(typeof(YukiMaterialMenu))]
    public class YukiMaterialMenuEditor : UnityEditor.Editor
    {
        static YukiLocalizer L => MaterialText.L;

        YukiMaterialMenu Menu => (YukiMaterialMenu)target;

        public override void OnInspectorGUI()
        {
            var menu = Menu;
            if (menu.EnsureIds()) EditorUtility.SetDirty(menu);

            YukiGUI.Header(L["ui.title"], "");

            if (menu.GetComponentInParent<VRCAvatarDescriptor>() == null)
                EditorGUILayout.HelpBox(L["ui.not_in_avatar"], MessageType.Warning);

            if (GUILayout.Button(L["ui.open_panel"], GUILayout.Height(24)))
            {
                MaterialWindow.Open(menu);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.LabelField(L["ui.open_panel.menu_help"], YukiGUI.WrapMini);
            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField(L["ui.menu_name"], MaterialSet.NameOf(menu));
            EditorGUILayout.LabelField(L["ui.table.title"],
                L.Tr("ui.menu.states_n", menu.states.Count) + " · " + L.Tr("ui.menu.slots_n", menu.slots.Count));
            EditorGUILayout.LabelField(L["ui.menu.cost"], L.Tr("ui.window.bits", 8));

            foreach (var slot in menu.slots)
            {
                if (slot == null || slot.source == null) continue;
                var found = slot.source.FindTarget(slot.targetId);
                EditorGUILayout.LabelField("    " + slot.source.gameObject.name,
                    found != null ? MaterialSet.NameOf(found) : L["ui.column.missing"], EditorStyles.miniLabel);
            }
        }
    }
}
