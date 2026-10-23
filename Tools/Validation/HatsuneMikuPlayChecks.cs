using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

// Run with Unity Pipeline run_script after entering HatsuneMikuDemo in the one Editor.
// Drives only local character intent; no transport, server, client or gameplay session.
public static class HatsuneMikuPlayChecks
{
    [Serializable] private sealed class Report
    {
        public int startFrame, endFrame, stanceChanges, menuSwitches;
        public bool networkSessionStarted, firstPersonGestureArms, warehousePortraitLifecycle, passed;
        public float walkDistanceMetres, standingTailHeightMetres, landedTailHeightMetres, fallAirflow, fallCover;
        public string failure;
    }
    private const string Output = "Temp/HatsuneMikuValidation";
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static void Input(DollSingerInput input, string property, object value) =>
        typeof(DollSingerInput).GetProperty(property).SetValue(input, value);
    private static async Task Frames(int count)
    {
        int end = Time.frameCount + count;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (Time.frameCount < end)
        {
            Require(Application.isPlaying, "Play mode ended during validation.");
            Require(DateTime.UtcNow < deadline, "The player loop stopped advancing.");
            await Task.Delay(16);
        }
    }

    public static async Task<string> Run()
    {
        Require(Application.isPlaying && SceneManager.GetActiveScene().name == "HatsuneMikuDemo", "Open and play the isolated Miku demo first.");
        foreach (var world in Unity.Entities.World.All)
            Require(!world.IsClient() && !world.IsServer(), "Do not run this check with multiplayer worlds.");
        Directory.CreateDirectory(Output);
        var report = new Report { startFrame = Time.frameCount, networkSessionStarted = false };
        var movement = Object.FindAnyObjectByType<DollSingerMovement>();
        Require(movement, "Local character missing.");
        var root = movement.gameObject;
        var input = root.GetComponent<DollSingerInput>();
        var view = root.GetComponentInChildren<DollSingerView>(true);
        var halo = root.GetComponent<DollSingerHaloAim>();
        var spring = root.GetComponent<SecondaryBoneSpring>();
        var avoidance = root.GetComponent<SkirtHandAvoidance>();
        var bones = root.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        int previousCharacter = GameSettings.Instance.PlayerCharacter;
        bool inputEnabled = input.enabled;
        input.enabled = false;
        // Stop hardware polling while retaining the same local ownership gate.
        typeof(DollSingerInput).GetMethod("SetCapture", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(input, new object[] { true });
        try
        {
            // Start on the demo's open floor each run, clear of its rail/step obstacles.
            var startingController = root.GetComponent<CharacterController>();
            startingController.enabled = false;
            root.transform.SetPositionAndRotation(new Vector3(0, -4.4f, 0), Quaternion.identity);
            startingController.enabled = true;
            view.SetFirstPerson(false);
            await Frames(30);
            Require(movement.IsGrounded, "Demo character did not reach the floor.");
            var before = root.transform.position;
            Input(input, "Move", Vector2.up);
            await Frames(50);
            Input(input, "Move", Vector2.zero);
            float travelled = Vector3.ProjectOnPlane(root.transform.position - before, Vector3.up).magnitude;
            Require(travelled > .25f, "Movement input did not move the character.");
            report.walkDistanceMetres = travelled;
            await Frames(30);
            foreach (var stance in new[] { DollSingerStance.Crouching, DollSingerStance.Prone, DollSingerStance.Tiptoe, DollSingerStance.Standing })
            {
                Input(input, "Stance", stance); await Frames(20);
                Require(movement.Stance == stance, "Stance did not change: " + stance);
            }
            report.stanceChanges = 4;
            // Let the prone-to-standing transient settle before checking idle length.
            await Frames(120);
            float tailHeight = new[] { "Left_BS_hair_end", "Right_BS_hair_end" }
                .Max(name => root.transform.InverseTransformPoint(bones[name].position).y);
            Require(tailHeight < .65f, "Tail curled while standing: " + tailHeight);
            report.standingTailHeightMetres = tailHeight;
            Capture(view.camera, "play-third-person");
            view.SetFirstPerson(true); await Frames(30);
            Capture(view.camera, "play-first-person");
            halo.editorForceAim = true;
            await Frames(30);
            Require(halo.AimBlend > .9f && view.GestureHandsActive, "First-person gesture arms did not activate.");
            Capture(view.camera, "play-first-person-rifle");
            halo.EditorPreviewFire(); await Frames(8);
            report.firstPersonGestureArms = true;
            halo.editorForceAim = false; await Frames(25);
            view.SetFirstPerson(false);
            var controller = root.GetComponent<CharacterController>();
            controller.enabled = false; root.transform.position += Vector3.up * 5; controller.enabled = true;
            float maxAir = 0, maxCover = 0;
            int fallStart = Time.frameCount;
            while (Time.frameCount - fallStart < 200)
            {
                await Frames(2);
                maxAir = Mathf.Max(maxAir, spring.AirflowBlend);
                maxCover = Mathf.Max(maxCover, avoidance.CoverBlend);
                if (maxAir > .3f && maxCover > .3f) break;
            }
            Require(maxAir > .3f && maxCover > .3f, "Falling did not activate skirt airflow/cover.");
            Capture(view.camera, "play-fall-cover");
            report.fallAirflow = maxAir; report.fallCover = maxCover;
            await Frames(160);
            Require(movement.IsGrounded, "Character did not land.");
            await Frames(100);
            tailHeight = new[] { "Left_BS_hair_end", "Right_BS_hair_end" }
                .Max(name => root.transform.InverseTransformPoint(bones[name].position).y);
            Require(tailHeight < .65f, "Tail did not settle after landing: " + tailHeight);
            report.landedTailHeightMetres = tailHeight;
            // Check the real selection event, stage replacement, and inventory portrait lifecycle.
            using (var menu = new MenuCharacterView(idleOnly: true))
            {
                foreach (int character in new[] { 2, 3, 2, 3 })
                {
                    GameSettings.Instance.PlayerCharacter = character;
                    menu.Advance(1f / 60); menu.Render(new Vector2(800, 1000));
                    Require(menu.CharacterIndex == character && menu.Target && menu.DisplayScene.IsValid(), "Menu switch did not rebuild the selected operator.");
                }
                Save(menu.Target, "menu-miku");
            }
            var parent = new VisualElement();
            using (var inventory = new ContainerInventoryView(parent, true, command => { }))
            {
                inventory.Show();
                Require(parent.Q<Image>("operatorLivePortrait") != null, "Miku warehouse portrait missing.");
                GameSettings.Instance.PlayerCharacter = 2;
                Require(parent.Q<Image>("operatorLivePortrait") == null, "Dollsinger retained the Miku portrait.");
                GameSettings.Instance.PlayerCharacter = 3;
                Require(parent.Q<Image>("operatorLivePortrait") != null, "Miku portrait did not return.");
                inventory.Suspend();
                Require(parent.Q<Image>("operatorLivePortrait") == null, "Hidden inventory retained its live portrait.");
            }
            report.menuSwitches = 4; report.warehousePortraitLifecycle = true;
            report.endFrame = Time.frameCount; report.passed = true;
            return JsonUtility.ToJson(report, true);
        }
        catch (Exception exception) { report.passed = false; report.failure = exception.ToString(); throw; }
        finally
        {
            Input(input, "Move", Vector2.zero); Input(input, "Stance", DollSingerStance.Standing);
            halo.editorForceAim = false; input.enabled = inputEnabled;
            GameSettings.Instance.PlayerCharacter = previousCharacter;
            File.WriteAllText(Output + "/play-checks.json", JsonUtility.ToJson(report, true));
        }
    }
    private static void Capture(Camera camera, string name)
    {
        var target = new RenderTexture(1280, 720, 24);
        var previous = camera.targetTexture;
        try { camera.targetTexture = target; camera.Render(); Save(target, name); }
        finally { camera.targetTexture = previous; target.Release(); Object.Destroy(target); }
    }
    private static void Save(RenderTexture target, string name)
    {
        var previous = RenderTexture.active;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; Object.Destroy(pixels); }
    }
}
