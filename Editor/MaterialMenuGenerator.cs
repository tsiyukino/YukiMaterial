using TsiYuki.Core.Menus.Editor;
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
            var root = MenuItems.Root(host, menu.DisplayName, menu.Icon);
            foreach (var state in menu.States)
                MenuItems.Control(root.transform, state.DisplayName, state.Icon,
                                  VRCExpressionsMenu.Control.ControlType.Toggle, menu.ParameterName, state.Value);
            return root;
        }
    }
}
