using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace TsiYuki.Materials.Editor
{
    // Hierarchy right-click: GameObject > TsiYuki > Edit Materials.
    // The components are also under Add Component > TsiYuki.
    static class MaterialMenu
    {
        const string Path = "GameObject/TsiYuki/Edit Materials";

        [MenuItem(Path, false, 21)]
        static void EditMaterials(MenuCommand command)
        {
            // Called once per selected object; handle the whole selection once.
            if (command.context != null && command.context != Selection.activeGameObject) return;
            var objects = Selection.gameObjects;
            if (objects.Length == 0) return;

            if (objects[0].GetComponentInParent<VRCAvatarDescriptor>() == null)
            {
                EditorUtility.DisplayDialog(MaterialText.L["ui.title"], MaterialText.L["ui.not_in_avatar"], MaterialText.L["ui.ok"]);
                return;
            }

            YukiMaterial last = null;
            foreach (var go in objects)
            {
                var config = MaterialActions.CatalogueFor(go);
                // A component added straight onto a mesh is meant for that mesh,
                // so fill the slots instead of leaving an empty list.
                if (config.targets.Count == 0)
                    MaterialActions.FillFrom(config, go, go.GetComponent<Renderer>() == null);
                last = config;
            }

            if (last != null)
            {
                Selection.activeGameObject = last.gameObject;
                MaterialWindow.Open(last);
            }
        }

        [MenuItem(Path, true)]
        static bool Validate() =>
            Selection.activeGameObject != null &&
            Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>() != null;
    }
}
