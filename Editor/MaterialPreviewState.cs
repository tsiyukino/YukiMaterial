using System.Collections.Generic;
using nadena.dev.ndmf.preview;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// Which state each menu is being tried on in.
    ///
    /// A slot no menu drives is previewed as it will be built — there is only
    /// one answer, so asking would be pointless. A slot a menu drives shows the
    /// state the avatar spawns in, and "Try on" points the whole menu somewhere
    /// else, which moves every slot the menu drives at once: seeing another look
    /// is something you ask for rather than something that happens while you
    /// edit, the same deal as the wardrobe.
    /// </summary>
    internal static class MaterialPreviewState
    {
        // NDMF can only observe a value that compares by equality, so the table
        // is carried as text rather than a dictionary: "menu=state|menu=state".
        static readonly PublishedValue<string> Selected =
            new PublishedValue<string>("", "Yuki Material preview");

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

        /// <summary>The state this menu is being tried on in, or null for the
        /// one the avatar spawns in.</summary>
        public static MaterialState Current(YukiMaterialMenu menu)
        {
            if (menu == null) return null;
            string id;
            if (!Parse(Selected.Value).TryGetValue(menu.id, out id)) return null;
            return menu.FindState(id);
        }

        public static bool IsShowing(YukiMaterialMenu menu, MaterialState state)
        {
            return menu != null && state != null && Current(menu) == state;
        }

        /// <summary>Null goes back to the state the avatar spawns in.</summary>
        public static void Show(YukiMaterialMenu menu, MaterialState state)
        {
            if (menu == null) return;
            var table = Parse(Selected.Value);
            if (state == null) table.Remove(menu.id); else table[menu.id] = state.id;
            Selected.Value = Write(table);
        }

        public static void Clear() => Selected.Value = "";
    }
}
