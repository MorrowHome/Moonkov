using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class SoundLifecycleChecks
{
    [MenuItem("Tools/Moonkov/Check Sound Lifecycle")]
    public static string Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        for (int scenario = 0; scenario < 3; scenario++)
        {
            var pool = new GameObject("Temporary sound pool");
            var owner = new GameObject("Temporary sound owner");
            var source = new GameObject("Temporary source").AddComponent<AudioSource>();
            var sound = new SoundGameObject(pool, new SoundGameObject.AudioComponentData { ASource = source });
            var emitter = new SoundEmitter(null);
            try
            {
                sound.Enable(emitter); sound.SetParent(owner.transform, Vector3.zero);
                sound.Kill(new SoundEmitter(null));
                Check(ReferenceEquals(typeof(SoundGameObject).GetField("m_Emitter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sound), emitter),
                    "Another emitter released an owned sound.");
                if (scenario == 1) Object.DestroyImmediate(pool);
                if (scenario == 2) Object.DestroyImmediate(source.gameObject);
                sound.Kill(emitter);
                int count = 0; bool fatal = false;
                Check(!sound.Update(emitter, 0, ref count, ref fatal), "A released sound remained active.");
                if (scenario == 0) Check(source.transform.parent == pool.transform, "A live sound did not return to its pool.");
                if (scenario == 1) Check(source && source.transform.parent == owner.transform, "A missing pool moved or destroyed its still-owned source.");
                sound.Kill(emitter);
                Check(true, "Repeated release failed.");
            }
            finally
            {
                if (owner) Object.DestroyImmediate(owner);
                if (pool) Object.DestroyImmediate(pool);
                if (source) Object.DestroyImmediate(source.gameObject);
            }
        }
        return $"PASS {checks} sound lifecycle checks: normal return, destroyed pool/source and repeated release.";
    }
}
