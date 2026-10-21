using System;
using System.Reflection;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Uses temporary fixtures only; leaves the user's scenes and Play state untouched.
public sealed class MoonkovViewInteractionChecks : EditorWindow
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private MenuCharacterView portrait;
    public static string LastPreviewResult { get; private set; }

    [MenuItem("Tools/Moonkov/Check View Projection And Aim")]
    public static void CheckCamera()
    {
        var holder = new GameObject("Inactive view checks") { hideFlags = HideFlags.HideAndDontSave };
        holder.SetActive(false);
        // Keep camera matrices near the origin: reconstructing a ray from a .03 m
        // near plane at 10 km measures float cancellation rather than view direction.
        holder.transform.position = Vector3.up * 10f;
        DollSingerView view = null;
        try
        {
            var player = new GameObject("Model"); player.transform.SetParent(holder.transform, false);
            var model = player.AddComponent<DollSingerMovement>();
            var input = player.AddComponent<DollSingerInput>();
            var pivot = new GameObject("View"); pivot.transform.SetParent(holder.transform, false);
            var cameraObject = new GameObject("World camera"); cameraObject.transform.SetParent(pivot.transform, false);
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.aspect = 16f / 9f;
            camera.fieldOfView = 45; camera.transform.localRotation = Quaternion.Euler(12, 0, 0);
            camera.transform.localPosition = new Vector3(0, 2, -4);
            view = pivot.AddComponent<DollSingerView>(); view.camera = camera;
            view.BindPlayer(player, true); view.thirdPersonBackgroundBlur = false;
            var rotation = Quaternion.Euler(-17, 43, 0);
            float hipProjection = 0;
            foreach (float magnification in new[] { 1f, 1.75f, 6f, 8f })
            {
                float firstFov = 0, firstProjection = 0;
                foreach (bool first in new[] { true, false })
                {
                    view.SetFirstPerson(first);
                    Set(view, "viewBlend", first ? 1f : 0f);
                    view.SetAimBlend(magnification > 1 ? 1 : 0);
                    view.SetWeaponAimMagnification(magnification > 1 ? magnification : 0);
                    Set(view, "presentedWeaponMagnification", view.TargetAimMagnification);
                    view.SetNetworkLookRotation(rotation);
                    typeof(DollSingerView).GetMethod("LateUpdate", Private).Invoke(view, null);
                    var ray = view.CaptureAimRay(rotation, Quaternion.Euler(0, 43, 0), 0);
                    var screenRay = camera.ViewportPointToRay(new Vector3(.5f, .5f));
                    Require(Vector3.Distance(ray.direction, screenRay.direction) < .0001f &&
                        Vector3.Distance(ray.direction, rotation * Vector3.forward) < .0001f,
                        $"Camera/command centre directions differ at {magnification}x FP={first}: command={ray.direction}, screen={screenRay.direction}.");
                    float projection = camera.WorldToViewportPoint(camera.transform.position +
                        camera.transform.forward * 100 + camera.transform.right).x - .5f;
                    if (first) { firstFov = camera.fieldOfView; firstProjection = projection; }
                    else Require(Mathf.Abs(firstFov - camera.fieldOfView) < .0001f &&
                        Mathf.Abs(firstProjection - projection) < .00001f, "Perspective changed world projection.");
                    Require(Mathf.Abs(view.AimLookSensitivity - 1f / magnification) < .00001f,
                        "Projection sensitivity is missing or applied twice.");
                    if (magnification == 1) hipProjection = projection;
                    else Require(Mathf.Abs(projection / hipProjection - magnification) < .005f,
                        "Actual projected target size differs from the weapon magnification.");
                    Set(input, "<Look>k__BackingField", new Vector2(.008f, .008f));
                    var look = float2.zero; var free = float2.zero; float lean = 0;
                    DollSingerLookController.Update(ref look, ref free, ref lean, out _, input, model, view, false, 1f / 60f);
                    Require(math.distance(look, new float2(.008f, -.008f) / magnification) < .000001f,
                        "Micro aim was lost or scaled twice.");
                }
            }
            // Newly received aim blend must not change input scaling ahead of the image.
            view.SetAimBlend(0);
            Require(Mathf.Abs(view.AimLookSensitivity - .125f) < .00001f,
                "Input scaling ran ahead of the currently rendered 8x image.");
            Debug.Log("View checks passed: shared 70 degree world FOV, actual 1.75x/6x/8x projection in both perspectives, " +
                "command/centre ray agreement, rendered-zoom sensitivity and sub-degree input scaled once.");
        }
        finally { if (view) view.BindPlayer(null, true); DestroyImmediate(holder); }
    }

    [MenuItem("Tools/Moonkov/Check Portrait Drag")]
    public static void CheckPortrait()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run portrait checks outside Play mode.");
        LastPreviewResult = "running";
        var window = CreateInstance<MoonkovViewInteractionChecks>();
        window.titleContent = new GUIContent("Portrait regression check");
        window.position = new Rect(60, 60, 440, 560); window.Show();
        window.portrait = new MenuCharacterView(idleOnly: true);
        var image = window.portrait.Element;
        image.RemoveFromClassList("terminal-character-art"); image.style.width = 400; image.style.height = 500;
        window.rootVisualElement.Add(image);
        // A sibling equipment control overlays part of the same portrait rectangle.
        var button = new Button { name = "equipment" };
        button.style.position = Position.Absolute; button.style.left = 0; button.style.top = 0;
        button.style.width = 70; button.style.height = 70;
        window.rootVisualElement.Add(button);
        image.schedule.Execute(() => window.CheckPointerEvents(button)).StartingIn(250);
    }

    private void CheckPointerEvents(Button button)
    {
        try
        {
            var image = portrait.Element;
            portrait.Advance(0); portrait.Render();
            var scene = portrait.DisplayScene;
            Require(image.panel.Pick(button.worldBound.center) == button, "Portrait blocked an overlaid equipment button.");
            Vector2 start = image.worldBound.center, end = start + Vector2.right * 80;
            Down(image, start); Move(image, end);
            float yaw = portrait.InspectionYaw;
            Require(Mathf.Abs(yaw) > 1 && image.HasPointerCapture(PointerId.mousePointerId), "Drag did not rotate/capture the portrait.");
            Up(image, end); Move(image, end + Vector2.right * 50);
            portrait.Advance(.1f);
            Require(portrait.InspectionYaw == yaw && !image.HasPointerCapture(PointerId.mousePointerId), "Portrait moved after release.");
            Down(button, button.worldBound.center); Move(button, end); Up(button, end);
            Require(portrait.InspectionYaw == yaw, "Equipment gesture rotated the portrait.");
            Down(image, start);
            using (var cancel = PointerCancelEvent.GetPooled()) { cancel.target = image; image.SendEvent(cancel); }
            Require(!image.HasPointerCapture(PointerId.mousePointerId), "Cancelled pointer retained capture.");
            image.RemoveFromHierarchy();
            Require(portrait.InspectionYaw == 0 && !scene.isLoaded, "Detached portrait retained orientation or preview scene.");
            rootVisualElement.Insert(0, image); portrait.Advance(0);
            Require(portrait.InspectionYaw == 0, "Reopened portrait did not reset.");
            LastPreviewResult = "passed";
            Debug.Log("Portrait checks passed: pointer rotation, release stop, cancellation, overlaid equipment isolation, detach cleanup and reopen reset.");
        }
        catch (Exception ex) { LastPreviewResult = "failed: " + ex.Message; throw; }
        finally { Close(); }
    }
    private void OnDisable() { portrait?.Dispose(); portrait = null; }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("View checks: " + message); }
    private static void Down(VisualElement target, Vector2 at)
    { using (var evt = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = at })) target.SendEvent(evt); }
    private static void Move(VisualElement target, Vector2 at)
    { using (var evt = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = at })) target.SendEvent(evt); }
    private static void Up(VisualElement target, Vector2 at)
    { using (var evt = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = at })) target.SendEvent(evt); }
}
