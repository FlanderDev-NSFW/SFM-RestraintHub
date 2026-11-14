using System;
using System.Collections.Generic;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace RestraintHub
{
    /// <summary>
    /// The game's particle effects on her bones (the climax squirt <c>ShiofukiParticle</c>, the pee, ...) simulate in world space,
    /// so each particle starts where its emitter is when Unity updates particle systems - after the animation, but before
    /// <c>LateUpdate</c>, where restraint mods pose her body (Hogtie, WallRestraint ...). Without help they start where the
    /// animation alone would put her. While a restraint is worn, each such emitter hangs under a helper object that copies its bone's
    /// <b>drawn</b> pose every frame in <c>Camera.onPreCull</c> (after every <c>LateUpdate</c>); the next particle update emits there
    /// (one frame later). On release the emitters go back to their bones. [observed 2026-11-14, docs/knowledge/patterns.md]
    /// </summary>
    internal static class EffectAnchors
    {
        private sealed class Moved
        {
            public Transform Effect, Bone, Helper;
            public Vector3 LocalPosition, LocalScale;
            public Quaternion LocalRotation;
        }

        private static readonly List<Moved> Effects = new List<Moved>();
        private static IntPtr _player;
        private static int _drawnFrame = -1;
        private static Camera.CameraCallback _preCull;

        internal static string Status => Effects.Count == 0 ? "off" : Effects.Count + " effects follow the drawn body";

        /// <summary>Once per frame in game (hub Update): on while something is worn.</summary>
        internal static void Tick(bool anyWorn)
        {
            var pc = PlayerController.Instance;
            IntPtr player = pc == null ? IntPtr.Zero : pc.Pointer;
            if (Effects.Count > 0 && (!anyWorn || player != _player)) Release();
            if (anyWorn && Effects.Count == 0 && pc != null && _player != player) Capture(pc);
            if (!anyWorn) _player = IntPtr.Zero;
        }

        private static void Capture(PlayerController pc)
        {
            _player = pc.Pointer; // once per wearing (and per player): no retry every frame if nothing is found
            var armature = pc.transform.Find("Manustia/Armature");
            if (armature == null) return;
            // Non-generic lookup: the generic GetComponentsInChildren<ParticleSystem> is not in the game build (IL2CPP) and
            // crashed the game [observed 2026-11-14].
            var found = armature.GetComponentsInChildren(Il2CppType.Of<ParticleSystem>(), true);
            foreach (var c in found)
            {
                var ps = c.TryCast<ParticleSystem>();
                if (ps == null || ps.main.simulationSpace != ParticleSystemSimulationSpace.World) continue;
                var t = ps.transform;
                var bone = t.parent;
                if (bone == null) continue;
                var helper = new GameObject("RestraintHub effect " + t.name);
                UnityEngine.Object.DontDestroyOnLoad(helper);
                var m = new Moved { Effect = t, Bone = bone, Helper = helper.transform, LocalPosition = t.localPosition, LocalRotation = t.localRotation, LocalScale = t.localScale };
                Follow(m);
                t.SetParent(helper.transform, false);
                t.localPosition = m.LocalPosition;
                t.localRotation = m.LocalRotation;
                t.localScale = m.LocalScale;
                Effects.Add(m);
            }
            if (Effects.Count == 0) return;
            if (_preCull == null)
            {
                _preCull = DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new Action<Camera>(PreCull));
                Camera.onPreCull = Camera.onPreCull == null ? _preCull : Il2CppSystem.Delegate.Combine(Camera.onPreCull, _preCull).Cast<Camera.CameraCallback>();
            }
            var names = new List<string>();
            foreach (var m in Effects) names.Add(m.Effect.name);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] effects: follow the drawn body (" + string.Join(", ", names) + ")");
        }

        /// <summary>The emitters back on their bones, the helpers gone.</summary>
        internal static void Release()
        {
            int n = 0;
            foreach (var m in Effects)
            {
                try
                {
                    if (m.Effect != null && m.Bone != null)
                    {
                        m.Effect.SetParent(m.Bone, false);
                        m.Effect.localPosition = m.LocalPosition;
                        m.Effect.localRotation = m.LocalRotation;
                        m.Effect.localScale = m.LocalScale;
                        n++;
                    }
                    if (m.Helper != null) UnityEngine.Object.Destroy(m.Helper.gameObject);
                }
                catch (Exception e) { HubController.Fail("effect " + m.Effect?.name, e); }
            }
            if (Effects.Count > 0) Plugin.Logger.LogInfo("[SFM-ACCEPT] effects: back on her bones (" + n + ")");
            Effects.Clear();
        }

        private static void Follow(Moved m)
        {
            if (m.Bone == null || m.Helper == null) return;
            m.Helper.SetPositionAndRotation(m.Bone.position, m.Bone.rotation);
            m.Helper.localScale = m.Bone.lossyScale;
        }

        private static void PreCull(Camera cam)
        {
            try
            {
                if (Effects.Count == 0 || _drawnFrame == Time.frameCount) return;
                _drawnFrame = Time.frameCount;
                foreach (var m in Effects) Follow(m);
            }
            catch (Exception e) { HubController.Fail("effects pre-cull", e); }
        }
    }
}
