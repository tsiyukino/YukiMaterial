using TsiYuki.Core.Editor;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// UI strings (Localization/&lt;code&gt;.txt) for both the editor UI and NDMF's
    /// error report window.
    /// </summary>
    public static class MaterialText
    {
        public const string Package = "moe.tsiyuki.material";
        public static readonly YukiLocalizer L = new YukiLocalizer(Package);

        public static readonly YukiNdmfReport Errors = new YukiNdmfReport(L);
    }
}
