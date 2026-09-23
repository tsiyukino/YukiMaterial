using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TsiYuki.Materials.Editor
{
    public class ModelWarning
    {
        public string Key;
        public object[] Args;
        public Object Context;

        public ModelWarning(string key, Object context, params object[] args)
        {
            Key = key;
            Context = context;
            Args = args;
        }

        public string Message { get { return MaterialText.L.Tr(Key, Args); } }
    }

    /// <summary>One material slot that exists, checked and ready to be patched.</summary>
    public class ResolvedSlot
    {
        public YukiMaterial Config;
        public MaterialTarget Target;
        public Renderer Renderer;
        public int Slot;

        // What sits in the slot right now. Looks are applied on top of this, not
        // on top of the material they were authored against, so a slot another
        // tool already changed still gets the change.
        public UnityEngine.Material Original;
        public string DisplayName;

        /// <summary>The menu that drives it, or null when nothing does.</summary>
        public ResolvedMenu Menu;

        /// <summary>What the states name it by.</summary>
        public string Key { get { return YukiMaterialMenu.KeyOf(Config, Target.id); } }

        /// <summary>The physical slot, for spotting two components on one.</summary>
        public string Physical { get { return Renderer.GetInstanceID() + "#" + Slot; } }

        public string ObjectName { get { return Renderer != null ? Renderer.gameObject.name : "?"; } }
    }

    /// <summary>One thing a menu can be set to.</summary>
    public class ResolvedState
    {
        public MaterialState Source;
        public string DisplayName;
        public Texture2D Icon;
        public int Value;
    }

    /// <summary>
    /// One menu: the slots it drives and the states it switches them between.
    ///
    /// One menu is one synced int and one submenu, however many slots and
    /// however many objects it reaches across.
    /// </summary>
    public class ResolvedMenu
    {
        public YukiMaterialMenu Config;
        public string DisplayName;
        public Texture2D Icon;
        public string ParameterName;
        public bool Saved;

        public List<ResolvedSlot> Slots = new List<ResolvedSlot>();
        public List<ResolvedState> States = new List<ResolvedState>();
        public ResolvedState Default;

        /// <summary>The look that slot wears in that state; null is the
        /// material the slot already has.</summary>
        public MaterialVariant Wears(ResolvedSlot slot, ResolvedState state)
        {
            if (slot == null || state == null) return null;
            return slot.Target.Find(state.Source.Wears(slot.Key));
        }
    }

    /// <summary>
    /// Everything Yuki Material is doing to one avatar, resolved together: the
    /// catalogues, the menus that point into them, and the slots that change
    /// on upload because nothing switches them.
    /// </summary>
    public class MaterialSet
    {
        public Transform AvatarRoot;
        public List<YukiMaterial> Catalogues = new List<YukiMaterial>();
        public List<YukiMaterialMenu> MenuComponents = new List<YukiMaterialMenu>();
        public List<ResolvedMenu> Menus = new List<ResolvedMenu>();

        /// <summary>Every slot that survived checking, menu or not.</summary>
        public List<ResolvedSlot> Slots = new List<ResolvedSlot>();
        public List<ModelWarning> Warnings = new List<ModelWarning>();
        public List<ModelWarning> Errors = new List<ModelWarning>();

        /// <summary>One synced int per menu.</summary>
        public int TotalBits { get { return Menus.Count * 8; } }

        /// <summary>Slots that change on upload with nothing to switch them.</summary>
        public IEnumerable<ResolvedSlot> Permanent
        {
            get { return Slots.Where(s => s.Menu == null && s.Target.ChangesOnItsOwn); }
        }

        public ResolvedSlot Find(YukiMaterial config, string targetId)
        {
            foreach (var slot in Slots)
                if (slot.Config == config && slot.Target.id == targetId) return slot;
            return null;
        }

        // ---------------------------------------------------------- naming

        public static string Fallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        public static string NameOf(MaterialTarget target)
        {
            if (target == null) return "?";
            var material = target.source;
            return Fallback(target.displayName, material != null ? material.name : "?");
        }

        public static string NameOf(YukiMaterialMenu menu)
        {
            if (menu == null) return "?";
            return Fallback(menu.displayName, menu.gameObject.name);
        }

        /// <summary>The name of a look, with the untouched material as the one
        /// every slot has without being given it.</summary>
        public static string NameOf(MaterialTarget target, MaterialVariant variant)
        {
            if (variant == null) return MaterialText.L["ui.original"];
            int index = target != null ? target.variants.IndexOf(variant) + 1 : 1;
            return Fallback(variant.displayName, MaterialText.L.Tr("ui.variant_n", index));
        }

        public static string NameOf(MaterialState state, int index)
        {
            if (state == null) return "?";
            return Fallback(state.displayName, MaterialText.L.Tr("ui.state_n", index));
        }

        // ------------------------------------------------------- resolution

        public static MaterialSet Resolve(Transform avatarRoot)
        {
            var set = new MaterialSet { AvatarRoot = avatarRoot };
            if (avatarRoot == null) return set;

            foreach (var config in avatarRoot.GetComponentsInChildren<YukiMaterial>(true))
            {
                if (config == null) continue;
                config.EnsureIds();
                set.Catalogues.Add(config);
                set.ResolveSlots(config);
            }

            // Two components on one physical slot: neither can be trusted to
            // win, so it is reported rather than resolved.
            foreach (var group in set.Slots.GroupBy(s => s.Physical))
            {
                var owners = group.Select(s => s.Config).Distinct().ToList();
                if (owners.Count < 2) continue;
                var first = group.First();
                set.Errors.Add(new ModelWarning("conflict.two_components", first.Renderer,
                    first.ObjectName, first.Slot,
                    string.Join(", ", owners.Select(c => c.gameObject.name).ToArray())));
            }

            foreach (var menu in avatarRoot.GetComponentsInChildren<YukiMaterialMenu>(true))
            {
                if (menu == null) continue;
                menu.EnsureIds();
                set.MenuComponents.Add(menu);
                set.ResolveMenu(menu);
            }
            return set;
        }

        void ResolveSlots(YukiMaterial config)
        {
            foreach (var target in config.targets)
            {
                if (target == null) continue;
                var name = NameOf(target);

                if (target.renderer == null)
                {
                    Warnings.Add(new ModelWarning("warn.missing_renderer", config));
                    continue;
                }
                if (AvatarRoot != null && !target.renderer.transform.IsChildOf(AvatarRoot))
                {
                    Warnings.Add(new ModelWarning("warn.outside_avatar", target.renderer, target.renderer.name));
                    continue;
                }

                var slots = target.renderer.sharedMaterials;
                if (target.slot < 0 || target.slot >= slots.Length)
                {
                    Warnings.Add(new ModelWarning("warn.slot_out_of_range", target.renderer, name, target.slot));
                    continue;
                }

                var current = slots[target.slot];
                if (current == null)
                {
                    Warnings.Add(new ModelWarning("warn.missing_source", target.renderer, name));
                    continue;
                }
                if (target.source != null && current != target.source)
                    Warnings.Add(new ModelWarning("warn.source_changed", target.renderer, name, current.name));

                if (MaterialDiff.IsLockedPoiyomi(current) &&
                    target.variants.Any(v => v != null && v.edit != null &&
                                             v.edit.properties.Any(p => p != null && p.kind != PropertyKind.Texture)))
                    Warnings.Add(new ModelWarning("warn.locked_shader", current, name));

                Slots.Add(new ResolvedSlot
                {
                    Config = config,
                    Target = target,
                    Renderer = target.renderer,
                    Slot = target.slot,
                    Original = current,
                    DisplayName = name,
                });
            }
        }

        void ResolveMenu(YukiMaterialMenu menu)
        {
            var label = NameOf(menu);
            var resolved = new ResolvedMenu
            {
                Config = menu,
                DisplayName = label,
                Icon = menu.icon,
                Saved = menu.saved,
                ParameterName = !string.IsNullOrWhiteSpace(menu.parameterName)
                    ? menu.parameterName.Trim()
                    : "Material/" + menu.id,
            };

            foreach (var slot in menu.slots)
            {
                if (slot == null || slot.IsBroken) continue;
                var found = Find(slot.source, slot.targetId);
                if (found == null)
                {
                    // Already reported where the slot itself failed; saying it
                    // twice for every menu that points at it helps nobody.
                    continue;
                }
                if (found.Menu != null)
                {
                    Errors.Add(new ModelWarning("conflict.two_menus", menu,
                        found.ObjectName, found.DisplayName,
                        NameOf(found.Menu.Config) + ", " + label));
                    continue;
                }
                found.Menu = resolved;
                resolved.Slots.Add(found);
            }

            for (int i = 0; i < menu.states.Count; i++)
            {
                var state = menu.states[i];
                if (state == null) continue;
                resolved.States.Add(new ResolvedState
                {
                    Source = state,
                    DisplayName = NameOf(state, i + 1),
                    Icon = state.icon,
                    Value = state.value,
                });
            }
            resolved.Default = resolved.States.FirstOrDefault(s => s.Source == menu.Default)
                               ?? resolved.States.FirstOrDefault();

            if (resolved.Slots.Count == 0)
            {
                Warnings.Add(new ModelWarning("warn.menu_no_slots", menu, label));
                Release(resolved);
                return;
            }
            if (resolved.States.Count < 2)
            {
                Warnings.Add(new ModelWarning("warn.menu_one_state", menu, label));
                Release(resolved);
                return;
            }
            // Every state putting every slot back where it found it is a menu
            // that switches between one thing and the same thing.
            bool moves = resolved.States.Any(state => resolved.Slots.Any(slot =>
            {
                var variant = resolved.Wears(slot, state);
                return variant != null && variant.Changes;
            }));
            if (!moves)
            {
                Warnings.Add(new ModelWarning("warn.menu_changes_nothing", menu, label));
                Release(resolved);
                return;
            }

            Menus.Add(resolved);
        }

        /// <summary>A menu that will not be built lets go of its slots, so they
        /// are free to change on upload or to be driven by another menu.</summary>
        static void Release(ResolvedMenu menu)
        {
            foreach (var slot in menu.Slots) slot.Menu = null;
            menu.Slots.Clear();
        }
    }
}
