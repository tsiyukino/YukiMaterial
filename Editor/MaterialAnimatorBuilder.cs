using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// The FX controller that switches material versions: one layer per slot,
    /// one state per version, each state swapping the renderer's material.
    ///
    /// Every state writes the one property its layer touches, so write-defaults
    /// off is safe and either convention works.
    /// </summary>
    public static class MaterialAnimatorBuilder
    {
        public static AnimatorController Build(IEnumerable<ResolvedTarget> targets, Transform avatarRoot,
                                               Func<ResolvedTarget, MaterialVariant, UnityEngine.Material> materialFor,
                                               string name, Action<UnityEngine.Object> persist)
        {
            var controller = new AnimatorController { name = name };
            persist(controller);

            foreach (var target in targets)
            {
                controller.AddParameter(target.ParameterName, AnimatorControllerParameterType.Int);

                var machine = AddLayer(controller, target.ParameterName, persist);
                var path = AnimationUtility.CalculateTransformPath(target.Renderer.transform, avatarRoot);
                int y = 0;

                foreach (var variant in target.Variants)
                {
                    var material = materialFor(target, variant);
                    if (material == null) continue;

                    var clip = new AnimationClip { name = name + " " + target.DisplayName + " " + variant.value };
                    AnimationUtility.SetObjectReferenceCurve(clip,
                        EditorCurveBinding.PPtrCurve(path, target.Renderer.GetType(), "m_Materials.Array.data[" + target.Slot + "]"),
                        new[] { new ObjectReferenceKeyframe { time = 0, value = material } });
                    persist(clip);

                    var state = AddState(machine, variant.value.ToString(), clip, new Vector3(400, y), persist);
                    y += 60;

                    AddEnter(machine, state, variant.value, target.ParameterName, persist);
                    if (variant == target.Default)
                    {
                        // 0 is what VRChat resets a parameter to, so it has to
                        // mean the version the avatar spawns with.
                        machine.defaultState = state;
                        AddEnter(machine, state, 0, target.ParameterName, persist);
                    }
                }
            }
            return controller;
        }

        static AnimatorStateMachine AddLayer(AnimatorController controller, string name, Action<UnityEngine.Object> persist)
        {
            var machine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            persist(machine);
            controller.AddLayer(new AnimatorControllerLayer { name = name, defaultWeight = 1, stateMachine = machine });
            return machine;
        }

        static AnimatorState AddState(AnimatorStateMachine machine, string name, Motion motion, Vector3 position, Action<UnityEngine.Object> persist)
        {
            var state = new AnimatorState { name = name, motion = motion, writeDefaultValues = false, hideFlags = HideFlags.HideInHierarchy };
            persist(state);
            machine.AddState(state, position);
            return state;
        }

        static void AddEnter(AnimatorStateMachine machine, AnimatorState state, int value, string parameter, Action<UnityEngine.Object> persist)
        {
            var transition = machine.AddAnyStateTransition(state);
            transition.canTransitionToSelf = false;
            transition.hasExitTime = false;
            transition.duration = 0;
            transition.AddCondition(AnimatorConditionMode.Equals, value, parameter);
            persist(transition);
        }
    }
}
