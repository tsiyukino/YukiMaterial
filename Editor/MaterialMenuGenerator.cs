using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// Builds the menu as Modular Avatar menu items under the generated host, so
    /// MA handles installation and paging.
    ///
    /// One component is one menu; each material slot inside it is a submenu of
    /// versions. With a single slot the submenu would be a folder containing
    /// only itself, so it is flattened away.
    /// </summary>
    public static class MaterialMenuGenerator
    {
        public static GameObject Build(MaterialModel model, Transform host)
        {
            var root = new GameObject(model.MenuName);
            root.transform.SetParent(host, false);
            root.AddComponent<ModularAvatarMenuInstaller>();
            SubMenu(root, model.MenuName, model.MenuIcon);

            if (model.Targets.Count == 1)
            {
                AddVersions(root.transform, model.Targets[0]);
                return root;
            }

            foreach (var target in model.Targets)
            {
                var folder = Child(root.transform, target.DisplayName);
                SubMenu(folder, target.DisplayName, target.Source.icon);
                AddVersions(folder.transform, target);
            }
            return root;
        }

        static void AddVersions(Transform parent, ResolvedTarget target)
        {
            foreach (var variant in target.Variants)
                Item(parent, target.VariantName(variant), variant.icon,
                     VRCExpressionsMenu.Control.ControlType.Toggle, target.ParameterName, variant.value);
        }

        static GameObject Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
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

        static void Item(Transform parent, string label, Texture2D icon, VRCExpressionsMenu.Control.ControlType type, string parameter, float value)
        {
            var go = Child(parent, label);
            var item = go.AddComponent<ModularAvatarMenuItem>();
            item.Control = new VRCExpressionsMenu.Control
            {
                name = label,
                icon = icon,
                type = type,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter },
                value = value,
            };
            item.label = label;
            item.automaticValue = false;
        }
    }
}
