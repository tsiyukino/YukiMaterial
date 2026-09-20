using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;

[assembly: ExportsPlugin(typeof(TsiYuki.Materials.Editor.MaterialPlugin))]

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// Applies every YukiMaterial component's edits to the build copy of the
    /// avatar and removes the components.
    ///
    /// Runs in Transforming, after Modular Avatar, so the slot being patched is
    /// whatever MA finally assigned; that is still before the Optimizing passes
    /// (NonToon conversion, Avatar Optimizer) which read the materials.
    /// </summary>
    public class MaterialPlugin : Plugin<MaterialPlugin>
    {
        public override string QualifiedName => "moe.tsiyuki.material";
        public override string DisplayName => "Yuki Material";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .Run("Apply material edits", Execute);
        }

        static void Execute(BuildContext ctx)
        {
            var configs = ctx.AvatarRootObject.GetComponentsInChildren<YukiMaterial>(true);
            if (configs.Length == 0) return;

            var set = MaterialSet.Resolve(ctx.AvatarRootTransform);

            foreach (var model in set.Models)
                foreach (var warning in model.Warnings)
                    Report(ErrorSeverity.NonFatal, warning.Key, warning.Context, warning.Args);

            // Two components patching one slot: the first in hierarchy order wins
            // and the rest are dropped, so the result never depends on ordering
            // we do not control.
            var claimed = new HashSet<string>();
            foreach (var conflict in set.Conflicts())
                Report(ErrorSeverity.Error, conflict.Key, conflict.Context, conflict.Args);

            // Renderer -> slot -> patched material.
            var patches = new Dictionary<Renderer, Dictionary<int, UnityEngine.Material>>();
            int count = 0;

            foreach (var target in set.AllTargets)
            {
                if (!claimed.Add(target.Key)) continue;

                var missing = new List<string>();
                var patched = MaterialDiff.Build(target.Original, target.Edit,
                    target.Original.name + " (" + target.Config.gameObject.name + ")",
                    name => missing.Add(name));
                if (patched == null) continue;

                foreach (var name in missing.Distinct())
                    Report(ErrorSeverity.NonFatal, "warn.missing_property", target.Original, new object[] { target.DisplayName, name });

                if (!patches.TryGetValue(target.Renderer, out var slots))
                    patches[target.Renderer] = slots = new Dictionary<int, UnityEngine.Material>();
                slots[target.Slot] = patched;
                ctx.AssetSaver.SaveAsset(patched);
                count++;
            }

            foreach (var pair in patches)
            {
                var materials = pair.Key.sharedMaterials;
                foreach (var slot in pair.Value)
                    if (slot.Key >= 0 && slot.Key < materials.Length) materials[slot.Key] = slot.Value;
                pair.Key.sharedMaterials = materials;
            }

            foreach (var config in configs)
                if (config != null) Object.DestroyImmediate(config);

            if (count > 0) Debug.Log($"[Yuki Material] Patched {count} material slot(s).");
        }

        static void Report(ErrorSeverity severity, string key, Object context, object[] args)
        {
            // Strings fill the message; a trailing Unity object becomes a
            // clickable reference in NDMF's error window.
            var all = (args ?? new object[0]).Select(a => (object)(a?.ToString() ?? "")).ToList();
            if (context != null) all.Add(context);
            ErrorReport.ReportError(MaterialText.Ndmf, severity, key, all.ToArray());
        }
    }
}
