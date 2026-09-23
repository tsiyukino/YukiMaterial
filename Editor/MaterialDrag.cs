using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// Dragging material slots out of the panel's list and objects in from the
    /// Hierarchy.
    ///
    /// Unity's drag payload only carries assets and scene objects, and a slot is
    /// neither — it is one row of a component. So the slot travels as generic
    /// data, with its component alongside it as the visible object so the drag
    /// looks like something and other windows can ignore it.
    /// </summary>
    internal static class MaterialDrag
    {
        const string Key = "moe.tsiyuki.material/slot";

        /// <summary>The bar's height, so a button beside it can match.</summary>
        public const float BarHeight = 28f;

        struct Payload
        {
            public YukiMaterial Config;
            public string TargetId;
        }

        public static void Begin(YukiMaterial config, MaterialTarget target, string label)
        {
            if (config == null || target == null) return;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new Object[] { config.gameObject };
            DragAndDrop.SetGenericData(Key, new Payload { Config = config, TargetId = target.id });
            DragAndDrop.StartDrag(label);
        }

        /// <summary>The slot being dragged, or false when it is something else.</summary>
        public static bool Slot(out YukiMaterial config, out string targetId)
        {
            config = null;
            targetId = null;
            var data = DragAndDrop.GetGenericData(Key);
            if (!(data is Payload)) return false;
            var payload = (Payload)data;
            if (payload.Config == null || string.IsNullOrEmpty(payload.TargetId)) return false;
            config = payload.Config;
            targetId = payload.TargetId;
            return true;
        }

        /// <summary>The objects being dragged that are inside this avatar. An
        /// object from another avatar, or from the project, is not something
        /// this panel can do anything with.</summary>
        public static List<GameObject> Objects(Transform avatarRoot)
        {
            var list = new List<GameObject>();
            if (avatarRoot == null) return list;
            foreach (var reference in DragAndDrop.objectReferences)
            {
                var go = reference as GameObject;
                if (go == null && reference is Component) go = ((Component)reference).gameObject;
                if (go == null || !go.transform.IsChildOf(avatarRoot)) continue;
                if (!list.Contains(go)) list.Add(go);
            }
            return list;
        }

        /// <summary>Renderers with something to change, on that object or,
        /// failing that, under it — the same reach as the button.</summary>
        public static bool HasSlots(GameObject go)
        {
            return Renderers(go).Any();
        }

        public static IEnumerable<Renderer> Renderers(GameObject go)
        {
            var own = go.GetComponents<Renderer>().Where(Usable).ToList();
            if (own.Count > 0) return own;
            return go.GetComponentsInChildren<Renderer>(true).Where(Usable);
        }

        static bool Usable(Renderer renderer)
        {
            if (!(renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) return false;
            foreach (var material in renderer.sharedMaterials)
                if (material != null) return true;
            return false;
        }

        /// <summary>
        /// A bar that takes a drop and a click. Draws its own highlight, tells
        /// the caller when it was clicked, and hands back a drop through
        /// <paramref name="dropped"/> so the caller can act after the layout is
        /// finished with.
        /// </summary>
        /// <param name="capture">Called the moment the drop happens, while the
        /// drag data is still there, and returns what to do about it. The doing
        /// waits: a panel in the middle of laying itself out is no place to add
        /// and remove things.</param>
        public static bool Bar(string label, bool willAccept, System.Func<System.Action> capture, out System.Action dropped,
                               params GUILayoutOption[] options)
        {
            dropped = null;
            var rect = GUILayoutUtility.GetRect(0, BarHeight, options);
            var e = Event.current;
            bool hover = rect.Contains(e.mousePosition);

            EditorGUI.DrawRect(rect, hover && willAccept
                ? new Color(0.3f, 0.5f, 0.8f, 0.35f)
                : new Color(0f, 0f, 0f, 0.12f));
            GUI.Label(rect, label, Centered);

            if (!hover) return false;
            switch (e.type)
            {
                case EventType.DragUpdated:
                    DragAndDrop.visualMode = willAccept ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                    e.Use();
                    return false;
                case EventType.DragPerform:
                    if (!willAccept) return false;
                    DragAndDrop.AcceptDrag();
                    dropped = capture();
                    e.Use();
                    return false;
                case EventType.MouseDown when e.button == 0:
                    e.Use();
                    return true;
            }
            return false;
        }

        static GUIStyle _centered;
        static GUIStyle Centered => _centered ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true };
    }
}
