using nadena.dev.modular_avatar.core;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// Builds one menu as Modular Avatar menu items under the generated host, so
    /// MA handles installation and paging.
    ///
    /// One menu component is one submenu with one item per state. Picking "Pink"
    /// changes every slot the menu drives at once, which is the point: the hair
    /// and the ears are asked once, not twice.
    /// </summary>
    public static class MaterialMenuGenerator
    {
        public static GameObject Build(ResolvedMenu menu, Transform host)
        {
            var root = new GameObject(menu.DisplayName);
            root.transform.SetParent(host, false);
            root.AddComponent<ModularAvatarMenuInstaller>();
            SubMenu(root, menu.DisplayName, menu.Icon);

            foreach (var state in menu.States)
                Item(root.transform, state.DisplayName, state.Icon, menu.ParameterName, state.Value);

            return root;
        }

        static void SubMenu(GameObject go, string label, Texture2D icon)
        {
            var item = go.AddComponent<ModularAvatarMenuItem>();
            item.Control = new VRCExpressionsMenu.Control
            {
                name = label,
                icon = icon,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
            };
            item.MenuSource = SubmenuSource.Children;
            item.label = label;
            item.automaticValue = false;
        }

        static void Item(Transform parent, string label, Texture2D icon, string parameter, float value)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            var item = go.AddComponent<ModularAvatarMenuItem>();
            item.Control = new VRCExpressionsMenu.Control
            {
                name = label,
                icon = icon,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter },
                value = value,
            };
            item.label = label;
            item.automaticValue = false;
        }
    }
}
