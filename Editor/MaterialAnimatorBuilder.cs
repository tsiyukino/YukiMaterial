using System;
using TsiYuki.Core.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// The FX layer that switches one menu's states: one state of the machine
    /// per state of the menu, each swapping the material of every slot the menu
    /// drives — however many objects those slots are spread across.
    ///
    /// Every state writes every slot, so write-defaults off is safe and either
    /// convention works.
    /// </summary>
    public static class MaterialAnimatorBuilder
    {
        public static AnimatorController Build(ResolvedMenu menu, Transform avatarRoot,
                                               Func<ResolvedSlot, ResolvedState, UnityEngine.Material> materialFor,
                                               string name, Action<UnityEngine.Object> persist)
        {
            var controller = new AnimatorController { name = name };
            persist(controller);
            controller.AddParameter(menu.ParameterName, AnimatorControllerParameterType.Int);

            var machine = AnimatorGraph.AddLayer(controller, menu.ParameterName, persist);

            int y = 0;
            foreach (var state in menu.States)
            {
                var clip = new AnimationClip { name = name + " " + state.Value };
                bool any = false;

                foreach (var slot in menu.Slots)
                {
                    var material = materialFor(slot, state);
                    if (material == null) continue;

                    var path = AnimationUtility.CalculateTransformPath(slot.Renderer.transform, avatarRoot);
                    AnimationUtility.SetObjectReferenceCurve(clip,
                        EditorCurveBinding.PPtrCurve(path, slot.Renderer.GetType(), "m_Materials.Array.data[" + slot.Slot + "]"),
                        new[] { new ObjectReferenceKeyframe { time = 0, value = material } });
                    any = true;
                }
                if (!any) continue;
                persist(clip);

                var node = AnimatorGraph.AddState(machine, state.Value.ToString(), clip, new Vector3(400, y), persist);
                y += 60;

                AnimatorGraph.AddAnyStateTransition(machine, node, menu.ParameterName, state.Value, persist);
                if (state == menu.Default)
                {
                    // 0 is what VRChat resets a parameter to, so it has to mean
                    // the state the avatar spawns in.
                    machine.defaultState = node;
                    AnimatorGraph.AddAnyStateTransition(machine, node, menu.ParameterName, 0, persist);
                }
            }
            return controller;
        }
    }
}
