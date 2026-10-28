using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor;

public static class SurvivalActionRuntimeChecks
{
    private static int checks;
    private static Camera fixtureCamera;
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }

    // Await continuations run before LateUpdate. Read the rendered pose in the
    // render callback itself, after Animator, twist followers and action playback.
    private static async Task Rendered(Action sample)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<ScriptableRenderContext, Camera> handler = null;
        handler = (_, camera) =>
        {
            if(fixtureCamera ? camera!=fixtureCamera : camera.cameraType!=CameraType.Game)return;
            RenderPipelineManager.endCameraRendering -= handler;
            try { sample(); completion.TrySetResult(true); }
            catch (Exception error) { completion.TrySetException(error); }
        };
        RenderPipelineManager.endCameraRendering += handler;
        try
        {
            if (await Task.WhenAny(completion.Task, Task.Delay(8000)) != completion.Task)
                throw new TimeoutException("The existing Editor did not render a frame.");
            await completion.Task;
        }
        finally { RenderPipelineManager.endCameraRendering -= handler; }
    }

    public static async Task<object> Run()
    {
        bool background = Application.runInBackground;
        Application.runInBackground = true;
        try { return await RunFixture(); }
        finally { Application.runInBackground = background; }
    }

    private static async Task<object> RunFixture()
    {
        checks = 0;
        Check(EditorApplication.isPlaying, "Use the single existing Editor in Play mode.");
        Check(SceneManager.GetActiveScene().path == "Assets/Scenes/MainMenu.unity", "Use the menu for this isolated fixture.");
        for (int i = 0; i < 40 && (!GameManager.Instance || GameManager.Instance.SoundSystem == null); i++) await Task.Delay(100);
        Check(GameManager.Instance && GameManager.Instance.SoundSystem != null, "Pooled game sound system is unavailable.");
        int initialFrame = Time.frameCount;
        await Rendered(() => { }); await Rendered(() => { });
        Check(Time.frameCount > initialFrame, "Player loop is not advancing.");
        var scene = SceneManager.CreateScene("Survival action runtime fixture");
        var results = new List<object>();
        var target = new RenderTexture(960, 720, 24);
        var image = new Texture2D(960, 720, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        Directory.CreateDirectory("LocalData/Actions/Runtime");
        GameObject root = null; RaidActionPresentation action = null;
        var library = MoonkovAudio.Library;
        SoundDef[] banks = { library.EatBite, library.DrinkSip, library.BottleOpen, library.PacketOpen, library.InjectorCap, library.InjectorPress };
        var clips = new HashSet<AudioClip>(banks.SelectMany(b => b.Clips));
        int Voices() => GameManager.Instance.SoundGameObjects.SoundGameObjectList.Count(s => s.m_AudioSource && s.m_AudioSource.isPlaying && clips.Contains(s.m_AudioSource.clip));
        bool Playing(SoundDef bank) => GameManager.Instance.SoundGameObjects.SoundGameObjectList.Any(s => s.m_AudioSource && s.m_AudioSource.isPlaying && bank.Clips.Contains(s.m_AudioSource.clip));
        var heard = new HashSet<SoundDef>();
        Action<ScriptableRenderContext,List<Camera>> observeAudio = (_,__) => { foreach (var bank in banks) if (Playing(bank)) heard.Add(bank); };
        RenderPipelineManager.endContextRendering += observeAudio;
        void Capture(Camera camera, string path)
        {
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 960, 720), 0, 0, false); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        try
        {
            var lightObject = new GameObject("Fixture key"); SceneManager.MoveGameObjectToScene(lightObject, scene);
            var key = lightObject.AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 2; key.transform.rotation = Quaternion.Euler(35, -25, 0);
            var fillObject = new GameObject("Fixture fill"); SceneManager.MoveGameObjectToScene(fillObject, scene);
            var fill = fillObject.AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = 1; fill.transform.rotation = Quaternion.Euler(-25, 150, 0);
            var observerObject=new GameObject("Fixture observer");SceneManager.MoveGameObjectToScene(observerObject,scene);var observer=observerObject.AddComponent<Camera>();observer.enabled=false;observer.scene=scene;observer.aspect=4f/3;observer.fieldOfView=40;observer.nearClipPlane=.015f;observer.farClipPlane=10;observer.cullingMask=~(1<<LayerMask.NameToLayer("FirstPersonOverlay"));observer.backgroundColor=new Color(.10f,.12f,.14f);observer.clearFlags=CameraClearFlags.SolidColor;
            foreach (string rig in RaidActionAnimationSetup.Characters)
            {
                root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/{rig}/Prefabs/{rig}NetworkPlayer.prefab"));
                SceneManager.MoveGameObjectToScene(root, scene); root.transform.position += Vector3.one * 1000;
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
                var animator = root.GetComponentInChildren<Animator>(true);
                for (var node = animator.transform; node; node = node.parent) node.gameObject.SetActive(true);
                animator.enabled = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var view = root.GetComponentInChildren<DollSingerView>(true); view.gameObject.SetActive(true); view.enabled = true;
                view.BindPlayer(animator.gameObject, true); view.SetFirstPerson(true); view.SetNetworkLookRotation(Quaternion.identity);
                typeof(DollSingerView).GetField("viewBlend", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(view, 1f);
                view.camera.enabled = true; view.camera.scene = scene; view.camera.aspect = 4f / 3;
                fixtureCamera=view.camera;
                view.camera.backgroundColor = new Color(.10f, .12f, .14f); view.camera.clearFlags = CameraClearFlags.SolidColor;
                view.camera.farClipPlane = 10; view.camera.useOcclusionCulling = false;
                if (view.GestureCamera) view.GestureCamera.scene = scene;
                var twist = animator.GetComponent<HumanoidTwistFollowers>(); if (twist) twist.enabled = true;
                var halo = root.GetComponentInChildren<DollSingerHaloAim>(true);
                if(halo)halo.enabled=true;
                action = animator.gameObject.AddComponent<RaidActionPresentation>(); action.Configure(animator, view, halo, rig);
                var blendField=typeof(RaidActionPresentation).GetField("blend",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                var authored = JsonUtility.FromJson<DollSingerActionAnimation.Library>(Resources.Load<TextAsset>("Moonkov/Actions/" + rig).text);
                var hierarchy = animator.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                uint sequence = 1;
                foreach (var kind in new[] { HandAction.Search, HandAction.Eat, HandAction.Drink, HandAction.Inject })
                {
                    heard.Clear();
                    view.SetNetworkLookRotation(kind == HandAction.Search ? Quaternion.Euler(55, 0, 0) : Quaternion.identity);
                    string code = kind == HandAction.Eat ? "ration" : kind == HandAction.Drink ? "water" : kind == HandAction.Inject ? "medkit" : "";
                    var state = new PredictedPlayerGhost { CurrentHealth = 100, HandAction = (byte)kind, HandActionSequence = sequence++, HandActionItem = code, HandActionDuration = 3, HandActionRemaining = 3 };
                    action.Present(state, true, true, false); await Task.Delay(150); await Rendered(() => { });
                    for(int wait=0;wait<40&&(float)blendField.GetValue(action)<.999f;wait++)await Rendered(()=>{});
                    Check((float)blendField.GetValue(action)>=.999f,rig+" action did not finish its entry blend");
                    state.HandActionRemaining = 2.22f; action.Present(state, true, true, false);
                    // The sound pool applies LateUpdate requests on its next Update.
                    await Task.Delay(65);
                    await Rendered(() =>
                    {
                        if (kind == HandAction.Search) return;
                        var open = kind == HandAction.Eat ? library.PacketOpen : kind == HandAction.Drink ? library.BottleOpen : library.InjectorCap;
                        Check(heard.Contains(open), rig + " opening cue did not reach an AudioSource: " + kind);
                    });
                    int before = Voices(); action.Present(state, true, true, false);
                    await Rendered(() => Check(Voices() <= before, "Duplicate snapshot added a voice."));
                    var motion = authored.motions.First(m => m.action == kind.ToString() && (kind == HandAction.Search || m.items != null && m.items.Contains(code)));
                    state.HandActionRemaining = 3 * (1 - Mathf.Max(.52f, motion.contact + .02f)); action.Present(state, true, true, false);
                    await Task.Delay(65);
                    Transform[] poseNodes=null;Vector3[] posePositions=null;Quaternion[] poseRotations=null;
                    int capturedFrame=0;float capturedPhase=0;
                    await Rendered(() =>
                    {
                        if (kind != HandAction.Search)
                        {
                            var contact = kind == HandAction.Eat ? library.EatBite : kind == HandAction.Drink ? library.DrinkSip : library.InjectorPress;
                            Check(heard.Contains(contact), rig + " contact cue did not play: " + kind);
                            Check(action.ContactError < .04f, rig + " runtime contact miss " + kind + " " + action.ContactError);
                        }
                        Check(kind == HandAction.Search ? !view.GestureHandsActive : view.GestureHandsActive && view.GestureCamera.enabled, "Incorrect owner hand camera for " + kind);
                        float sample = action.SampledPhase * (motion.frames - 1); int from = Mathf.FloorToInt(sample), to = Mathf.Min(from + 1, motion.frames - 1);
                        foreach (string bone in new[] { "ひじ.R", "手捩.R", "手首.R", "手捩1.R", "手捩2.R", "手捩3.R", "人指１.R", "親指１.R" })
                        {
                            int index = Array.IndexOf(authored.bones, bone);
                            Quaternion Read(int f) { int p = (f * authored.bones.Length + index) * 4; return new Quaternion(motion.rotations[p], motion.rotations[p + 1], motion.rotations[p + 2], motion.rotations[p + 3]); }
                            float error=Quaternion.Angle(hierarchy[bone].localRotation, Quaternion.Slerp(Read(from), Read(to), sample - from));
                            Check(error < .12f, rig + " player loop overwrote authored bone " + bone + " " + kind+" angle="+error+" blend="+blendField.GetValue(action));
                        }
                        poseNodes=root.GetComponentsInChildren<Transform>(true);posePositions=poseNodes.Select(t=>t.localPosition).ToArray();poseRotations=poseNodes.Select(t=>t.localRotation).ToArray();capturedFrame=Time.frameCount;capturedPhase=action.SampledPhase;
                    });
                    // Re-render the recorded pose outside SRP's callback. Nested
                    // SubmitRenderRequest calls are unsupported by the pipeline.
                    for(int node=0;node<poseNodes.Length;node++){poseNodes[node].localPosition=posePositions[node];poseNodes[node].localRotation=poseRotations[node];}
                    string path=$"LocalData/Actions/Runtime/{rig}-{kind}.png";Capture(view.camera,path);
                    var head=animator.GetBoneTransform(HumanBodyBones.Head);observer.transform.position=head.position+new Vector3(kind==HandAction.Inject?-.55f:.55f,-.18f,1.0f);observer.transform.LookAt(head.position+Vector3.down*.20f);Capture(observer,$"LocalData/Actions/Runtime/{rig}-{kind}-observer.png");
                    results.Add(new{rig,action=kind.ToString(),frame=capturedFrame,phase=capturedPhase,screenshot=path});
                    action.Present(default, true, true, false); await Task.Delay(200);
                    await Rendered(() => Check(!action.OwnsHands && (!view.GestureHandsActive || halo && halo.OwnsHandPose) && (!halo || !halo.ExternalHandPoseActive) && root.GetComponentsInChildren<RaidActionProp>(true).All(p=>!p.gameObject.activeInHierarchy) && Voices() == 0, "Cancel/death retained a prop, hands or cue."));
                }
                action.DisposePresentation(); action = null; UnityEngine.Object.Destroy(root); root = null; await Task.Delay(50);
            }
            File.WriteAllText("LocalData/Actions/Runtime/Checks.txt", "PASS " + checks);
            return new { checks, result = "PASS actual player loop, saved bone playback, contacts, distinct pooled cues, duplicate snapshots, camera and cancellation", samples = results };
        }
        finally
        {
            RenderPipelineManager.endContextRendering -= observeAudio;
            fixtureCamera=null;
            if (action) action.DisposePresentation(); if (root) UnityEngine.Object.Destroy(root);
            RenderTexture.active = previous; target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(image);
            await SceneManager.UnloadSceneAsync(scene);
        }
    }
}
