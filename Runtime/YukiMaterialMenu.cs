using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace TsiYuki.Materials
{
    /// <summary>
    /// A material slot a menu drives, named from wherever the menu lives.
    ///
    /// The menu refers to the slot rather than owning it: the hair keeps its
    /// looks on the hair, the ears keep theirs on the ears, and one menu points
    /// at both. That is the only way two objects can change in one state, since
    /// an object's own component cannot reach across to another.
    /// </summary>
    [Serializable]
    public class MaterialSlotRef
    {
        /// <summary>What the states name this slot by. Stable across renaming,
        /// reordering and re-picking the component.</summary>
        public string key = "";

        public YukiMaterial source;
        public string targetId = "";

        public bool IsBroken { get { return source == null || string.IsNullOrEmpty(targetId); } }
    }

    /// <summary>What one slot wears in one state. An empty look is the material
    /// the slot already has — the change that changes nothing.</summary>
    [Serializable]
    public class MaterialBinding
    {
        public string slotKey = "";
        public string variantId = MaterialVariant.Original;
    }

    /// <summary>
    /// One thing the menu can be set to, across every slot it drives.
    ///
    /// The hair and the ears turning pink is one of these, not two: a state says
    /// what each slot wears, and a slot the state has nothing to say about wears
    /// its own material.
    /// </summary>
    [Serializable]
    public class MaterialState
    {
        public string id = "";
        public string displayName = "";
        public Texture2D icon;
        public int value = 1;

        public List<MaterialBinding> bindings = new List<MaterialBinding>();

        /// <summary>The look this state puts on that slot; empty is the slot's
        /// own material, which is also the answer for a slot never mentioned.</summary>
        public string Wears(string slotKey)
        {
            foreach (var binding in bindings)
                if (binding != null && binding.slotKey == slotKey) return binding.variantId;
            return MaterialVariant.Original;
        }

        public void Wear(string slotKey, string variantId)
        {
            foreach (var binding in bindings)
                if (binding != null && binding.slotKey == slotKey)
                {
                    binding.variantId = variantId;
                    return;
                }
            bindings.Add(new MaterialBinding { slotKey = slotKey, variantId = variantId });
        }

        public void Forget(string slotKey)
        {
            bindings.RemoveAll(b => b == null || b.slotKey == slotKey);
        }

        /// <summary>How many of the given slots this state actually moves.</summary>
        public int Moves(List<MaterialSlotRef> slots)
        {
            int moved = 0;
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                if (!string.IsNullOrEmpty(Wears(slot.key))) moved++;
            }
            return moved;
        }
    }

    /// <summary>
    /// One menu item that switches material looks, and the slots it drives.
    ///
    /// Put it anywhere — the avatar root is the usual place. It costs one synced
    /// int however many slots it moves, and reaches those slots through Yuki
    /// Material components on the objects that own them.
    /// </summary>
    [AddComponentMenu("TsiYuki/Yuki Material Menu")]
    public class YukiMaterialMenu : MonoBehaviour, IEditorOnly
    {
        public string id = "";

        // Menu label; empty uses the object name.
        public string displayName = "";
        public Texture2D icon;
        public bool saved = true;

        // Empty builds one from the component id, which survives renaming.
        public string parameterName = "";

        // Where this menu is installed; null is the avatar's root menu. Takes a
        // VRCExpressionsMenu asset, an object carrying a Modular Avatar menu
        // item, or another TsiYuki component that makes a menu. Held loosely so
        // the runtime assembly needs none of those types.
        public UnityEngine.Object menuParent;

        /// <summary>The slots this menu drives: the columns of the table.</summary>
        public List<MaterialSlotRef> slots = new List<MaterialSlotRef>();

        /// <summary>What it can be set to: the rows.</summary>
        public List<MaterialState> states = new List<MaterialState>();

        public string defaultState = "";
        public int nextValue = 1;

        public static string KeyOf(YukiMaterial source, string targetId)
        {
            if (source == null || string.IsNullOrEmpty(targetId)) return "";
            return source.id + "/" + targetId;
        }

        public MaterialSlotRef FindSlot(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var slot in slots)
                if (slot != null && slot.key == key) return slot;
            return null;
        }

        public MaterialSlotRef FindSlot(YukiMaterial source, string targetId)
        {
            foreach (var slot in slots)
                if (slot != null && slot.source == source && slot.targetId == targetId) return slot;
            return null;
        }

        public MaterialState FindState(string stateId)
        {
            if (string.IsNullOrEmpty(stateId)) return null;
            foreach (var state in states)
                if (state != null && state.id == stateId) return state;
            return null;
        }

        /// <summary>The state the avatar spawns in.</summary>
        public MaterialState Default
        {
            get
            {
                var state = FindState(defaultState);
                if (state != null) return state;
                return states.Count > 0 ? states[0] : null;
            }
        }

        /// <summary>Whether this menu has anything to say about that slot.</summary>
        public bool Drives(YukiMaterial source, string targetId)
        {
            return FindSlot(source, targetId) != null;
        }

        public bool EnsureIds()
        {
            bool changed = false;
            if (string.IsNullOrEmpty(id)) { id = NewId(); changed = true; }

            var keys = new HashSet<string>();
            for (int i = slots.Count - 1; i >= 0; i--)
            {
                var slot = slots[i];
                // A slot whose object is gone takes its column with it; leaving
                // a dead column would only put empty cells in every state.
                if (slot == null || slot.IsBroken)
                {
                    if (slot != null) Unbind(slot.key);
                    slots.RemoveAt(i);
                    changed = true;
                    continue;
                }
                var key = KeyOf(slot.source, slot.targetId);
                if (slot.key != key || !keys.Add(key))
                {
                    if (!keys.Contains(key)) keys.Add(key);
                    slot.key = key;
                    changed = true;
                }
            }

            var stateIds = new HashSet<string>();
            foreach (var state in states)
            {
                if (state == null) continue;
                if (string.IsNullOrEmpty(state.id) || !stateIds.Add(state.id))
                {
                    state.id = Unique(stateIds);
                    changed = true;
                }
                if (state.value <= 0) { state.value = nextValue++; changed = true; }
                if (state.value >= nextValue) { nextValue = state.value + 1; changed = true; }

                // Cells for columns that no longer exist are not worth keeping:
                // re-adding the slot should not bring back forgotten answers.
                int before = state.bindings.Count;
                state.bindings.RemoveAll(b => b == null || FindSlot(b.slotKey) == null);
                if (state.bindings.Count != before) changed = true;
            }

            if (states.Count > 0 && FindState(defaultState) == null)
            {
                defaultState = states[0].id;
                changed = true;
            }
            return changed;
        }

        void Unbind(string key)
        {
            foreach (var state in states)
                if (state != null) state.Forget(key);
        }

        public static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 6); }

        static string Unique(HashSet<string> used)
        {
            string candidate;
            do candidate = NewId(); while (!used.Add(candidate));
            return candidate;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (EnsureIds()) UnityEditor.EditorUtility.SetDirty(this);
        }

        void Reset()
        {
            id = NewId();
        }
#endif
    }
}
