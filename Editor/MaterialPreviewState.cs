using System.Collections.Generic;
using nadena.dev.ndmf.preview;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// Which version of each material slot the Scene view is showing.
    ///
    /// A component that applies its change permanently has nothing to choose, so
    /// it is previewed as it is. A component that turns its versions into a menu
    /// shows the default one, and "Try on" points this somewhere else — the same
    /// deal as the wardrobe, where seeing another outfit is something you ask
    /// for rather than something that happens while you edit.
    /// </summary>
    internal static class MaterialPreviewState
    {
        // NDMF can only observe a value that compares by equality, so the table
        // is carried as text rather than a dictionary: "slot=version|slot=version".
        static readonly PublishedValue<string> Selected =
            new PublishedValue<string>("", "Yuki Material preview");

        public static string Key(YukiMaterial config, MaterialTarget target) =>
            (config != null ? config.id : "?") + "/" + (target != null ? target.id : "?");

        /// <summary>Reads the table and makes the caller re-run when it changes.</summary>
        public static void Observe(ComputeContext context) => context.Observe(Selected);

        static Dictionary<string, string> Parse(string text)
        {
            var table = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(text)) return table;
            foreach (var pair in text.Split('|'))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0) table[pair.Substring(0, eq)] = pair.Substring(eq + 1);
            }
            return table;
        }

        static string Write(Dictionary<string, string> table)
        {
            var parts = new List<string>();
            foreach (var pair in table) parts.Add(pair.Key + "=" + pair.Value);
            parts.Sort(System.StringComparer.Ordinal);
            return string.Join("|", parts.ToArray());
        }

        public static string Current(YukiMaterial config, MaterialTarget target)
        {
            string id;
            return Parse(Selected.Value).TryGetValue(Key(config, target), out id) ? id : null;
        }

        public static bool IsShowing(YukiMaterial config, MaterialTarget target, MaterialVariant variant) =>
            variant != null && Current(config, target) == variant.id;

        /// <summary>Null goes back to the version the avatar spawns with.</summary>
        public static void Show(YukiMaterial config, MaterialTarget target, MaterialVariant variant)
        {
            var table = Parse(Selected.Value);
            var key = Key(config, target);
            if (variant == null) table.Remove(key); else table[key] = variant.id;
            Selected.Value = Write(table);
        }

        public static void Clear() => Selected.Value = "";
    }
}
