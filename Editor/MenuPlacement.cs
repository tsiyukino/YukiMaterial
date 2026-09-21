using System;
using nadena.dev.modular_avatar.core;
using TsiYuki.Core.Editor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// Puts a generated menu where the user asked for it. Three kinds of
    /// destination, because three kinds of thing can hold a menu:
    ///
    /// a menu asset, which Modular Avatar's installer takes directly; an object
    /// already carrying a menu item, which takes an install target underneath
    /// it; and another TsiYuki menu, which may not have been generated yet and
    /// so goes through the registry in Core.
    /// </summary>
    public static class MenuPlacement
    {
        /// <summary>Modular Avatar's install target is internal, so it is added by reflection —
        /// the same way the wardrobe absorbs the menus that ship inside outfits.</summary>
        static readonly Type InstallTargetType =
            typeof(ModularAvatarMenuInstaller).Assembly.GetType("nadena.dev.modular_avatar.core.ModularAvatarMenuInstallTarget");

        /// <param name="warn">key + args, matching the localization table.</param>
        public static void Place(UnityEngine.Object source, UnityEngine.Object destination, GameObject menuRoot,
                                 string label, Action<string, object[]> warn)
        {
            YukiMenuRegistry.Register(source, menuRoot);
            if (destination == null) return; // the avatar's root menu

            var asset = destination as VRCExpressionsMenu;
            if (asset != null)
            {
                var installer = menuRoot.GetComponent<ModularAvatarMenuInstaller>();
                if (installer != null) installer.installTargetMenu = asset;
                return;
            }

            var go = destination as GameObject;
            if (go == null && destination is Component) go = ((Component)destination).gameObject;

            // An object that already carries a menu item can take us right away.
            if (go != null && go.GetComponent<ModularAvatarMenuItem>() != null)
            {
                InstallUnder(go, menuRoot, label, warn);
                return;
            }

            // Otherwise it should be another TsiYuki component, whose menu may
            // not exist yet. Core settles it once every tool has run.
            YukiMenuRegistry.Request(source, destination, Nest,
                name => warn?.Invoke("warn.menu_cycle", new object[] { label, name }),
                name => warn?.Invoke("warn.menu_parent_missing", new object[] { label, name }));
        }

        /// <summary>
        /// Our whole menu becomes a child of theirs. Their root lists its
        /// children as its submenu, so being a child is all it takes — and our
        /// own installer has to go, or the menu would also appear at the root.
        /// </summary>
        static void Nest(GameObject sourceRoot, GameObject destinationRoot)
        {
            var installer = sourceRoot.GetComponent<ModularAvatarMenuInstaller>();
            if (installer != null) UnityEngine.Object.DestroyImmediate(installer);
            sourceRoot.transform.SetParent(destinationRoot.transform, false);
        }

        static void InstallUnder(GameObject destination, GameObject menuRoot, string label, Action<string, object[]> warn)
        {
            var installer = menuRoot.GetComponent<ModularAvatarMenuInstaller>();
            if (installer == null) return;
            if (InstallTargetType == null)
            {
                warn?.Invoke("warn.no_install_target", new object[] { label });
                return;
            }
            var target = new GameObject(menuRoot.name);
            target.transform.SetParent(destination.transform, false);
            var component = target.AddComponent(InstallTargetType);
            InstallTargetType
                .GetField("installer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(component, installer);
        }
    }
}
