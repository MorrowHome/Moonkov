using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Preserves an actor and its owned visual attachments through the environment grade.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class MoonSilverPrintSubject : MonoBehaviour
    {
        private static readonly List<MoonSilverPrintSubject> Subjects = new List<MoonSilverPrintSubject>();
        private readonly List<Renderer> renderers = new List<Renderer>();
        private float refreshAt;

        public static void Protect(GameObject root)
        {
            if (!root.TryGetComponent<MoonSilverPrintSubject>(out var subject))
                subject = root.AddComponent<MoonSilverPrintSubject>();
            subject.Refresh();
        }

        public void Refresh()
        {
            renderers.Clear();
            GetComponentsInChildren(true, renderers);
            refreshAt = Time.realtimeSinceStartup + .5f;
        }

        private void OnEnable()
        {
            if (!Subjects.Contains(this)) Subjects.Add(this);
            Refresh();
        }

        private void OnDisable() => Subjects.Remove(this);
        private void OnTransformChildrenChanged() => Refresh();

        internal static void Collect(Camera camera, Plane[] frustum, List<Renderer> destination)
        {
            destination.Clear();
            for (int i = Subjects.Count - 1; i >= 0; i--)
            {
                var subject = Subjects[i];
                if (!subject) { Subjects.RemoveAt(i); continue; }
                // Covers late equipment changes below bones without scanning the scene.
                if (Time.realtimeSinceStartup >= subject.refreshAt) subject.Refresh();
                foreach (var renderer in subject.renderers)
                {
                    if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                        renderer.forceRenderingOff || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly ||
                        (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0 ||
                        !GeometryUtility.TestPlanesAABB(frustum, renderer.bounds)) continue;
                    if (!destination.Contains(renderer)) destination.Add(renderer);
                }
            }
        }
    }
}
