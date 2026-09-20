using UnityEditor;
using UnityEngine;

namespace TsiYuki.Materials.Editor
{
    // Undo-correct component editing: record before mutating, mark dirty and
    // register prefab-instance overrides after.
    static class UndoEdit
    {
        public static void Begin(Object target, string action) => Undo.RecordObject(target, action);

        public static void End(Object target)
        {
            EditorUtility.SetDirty(target);
            if (target is Component component)
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
    }
}
