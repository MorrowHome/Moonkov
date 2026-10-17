using System;
using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace Unity.MP_FPS
{
    /// <summary>Local state injection into the real network player's presentation, with four simultaneous views.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class WeaponGestureLab : MonoBehaviour
    {
        [SerializeField] private GameObject m_PlayerPrefab;
        [SerializeField] private WeaponData[] m_Weapons;
        [SerializeField] private UIDocument m_Document;
        [SerializeField] private Camera[] m_Observers;
        private readonly RenderTexture[] m_Targets = new RenderTexture[4];
        private readonly string[] m_CardNames = { "ownerCard", "frontCard", "sideCard", "backCard" };
        private static readonly float[] ObserverAngles = { 0, 90, 180 };
        private GameObject m_Stage;
        private DollSingerMovement m_Model;
        private DollSingerView m_View;
        private DollSingerHaloAim m_Halo;
        private Animator m_Animator;
        private VisualElement m_UI;
        private Label m_Status;
        private float m_Pitch, m_Orbit, m_Distance = 1.25f, m_Speed = 1, m_ReloadElapsed, m_NextShot;
        private float m_PreviousTimeScale;
        private CursorLockMode m_PreviousLock;
        private bool m_PreviousCursor, m_Aiming = true, m_FirstPerson = true, m_Repeating, m_Paused, m_Reloading, m_Ready;
        private int m_Ammo, m_WeaponIndex, m_LastStatusFrame;
        private uint m_ShotTick, m_ReloadTick;
        public GameObject Player { get; private set; }
        public DollSingerView PlayerView => m_View;
        public DollSingerHaloAim Halo => m_Halo;
        public Camera[] Observers => m_Observers;
        public bool Reloading => m_Reloading;
        public int Ammo => m_Ammo;
        public int WeaponIndex => m_WeaponIndex;
        private WeaponData Weapon => m_Weapons[m_WeaponIndex];

        public void Configure(GameObject prefab, WeaponData[] weapons, UIDocument document, Camera[] observers)
        { m_PlayerPrefab = prefab; m_Weapons = weapons; m_Document = document; m_Observers = observers; }

        private void Start()
        {
            if (!m_PlayerPrefab || !m_Document || m_Weapons == null || m_Weapons.Length != 4 || m_Observers.Length != 3)
                throw new InvalidOperationException("Weapon Gesture Lab needs the production player, four weapon assets and three observer cameras.");
            m_PreviousTimeScale = Time.timeScale;
            m_PreviousLock = Cursor.lockState; m_PreviousCursor = Cursor.visible;
            // Configure the official prefab before its gameplay callbacks can run.
            m_Stage = new GameObject("GestureLab Player Staging"); m_Stage.SetActive(false);
            Player = Instantiate(m_PlayerPrefab, Vector3.zero, Quaternion.identity, m_Stage.transform);
            Player.name = "GestureLab Actual Network Player";
            foreach (var b in Player.GetComponents<MonoBehaviour>()) b.enabled = false;
            foreach (var c in Player.GetComponentsInChildren<UnityEngine.CharacterController>(true)) c.enabled = false;
            foreach (var input in Player.GetComponentsInChildren<DollSingerInput>(true)) input.enabled = false;
            foreach (var camera in Player.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (var listener in Player.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            m_Model = Player.GetComponentInChildren<DollSingerMovement>(true);
            m_Halo = Player.GetComponentInChildren<DollSingerHaloAim>(true);
            m_View = Player.GetComponentInChildren<DollSingerView>(true);
            m_Animator = m_Model.GetComponent<Animator>();
            m_Model.gameObject.SetActive(true); m_View.gameObject.SetActive(true);
            m_Model.SetNetworkViewPresentation(true, Quaternion.identity, 0);
            m_Halo.SetNetworkPresentation(true, Vector3.forward, Vector3.forward * 50);
            m_Model.enabled = m_Halo.enabled = m_View.enabled = true;
            m_Stage.SetActive(true);
            m_View.BindPlayer(m_Model.gameObject, true); m_Model.BindCamera(m_View);
            m_Halo.view = m_View;
            m_View.SetFirstPerson(true); m_View.camera.enabled = true;
            m_Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            m_Animator.applyRootMotion = false;
            m_UI = m_Document.rootVisualElement;
            string[] imageNames = { "ownerImage", "frontImage", "sideImage", "backImage" };
            for (int i = 0; i < 4; i++)
            {
                var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32)
                { name = "GestureLab " + imageNames[i], antiAliasing = 1 };
                target.Create(); m_Targets[i] = target;
                var camera = i == 0 ? m_View.camera : m_Observers[i - 1];
                camera.targetTexture = target; camera.aspect = 16f / 9; camera.enabled = true;
                m_UI.Q<UnityEngine.UIElements.Image>(imageNames[i]).image = target;
                m_UI.Q<UnityEngine.UIElements.Image>(imageNames[i]).scaleMode = ScaleMode.ScaleToFit;
            }
            BindControls();
            m_Ready = true; SelectWeapon(0);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        private void BindControls()
        {
            string[] weaponNames = { "rifle", "revolver", "shotgun", "sniper" };
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                m_UI.Q<Button>(weaponNames[i]).clicked += () => SelectWeapon(index);
            }
            m_UI.Q<Button>("fire").clicked += Fire;
            m_UI.Q<Button>("reload").clicked += Reload;
            m_UI.Q<Toggle>("aim").RegisterValueChangedCallback(e => m_Aiming = e.newValue);
            m_UI.Q<Toggle>("repeatFire").RegisterValueChangedCallback(e => m_Repeating = e.newValue);
            m_UI.Q<Toggle>("pause").RegisterValueChangedCallback(e => SetPaused(e.newValue));
            m_UI.Q<Toggle>("firstPerson").RegisterValueChangedCallback(e =>
            {
                m_FirstPerson = e.newValue; m_View.SetFirstPerson(m_FirstPerson);
                m_UI.Q<Button>("focusOwner").text = m_FirstPerson ? "玩家相机 · 第一人称 / 点击放大" : "玩家相机 · 第三人称 / 点击放大";
            });
            m_UI.Q<Slider>("pitch").RegisterValueChangedCallback(e => m_Pitch = e.newValue);
            m_UI.Q<Slider>("orbit").RegisterValueChangedCallback(e => m_Orbit = e.newValue);
            m_UI.Q<Slider>("distance").RegisterValueChangedCallback(e => m_Distance = e.newValue);
            m_UI.Q<Slider>("speed").RegisterValueChangedCallback(e =>
            { m_Speed = e.newValue; if (!m_Paused) Time.timeScale = m_Speed; });
            string[] focusNames = { "focusOwner", "focusFront", "focusSide", "focusBack" };
            for (int i = 0; i < 4; i++)
            { int index = i; m_UI.Q<Button>(focusNames[i]).clicked += () => FocusView(index); }
            m_UI.Q<Button>("grid").clicked += () => FocusView(-1);
            m_Status = m_UI.Q<Label>("status");
        }

        public void FocusView(int index)
        {
            for (int i = 0; i < 4; i++) m_UI.Q(m_CardNames[i]).EnableInClassList("hidden", index >= 0 && index != i);
            m_UI.Q("topRow").EnableInClassList("hidden", index >= 2);
            m_UI.Q("bottomRow").EnableInClassList("hidden", index >= 0 && index < 2);
        }

        public void SelectWeapon(int index)
        {
            if (m_Paused) return;
            m_WeaponIndex = Mathf.Clamp(index, 0, 3); m_Ammo = Weapon.MagazineSize;
            m_Reloading = false; m_ReloadElapsed = 0; m_NextShot = Time.time;
            if (m_UI != null)
            {
                string[] names = { "rifle", "revolver", "shotgun", "sniper" };
                for (int i = 0; i < 4; i++) m_UI.Q<Button>(names[i]).EnableInClassList("selected", i == m_WeaponIndex);
            }
            if (m_Ready) PresentState();
        }

        public void Fire()
        {
            if (!m_Ready || m_Paused || !m_Aiming || m_Reloading || Time.time < m_NextShot) return;
            if (m_Ammo <= 0) { Reload(); return; }
            m_Ammo--; m_ShotTick++; m_NextShot = Time.time + Mathf.Max(.02f, Weapon.CooldownInMs);
            PresentState();
            m_Halo.PlayNetworkShot(m_View.camera.transform.position + m_View.camera.transform.forward * 50, false, m_ShotTick);
        }

        public void Reload()
        {
            if (!m_Ready || m_Paused || m_Reloading) return;
            m_Reloading = true; m_ReloadElapsed = 0; m_ReloadTick++;
            PresentState();
        }

        public void SetPaused(bool paused)
        {
            m_Paused = paused; Time.timeScale = paused ? 0 : m_Speed;
            // Keep the sampled pose intact; observer cameras and layout stay interactive.
            foreach (string name in new[] { "rifle", "revolver", "shotgun", "sniper", "aim", "fire", "reload", "repeatFire", "firstPerson", "pitch" })
                m_UI.Q(name).SetEnabled(!paused);
        }

        private void Update()
        {
            if (!m_Ready) return;
            var keyboard = Keyboard.current;
            bool typing = m_UI.focusController?.focusedElement is VisualElement focused && focused.ClassListContains("unity-text-input");
            if (keyboard != null && !typing)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) SelectWeapon(0);
                if (keyboard.digit2Key.wasPressedThisFrame) SelectWeapon(1);
                if (keyboard.digit3Key.wasPressedThisFrame) SelectWeapon(2);
                if (keyboard.digit4Key.wasPressedThisFrame) SelectWeapon(3);
                if (keyboard.spaceKey.wasPressedThisFrame) Fire();
                if (keyboard.rKey.wasPressedThisFrame) Reload();
                if (keyboard.pKey.wasPressedThisFrame) m_UI.Q<Toggle>("pause").value = !m_Paused;
            }
            if (m_Reloading)
            {
                m_ReloadElapsed += Time.deltaTime;
                if (m_ReloadElapsed >= Weapon.ReloadTime) { m_Reloading = false; m_Ammo = Weapon.MagazineSize; }
            }
            if (m_Repeating && !m_Paused) Fire();
            PresentState();
            if (Time.frameCount - m_LastStatusFrame >= 8)
            {
                m_LastStatusFrame = Time.frameCount;
                m_Status.text = $"{Weapon.WeaponName}   弹药 {m_Ammo}/{Weapon.MagazineSize}   " +
                    $"{(m_Reloading ? "换弹 " + Mathf.Clamp01(m_ReloadElapsed / Weapon.ReloadTime).ToString("P0") : m_Aiming ? "瞄准" : "待机")}   " +
                    $"速度 {(m_Paused ? 0 : m_Speed):0.0}×   |   正式 Prefab / Animator / IK / 相机；本地注入状态，不启动联机战斗";
            }
        }

        private void PresentState()
        {
            Quaternion look = Quaternion.Euler(m_Pitch, 0, 0);
            m_Model.ApplyNetworkPresentation(0, 0, true, false, false, Time.deltaTime);
            m_Model.SetNetworkViewPresentation(m_FirstPerson, look, 0);
            m_Halo.SetNetworkPresentation(m_Aiming, look * Vector3.forward, m_Model.transform.position + Vector3.up * 1.2f + look * Vector3.forward * 50);
            m_Halo.SetNetworkWeapon((DollSingerHaloAim.HaloWeapon)(m_WeaponIndex + 1), m_Ammo,
                m_Reloading, m_Reloading ? Mathf.Clamp01(m_ReloadElapsed / Mathf.Max(.01f, Weapon.ReloadTime)) : 0,
                m_ShotTick, m_ReloadTick, Weapon.MagazineSize, 0, Weapon.MagazineSize);
            m_View.SetNetworkLookRotation(look);
            m_View.SetAimBlend(m_Halo.AimBlend);
            m_View.SetWeaponAimMagnification(m_Halo.ScopeMagnification);
        }

        private void LateUpdate()
        {
            if (!m_Ready) return;
            Vector3 target = m_Model.transform.position + Vector3.up * 1.15f;
            for (int i = 0; i < 3; i++)
            {
                Vector3 direction = Quaternion.Euler(0, m_Orbit + ObserverAngles[i], 0) * Vector3.forward;
                m_Observers[i].transform.position = target + direction * m_Distance + Vector3.up * .1f;
                m_Observers[i].transform.LookAt(target);
            }
        }

        private void OnDisable()
        {
            if (!m_Ready) return;
            Time.timeScale = m_PreviousTimeScale;
            Cursor.lockState = m_PreviousLock; Cursor.visible = m_PreviousCursor;
        }

        private void OnDestroy()
        {
            if (m_View && m_View.camera) m_View.camera.targetTexture = null;
            for (int i = 0; i < m_Observers.Length; i++) if (m_Observers[i]) m_Observers[i].targetTexture = null;
            foreach (var target in m_Targets) if (target) { target.Release(); Destroy(target); }
            if (m_Stage) Destroy(m_Stage);
        }
    }
}
