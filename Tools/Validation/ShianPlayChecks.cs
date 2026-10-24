using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

// Run with Unity Pipeline run_script after entering ShianDemo in the one Editor.
// Drives only local character intent; no transport, server, client or gameplay session.
public static class ShianPlayChecks
{
    [Serializable] private sealed class Report
    {
        public int startFrame, endFrame, stanceChanges, menuSwitches;
        public bool networkSessionStarted, firstPersonGestureArms, warehousePortraitLifecycle, equipmentPresentation, passed;
        public Vector3 helmetSizeMetres;
        public float walkDistanceMetres, standingTailHeightMetres, landedTailHeightMetres, fallAirflow, fallCover;
        public string failure;
    }
    private const string Output = "Temp/ShianValidation";
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
        Require(Application.isPlaying && SceneManager.GetActiveScene().name == "ShianDemo", "Open and play the isolated Shian demo first.");
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
            float tailHeight = spring.hairRoots.SelectMany(t => t.GetComponentsInChildren<Transform>()).Where(t => t.childCount == 0)
                .Max(t => root.transform.InverseTransformPoint(t.position).y);
            Require(!float.IsNaN(tailHeight) && !float.IsInfinity(tailHeight), "Invalid hair while standing.");
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
            tailHeight = spring.hairRoots.SelectMany(t => t.GetComponentsInChildren<Transform>()).Where(t => t.childCount == 0)
                .Max(t => root.transform.InverseTransformPoint(t.position).y);
            Require(!float.IsNaN(tailHeight) && !float.IsInfinity(tailHeight), "Invalid hair after landing.");
            report.landedTailHeightMetres = tailHeight;
            // Check the real selection event, stage replacement, and inventory portrait lifecycle.
            using (var menu = new MenuCharacterView(idleOnly: true))
            {
                foreach (int character in new[] { 2, 3, 4, 2, 4 })
                {
                    GameSettings.Instance.PlayerCharacter = character;
                    menu.Advance(1f / 60); menu.Render(new Vector2(800, 1000));
                    Require(menu.CharacterIndex == character && menu.Target && menu.DisplayScene.IsValid(), "Menu switch did not rebuild the selected operator.");
                }
                await InspectMenu(menu, report);
            }
            var parent = new VisualElement();
            using (var inventory = new ContainerInventoryView(parent, true, command => { }))
            {
                inventory.Show();
                Require(parent.Q<Image>("operatorLivePortrait") != null, "Shian warehouse portrait missing.");
                GameSettings.Instance.PlayerCharacter = 2;
                Require(parent.Q<Image>("operatorLivePortrait") == null, "Dollsinger retained the Shian portrait.");
                GameSettings.Instance.PlayerCharacter = 4;
                Require(parent.Q<Image>("operatorLivePortrait") != null, "Shian portrait did not return.");
                inventory.Suspend();
                Require(parent.Q<Image>("operatorLivePortrait") == null, "Hidden inventory retained its live portrait.");
            }
            report.menuSwitches = 5; report.warehousePortraitLifecycle = true;
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
    private static async Task InspectMenu(MenuCharacterView menu, Report report)
    {
        // Validate a settled preview as well as the first frame after switching.
        for (int i = 0; i < 120; i++) { menu.Advance(1f / 60); await Frames(1); }
        menu.Render(new Vector2(800, 1000)); Save(menu.Target, "menu-shian");
        var inventory = InventoryGraph.Create(false, false);
        foreach (var entry in new[] { ("Helmet", EnergyShieldEquipment.HeadDomeCode), ("Shield", EnergyShieldEquipment.ShieldCode), ("Backpack", "backpack") })
            inventory.Items.Add(new InventoryItem { Id = "shian-check-" + entry.Item1, Code = entry.Item2, Parent = InventoryCatalog.Equipment, Region = entry.Item1 });
        menu.SetProtectionInventory(inventory);
        for (int i = 0; i < 40; i++) { menu.Advance(1f / 60); await Frames(1); }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var protection = (EnergyShieldPresentation)typeof(MenuCharacterView).GetField("m_Protection", flags).GetValue(menu);
        Require(protection, "Equipment presentation was not created.");
        var size = (Vector3)typeof(EnergyShieldPresentation).GetField("m_VisorSize", flags).GetValue(protection);
        Require(size.x > .08f && size.x < .26f && size.y > .08f && size.y < .34f, "Helmet fit includes unrelated long-hair geometry: " + size);
        foreach (string field in new[] { "m_Visor", "m_Collar", "m_Device", "m_Subspace" })
            Require((Transform)typeof(EnergyShieldPresentation).GetField(field, flags).GetValue(protection), "Equipment geometry missing: " + field);
        menu.Render(new Vector2(800, 1000)); Save(menu.Target, "menu-shian-equipped");
        report.helmetSizeMetres = size; report.equipmentPresentation = true;
        menu.SetProtectionInventory(null);
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
