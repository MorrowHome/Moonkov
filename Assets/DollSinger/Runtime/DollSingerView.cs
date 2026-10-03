using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace Unity.MP_FPS.DollSinger
{
/// <summary>
/// Local-player camera rig: third person orbit plus a Tarkov-style first person view.
///
/// The Doll Singer is a single SkinnedMeshRenderer with 13 submeshes, and the head is
/// spread over several of them (Face / EyeWhite / Brows / Eyes / Lashes / Glasses / Hair),
/// so only the owning camera temporarily uses a mesh without the head surfaces.
/// A full skinned shadow renderer preserves the original materials and silhouette.
/// </summary>
[DefaultExecutionOrder(50)]
public class DollSingerView : MonoBehaviour
{
    public Camera camera;
    public DollSingerInput input;
    public Transform viewPoint;

    public GameObject player;
    public bool followPlayerRotation = true;

    [Header("Local player view")]
    [Tooltip("Switches between third and first person. Tick it in the Play-mode Inspector " +
             "— that works where there is no middle mouse button — or press MMB in game.")]
    [SerializeField] private bool firstPerson;
    [Min(0.5f)] public float minThirdPersonDistance = 1.25f;
    [Min(0.5f)] public float maxThirdPersonDistance = 6f;
    [Min(0.1f)] public float scrollStep = 0.5f;
    [Min(0.01f)] public float viewBlendSeconds = 0.15f;
    [Min(0.01f)] public float zoomSmoothTime = 0.12f;
    [Range(40f, 100f)] public float firstPersonFieldOfView = 70f;
    [Range(0.01f, 0.5f)] public float firstPersonNearClip = 0.03f;

    [Header("First person head")]
    [Tooltip("Fallback eye position in front of the head bone, in character-local metres.")]
    public Vector3 fallbackEyeWorldOffset = new Vector3(0f, 0.08f, 0.05f);
    [Tooltip("Distance used to build the head look-at target along the camera forward.")]
    public float lookTargetDistance = 12f;
    [Tooltip("Moves the eye outside the neck opening as the view pitches down, in world metres.")]
    [Range(0f, 0.15f)] public float firstPersonEyeForward = 0.045f;
    [Tooltip("Minimum distance in front of the chest when looking fully down, in world metres.")]
    [Range(0.05f, 0.35f)] public float lookDownBodyClearance = 0.20f;

    private DollSingerMovement movement;
    private Transform eyeAnchor;
    private Transform chestAnchor;
    private Vector3 eyeLocalOffset;
    private SkinnedMeshRenderer bodyRenderer;
    private Material[] thirdPersonMaterials;
    private UnityEngine.Mesh thirdPersonMesh;
    private UnityEngine.Mesh firstPersonMesh;
    private SkinnedMeshRenderer fullShadowRenderer;
    private ShadowCastingMode thirdPersonShadowMode;
    private bool firstPersonPresentationActive;
    private bool renderingFirstPersonMesh;
    private float[] shadowBlendWeights;
    private readonly Stack<CameraMeshState> cameraMeshStates = new Stack<CameraMeshState>();
    private struct CameraMeshState
    {
        public Camera camera;
        public bool firstPersonMesh;
    }

    private Vector3 thirdPersonRestPosition;
    private Quaternion thirdPersonRestRotation;
    private float thirdPersonRestFov;
    private float thirdPersonRestNearClip;
    private bool cameraDefaultsCaptured;

    private float targetDistance;
    private float currentDistance;
    private float distanceVelocity;
    private float viewBlend;
    private float aimBlend;
    private bool appliedFirstPerson;
    private bool networkLookDriven;
    private Quaternion networkLookRotation;
    private Vector3 lastRenderedLeanOffset;

    private const string HairMaterialName = "Hair";
    // "Hair" is the scalp/bangs/side-hair submesh only. The twin-tails are skinned to the
    // shuangmawei bones and live in the DressDetail/InnerDress submeshes, so hiding this
    // submesh clears the view without losing the tails behind the camera.
    // "Jewel" is a small ornament sitting at eye level in front of the face.
    private static readonly string[] HeadSurfaceMaterials =
        { "Face", "EyeWhite", "Eyes", "Lashes", "Brows", "Glasses", HairMaterialName, "Jewel" };

    public bool IsFirstPerson => firstPerson;

    /// <summary>Point the head bone should look at, far along the camera's forward axis.</summary>
    public Vector3 LookPoint
    {
        get {
            Vector3 origin = camera ? camera.transform.position : transform.position;
            Vector3 forward = camera ? camera.transform.forward : transform.forward;
            return origin + forward * lookTargetDistance;
        }
    }

    private void Awake()
    {
        CaptureCameraDefaults();
        if (player) { BindPlayer(player, true); if (movement) movement.BindCamera(this); }
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
        RenderPipelineManager.endCameraRendering += EndCameraRendering;
        Camera.onPreCull += BeforeBuiltinCamera;
        Camera.onPostRender += AfterBuiltinCamera;
        if (player && !bodyRenderer) BindPlayer(player, true);
    }

    // Defaults are only ever read from the camera, never written before being read, so a
    // caller that binds us before Awake() cannot poison them (that bug blanked the world).
    private void CaptureCameraDefaults()
    {
        if (cameraDefaultsCaptured || !camera) return;
        thirdPersonRestPosition = camera.transform.localPosition;
        thirdPersonRestRotation = camera.transform.localRotation;
        thirdPersonRestFov = camera.fieldOfView;
        thirdPersonRestNearClip = camera.nearClipPlane;
        cameraDefaultsCaptured = true;
    }

    public void BindPlayer(GameObject character, bool usesThirdPerson)
    {
        ReleaseFirstPersonPresentation();
        if (movement) movement.IsFirstPersonView = false;
        CaptureCameraDefaults();

        player = character;
        followPlayerRotation = !usesThirdPerson;
        movement = usesThirdPerson && character ? character.GetComponent<DollSingerMovement>() : null;
        firstPerson = false;
        if (input) input.SetLeanEnabled(false);
        appliedFirstPerson = false;
        networkLookDriven = false;
        viewBlend = 0f;
        aimBlend = 0f;
        lastRenderedLeanOffset = Vector3.zero;
        distanceVelocity = 0f;

        ResolveEyeAnchor(character);
        BuildFirstPersonPresentation(character);

        if (!camera || !movement) return;
        camera.transform.localPosition = thirdPersonRestPosition;
        camera.transform.localRotation = thirdPersonRestRotation;
        camera.fieldOfView = thirdPersonRestFov;
        camera.nearClipPlane = thirdPersonRestNearClip;
        targetDistance = currentDistance = Mathf.Clamp(
            -thirdPersonRestPosition.z, minThirdPersonDistance, maxThirdPersonDistance);
    }

    public void SetAimBlend(float blend) => aimBlend = Mathf.Clamp01(blend);

    /// <summary>Keep the owned view independent of network prediction rotating its character parent.</summary>
    public void SetNetworkLookRotation(Quaternion rotation)
    {
        networkLookDriven = true;
        networkLookRotation = rotation;
        // Aim capture needs the current pose immediately, before the final camera update.
        transform.rotation = rotation;
    }

    public float LimitLeanAmount(float amount)
        => LimitLeanAmount(amount, player ? player.transform.rotation : Quaternion.identity);

    public float LimitLeanAmount(float amount, Quaternion bodyRotation)
    {
        if (!firstPerson || !movement || !camera || !player) return 0f;
        Vector3 desired = movement.GetLeanOffset(amount, bodyRotation);
        if (desired.sqrMagnitude < 0.000001f) return amount;
        Vector3 origin = camera.transform.position - lastRenderedLeanOffset;
        Vector3 allowed = DollSingerMovement.ConstrainLeanOffset(origin, desired, player.transform.root,
            ~LayerMask.GetMask("ServerPlayer"));
        if (allowed.sqrMagnitude >= desired.sqrMagnitude - 0.000001f) return amount;
        // Joint arcs are nonlinear: reduce the angle, rather than scaling a translation.
        float low = 0f, high = Mathf.Abs(amount);
        float sign = Mathf.Sign(amount);
        for (int i = 0; i < 8; i++)
        {
            float middle = (low + high) * 0.5f;
            if (movement.GetLeanOffset(sign * middle, bodyRotation).sqrMagnitude <= allowed.sqrMagnitude)
                low = middle;
            else high = middle;
        }
        return sign * low;
    }

    public Ray CaptureAimRay(Quaternion lookRotation, Quaternion bodyRotation, float lean)
    {
        SetNetworkLookRotation(lookRotation);
        if (!firstPerson || !movement) return camera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        // Input is sampled before LateUpdate: replace the previous rendered lean with the
        // current commanded displacement, so aiming uses this frame's sideways eye position.
        Vector3 offset = movement.GetLeanOffset(lean, bodyRotation);
        Vector3 origin = camera.transform.position - lastRenderedLeanOffset + offset;
        return new Ray(origin, lookRotation * Vector3.forward);
    }

    public void SetFirstPerson(bool enabled)
    {
        firstPerson = enabled;
        if (input) input.SetLeanEnabled(enabled);
        appliedFirstPerson = enabled;
        if (movement) movement.IsFirstPersonView = enabled;
    }

    // Right-click the component header in the Inspector to flip the view; handy where
    // there is no middle mouse button (remote desktops, trackpads).
    [ContextMenu("Toggle First Person View")]
    private void ToggleFirstPersonFromMenu() => SetFirstPerson(!firstPerson);

    private void Update()
    {
        if (!movement || !camera) return;

        // The flag is serialized so it can be flipped from the Play-mode Inspector; that
        // is the only way to switch view where there is no middle mouse button.
        if (firstPerson != appliedFirstPerson) SetFirstPerson(firstPerson);

        if (!input || !input.CanReadPlayerInput) return;

        if (input.ViewPressed)
            SetFirstPerson(!firstPerson);

        if (firstPerson) return;
        float scroll = input.Scroll;
        if (Mathf.Abs(scroll) > 0.01f)
            targetDistance = Mathf.Clamp(targetDistance - Mathf.Sign(scroll) * scrollStep,
                minThirdPersonDistance, maxThirdPersonDistance);
    }

    private void LateUpdate()
    {
        // Leave the full mesh available to Scene view and any camera that renders next.
        // Also recover if a camera render was interrupted before its post-render event.
        cameraMeshStates.Clear();
        SetCameraMesh(false);
        if (!player) return;

        if (followPlayerRotation)
        {
            transform.position = player.transform.position;
            transform.rotation = player.transform.rotation;
            SetFirstPersonPresentation(false);
            return;
        }

        if (!camera || !movement)
        {
            transform.position = player.transform.position;
            return;
        }

        // Prediction can rotate the parent after Update. Restore world-space look before
        // calculating the final camera offset and applying lean, rather than inheriting that turn.
        if (networkLookDriven) transform.rotation = networkLookRotation;

        float step = Time.deltaTime / Mathf.Max(0.01f, viewBlendSeconds);
        viewBlend = Mathf.MoveTowards(viewBlend, firstPerson ? 1f : 0f, step);
        currentDistance = Mathf.SmoothDamp(currentDistance, targetDistance,
            ref distanceVelocity, zoomSmoothTime);

        // First person rides the head bone so the camera follows head animation; the pivot
        // rotation itself comes from DollSingerMovement.CameraRotation().
        Vector3 pivot = player.transform.position;
        if (eyeAnchor) pivot = GetFirstPersonEyePosition();
        transform.position = Vector3.Lerp(player.transform.position, pivot, viewBlend);

        Vector3 restForward = thirdPersonRestRotation * Vector3.forward;
        float heightPerDistance = -restForward.y / Mathf.Max(0.01f, restForward.z);
        float restHeight = thirdPersonRestPosition.y +
            (currentDistance + thirdPersonRestPosition.z) * heightPerDistance;
        Vector3 rest = new Vector3(thirdPersonRestPosition.x, restHeight, -currentDistance);
        Vector3 aiming = new Vector3(0.65f, 1.58f,
            -Mathf.Max(minThirdPersonDistance, currentDistance - 1.45f));

        camera.transform.localPosition = Vector3.Lerp(Vector3.Lerp(rest, aiming, aimBlend),
            Vector3.zero, viewBlend);
        camera.transform.localRotation = Quaternion.Slerp(
            Quaternion.Slerp(thirdPersonRestRotation, Quaternion.identity, aimBlend),
            Quaternion.identity, viewBlend);
        camera.fieldOfView = Mathf.Lerp(Mathf.Lerp(thirdPersonRestFov, 39f, aimBlend),
            firstPersonFieldOfView, viewBlend);
        camera.nearClipPlane = Mathf.Lerp(thirdPersonRestNearClip, firstPersonNearClip, viewBlend);

        SetFirstPersonPresentation(viewBlend > 0.5f);
        SyncShadowBlendShapes();
        ApplyLean();
        lastRenderedLeanOffset = firstPerson ? movement.LeanOffset * viewBlend : Vector3.zero;
    }

    /// <summary>
    /// Rolls the camera and slides it sideways for the Q/E lean. The sideways slide is what
    /// lets you peek past cover; the roll is what sells the tilt. Runs after the pivot
    /// rotation has been written by DollSingerMovement.CameraRotation().
    /// </summary>
    private void ApplyLean()
    {
        if (!firstPerson || !movement) return;
        // Eye translation already comes from the leaning bones; only first-person roll remains.
        float roll = movement.LeanRoll;
        if (Mathf.Abs(roll) > 0.01f)
            transform.rotation = transform.rotation * Quaternion.Euler(0f, 0f, roll);
    }

    private Vector3 GetFirstPersonEyePosition()
    {
        Vector3 up = player.transform.up;
        Vector3 horizontalLook = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        if (horizontalLook.sqrMagnitude < 0.01f) horizontalLook = player.transform.forward;
        Vector3 eye = eyeAnchor.TransformPoint(eyeLocalOffset) + horizontalLook * firstPersonEyeForward;
        float down = Mathf.Clamp01(Vector3.Dot(transform.forward, -up));
        float clearanceBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.95f, down));
        if (chestAnchor && clearanceBlend > 0f)
        {
            float forwardDistance = Vector3.Dot(eye - chestAnchor.position, horizontalLook);
            // Push horizontally out of the torso, rather than down along the camera ray.
            // The original head-relative eye dives into the neckline at extreme pitch.
            eye += horizontalLook * (Mathf.Max(0f, lookDownBodyClearance - forwardDistance) * clearanceBlend);
        }
        return eye;
    }

    /// <summary>
    /// Finds the transform the first person camera should ride and the eye position inside it.
    ///
    /// The offset is always stored via InverseTransformPoint rather than hand-written: MMD rigs
    /// carry a 100x bone scale (Blender cm import), so a "local" offset typed in metres gets
    /// magnified a hundredfold by TransformPoint and throws the camera off the map.
    /// </summary>
    private void ResolveEyeAnchor(GameObject character)
    {
        eyeAnchor = null;
        chestAnchor = null;
        eyeLocalOffset = Vector3.zero;
        if (!character) return;

        var animator = character.GetComponentInChildren<Animator>();
        Transform head = null, eyeLeft = null, eyeRight = null;
        if (animator && animator.isHuman)
        {
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            chestAnchor = animator.GetBoneTransform(HumanBodyBones.UpperChest)
                ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            eyeLeft = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            eyeRight = animator.GetBoneTransform(HumanBodyBones.RightEye);
        }

        // Humanoid avatars imported from MMD rigs usually leave the eye slots unmapped, so
        // fall back to the standard MMD bone names before giving up on an exact eye position.
        if (!eyeLeft || !eyeRight) FindBonePair(character.transform, "目.L", "目.R", ref eyeLeft, ref eyeRight);
        if (!eyeLeft || !eyeRight) FindBonePair(character.transform, "eye_L", "eye_R", ref eyeLeft, ref eyeRight);
        if (!eyeLeft || !eyeRight) FindBonePair(character.transform, "Eye_L", "Eye_R", ref eyeLeft, ref eyeRight);

        eyeAnchor = head ? head : (eyeLeft ? eyeLeft : character.transform);

        Vector3 worldEye;
        if (eyeLeft && eyeRight) worldEye = (eyeLeft.position + eyeRight.position) * 0.5f;
        else if (head) worldEye = head.position + character.transform.rotation * fallbackEyeWorldOffset;
        else worldEye = character.transform.position + Vector3.up * 1.5f;

        eyeLocalOffset = eyeAnchor.InverseTransformPoint(worldEye);
    }

    private static void FindBonePair(Transform root, string leftName, string rightName,
        ref Transform left, ref Transform right)
    {
        Transform foundLeft = null, foundRight = null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == leftName) foundLeft = t;
            else if (t.name == rightName) foundRight = t;
            if (foundLeft && foundRight) break;
        }
        if (foundLeft && foundRight)
        {
            left = foundLeft;
            right = foundRight;
        }
    }

    private void BuildFirstPersonPresentation(GameObject character)
    {
        bodyRenderer = FindBodyRenderer(character);
        if (!bodyRenderer) return;

        thirdPersonMaterials = bodyRenderer.sharedMaterials;
        thirdPersonMesh = bodyRenderer.sharedMesh;
        thirdPersonShadowMode = bodyRenderer.shadowCastingMode;
        firstPersonMesh = BuildFirstPersonMesh(thirdPersonMesh, bodyRenderer.bones, thirdPersonMaterials);
        if (!firstPersonMesh) return;

        var shadowObject = new GameObject("FirstPersonFullShadow (Runtime)");
        shadowObject.transform.SetParent(bodyRenderer.transform, false);
        shadowObject.layer = bodyRenderer.gameObject.layer;
        fullShadowRenderer = shadowObject.AddComponent<SkinnedMeshRenderer>();
        fullShadowRenderer.enabled = false;
        fullShadowRenderer.sharedMesh = thirdPersonMesh;
        fullShadowRenderer.sharedMaterials = thirdPersonMaterials;
        fullShadowRenderer.bones = bodyRenderer.bones;
        fullShadowRenderer.rootBone = bodyRenderer.rootBone;
        fullShadowRenderer.localBounds = bodyRenderer.localBounds;
        fullShadowRenderer.quality = bodyRenderer.quality;
        fullShadowRenderer.updateWhenOffscreen = true;
        fullShadowRenderer.renderingLayerMask = bodyRenderer.renderingLayerMask;
        fullShadowRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        fullShadowRenderer.receiveShadows = false;
        shadowBlendWeights = new float[thirdPersonMesh.blendShapeCount];
        for (int i = 0; i < shadowBlendWeights.Length; i++) shadowBlendWeights[i] = float.NaN;
    }

    /// <summary>
    /// Copies the mesh without any triangle dominated by a "head clutter" bone. Camera-side
    /// clutter (the toushi chain net, scalp, bangs, side hair) sits within centimetres of the
    /// eye and blocks the view; the twin-tails are weighted to the shuangmawei bones and stay.
    /// Empty head submeshes instead of replacing their materials with a generic opaque
    /// ShadowCaster (which projected the eye/eyelash planes without their alpha masks).
    /// Clone the source mesh to retain all skinning, UV channels and blendshape indices.
    /// </summary>
    private static UnityEngine.Mesh BuildFirstPersonMesh(UnityEngine.Mesh source, Transform[] bones, Material[] materials)
    {
        if (!source || bones == null) return null;

        var boneWeights = source.boneWeights;
        var drop = new bool[source.vertexCount];
        for (int v = 0; v < source.vertexCount && v < boneWeights.Length; v++)
        {
            int boneIndex = boneWeights[v].boneIndex0;
            string boneName = boneIndex >= 0 && boneIndex < bones.Length && bones[boneIndex]
                ? bones[boneIndex].name : string.Empty;
            drop[v] = IsHeadClutterBone(boneName);
        }

        var copy = Instantiate(source);
        copy.name = source.name + " (FirstPerson camera only)";

        var kept = new System.Collections.Generic.List<int>();
        for (int s = 0; s < source.subMeshCount; s++)
        {
            if (s < materials.Length && materials[s] && IsHeadSurface(materials[s].name))
            {
                copy.SetTriangles(System.Array.Empty<int>(), s, false);
                continue;
            }
            var triangles = source.GetTriangles(s);
            kept.Clear();
            kept.Capacity = Mathf.Max(kept.Capacity, triangles.Length);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (drop[a] || drop[b] || drop[c]) continue;
                kept.Add(a);
                kept.Add(b);
                kept.Add(c);
            }
            copy.SetTriangles(kept, s, false);
        }
        copy.bounds = source.bounds;
        return copy;
    }

    private static bool IsHeadClutterBone(string boneName)
    {
        // toushi = 头饰: the gold chain net and rings on top of the head, packed into the
        // DressDetail submesh. toufa/liuhai/cefa/houfa are the scalp, bangs, side hair and
        // back hair — and the scalp skin (toufa.p) also drives a fine net shell that lives
        // in the *Dress* submesh (19020 verts, 95% of it) and renders as the grid-looking
        // hair net. None of these can be hidden by material swapping alone.
        // 頭/首 are deliberately NOT matched: those are face/neck skin, the face is already
        // hidden per material and dropping the neck leaves a hole when looking down.
        return boneName.StartsWith("toushi") || boneName.StartsWith("toufa")
            || boneName.StartsWith("liuhai") || boneName.StartsWith("cefa")
            || boneName.StartsWith("houfa");
    }

    private static SkinnedMeshRenderer FindBodyRenderer(GameObject character)
    {
        if (!character) return null;
        foreach (var renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (renderer.sharedMesh && renderer.sharedMesh.name == "DollSinger_Body")
                return renderer;
        // Fall back to the largest skinned mesh so other characters still work.
        SkinnedMeshRenderer best = null;
        int bestVertices = 0;
        foreach (var renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!renderer.sharedMesh) continue;
            if (renderer.sharedMesh.vertexCount <= bestVertices) continue;
            best = renderer;
            bestVertices = renderer.sharedMesh.vertexCount;
        }
        return best;
    }

    private static bool IsHeadSurface(string materialName)
    {
        materialName = materialName.Replace(" (Instance)", string.Empty).Trim();
        foreach (var candidate in HeadSurfaceMaterials)
            if (materialName == candidate) return true;
        return false;
    }

    private void SetFirstPersonPresentation(bool active)
    {
        if (!bodyRenderer || !firstPersonMesh || !fullShadowRenderer) return;
        firstPersonPresentationActive = active;
        // One full silhouette casts shadows in every view. Only the visible body changes
        // per camera; the shadow mesh always uses the unmodified source and lilToon alpha.
        bodyRenderer.shadowCastingMode = active ? ShadowCastingMode.Off : thirdPersonShadowMode;
        fullShadowRenderer.enabled = active && bodyRenderer.enabled && thirdPersonShadowMode != ShadowCastingMode.Off;
    }

    private void BeginCameraRendering(ScriptableRenderContext context, Camera renderingCamera) => BeforeCameraCull(renderingCamera);
    private void EndCameraRendering(ScriptableRenderContext context, Camera renderingCamera) => AfterCameraRender(renderingCamera);
    private void BeforeBuiltinCamera(Camera renderingCamera) { if (!GraphicsSettings.currentRenderPipeline) BeforeCameraCull(renderingCamera); }
    private void AfterBuiltinCamera(Camera renderingCamera) { if (!GraphicsSettings.currentRenderPipeline) AfterCameraRender(renderingCamera); }

    private void BeforeCameraCull(Camera renderingCamera)
    {
        if (!bodyRenderer || !firstPersonMesh) return;
        cameraMeshStates.Push(new CameraMeshState { camera = renderingCamera, firstPersonMesh = renderingFirstPersonMesh });
        SetCameraMesh(firstPersonPresentationActive && renderingCamera == camera);
    }

    private void AfterCameraRender(Camera renderingCamera)
    {
        if (!bodyRenderer || cameraMeshStates.Count == 0) return;
        if (cameraMeshStates.Peek().camera != renderingCamera)
        {
            cameraMeshStates.Clear();
            SetCameraMesh(false);
            return;
        }
        SetCameraMesh(cameraMeshStates.Pop().firstPersonMesh && firstPersonPresentationActive);
    }

    private void SetCameraMesh(bool firstPersonMeshActive)
    {
        if (!bodyRenderer || !thirdPersonMesh || !firstPersonMesh || renderingFirstPersonMesh == firstPersonMeshActive) return;
        bodyRenderer.sharedMesh = firstPersonMeshActive ? firstPersonMesh : thirdPersonMesh;
        renderingFirstPersonMesh = firstPersonMeshActive;
    }

    private void SyncShadowBlendShapes()
    {
        if (!fullShadowRenderer || !fullShadowRenderer.enabled) return;
        for (int i = 0; i < shadowBlendWeights.Length; i++)
        {
            float weight = bodyRenderer.GetBlendShapeWeight(i);
            if (shadowBlendWeights[i] == weight) continue;
            fullShadowRenderer.SetBlendShapeWeight(i, weight);
            shadowBlendWeights[i] = weight;
        }
    }

    private void ReleaseFirstPersonPresentation()
    {
        cameraMeshStates.Clear();
        if (bodyRenderer)
        {
            if (thirdPersonMesh) bodyRenderer.sharedMesh = thirdPersonMesh;
            bodyRenderer.shadowCastingMode = thirdPersonShadowMode;
        }
        renderingFirstPersonMesh = false;
        firstPersonPresentationActive = false;

        if (fullShadowRenderer)
        {
            fullShadowRenderer.enabled = false;
            DestroyRuntime(fullShadowRenderer.gameObject);
        }
        if (firstPersonMesh) DestroyRuntime(firstPersonMesh);

        bodyRenderer = null;
        thirdPersonMaterials = null;
        thirdPersonMesh = null;
        firstPersonMesh = null;
        fullShadowRenderer = null;
        shadowBlendWeights = null;
        eyeAnchor = null;
        chestAnchor = null;
    }

    private static void DestroyRuntime(Object target)
    {
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= EndCameraRendering;
        Camera.onPreCull -= BeforeBuiltinCamera;
        Camera.onPostRender -= AfterBuiltinCamera;
        ReleaseFirstPersonPresentation();
        if (movement) movement.IsFirstPersonView = false;
        firstPerson = false;
        appliedFirstPerson = false;
        networkLookDriven = false;
        viewBlend = 0f;
        aimBlend = 0f;
        lastRenderedLeanOffset = Vector3.zero;
        if (!camera || !cameraDefaultsCaptured) return;
        camera.transform.localPosition = thirdPersonRestPosition;
        camera.transform.localRotation = thirdPersonRestRotation;
        camera.nearClipPlane = thirdPersonRestNearClip;
        camera.fieldOfView = thirdPersonRestFov;
    }
}

}
