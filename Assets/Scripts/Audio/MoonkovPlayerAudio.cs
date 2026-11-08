using UnityEngine;
using Unity.MP_FPS.DollSinger;

namespace Unity.MP_FPS
{
    // Observes rendered client state, outside the prediction replay loop.
    public sealed class MoonkovPlayerAudio
    {
        private bool m_Initialized, m_WasLootSearching;
        private SoundSystem.SoundInfo m_LootSound;
        private readonly MoonkovMeleeAudio m_Melee = new MoonkovMeleeAudio();
        private readonly MoonkovPainAudio m_Pain = new MoonkovPainAudio();
        private SoundSystem.SoundInfo m_PainSound, m_GroanSound, m_CoughSound;
        private double m_NextCough;
        private SoundSystem.SoundInfo m_JumpVoice;
        private readonly MoonkovDistressAudio m_Distress = new MoonkovDistressAudio();
        private readonly MoonkovReloadAudio m_Reload = new MoonkovReloadAudio();
        private uint m_JumpTick, m_LandTick, m_ImpactTick, m_LootSequence, m_ConsumableSequence, m_Weapon;
        private int m_WeaponSlot;
        private Unity.Collections.FixedString64Bytes m_WeaponItem;
        private bool m_CanStep;
        private float m_LastMovementTime = float.NegativeInfinity;
        private Vector3 m_Position;
        private SoundDef m_Surface;
        private DollSingerStance m_Stance;
        private float m_TurnTravel, m_Yaw, m_GearAt;
        private float m_MoveSpeed, m_TurnSpeed;
        private readonly MoonkovSurfaceFoley m_Wall = new MoonkovSurfaceFoley();
        private UnityEngine.CharacterController m_Capsule;
        private SoundSystem.SoundInfo m_WallSound, m_MovementSound, m_StanceSound, m_ReloadSound;
        private SoundSystem.SoundInfo m_WeaponSound;
        private float m_LastStepTime = -1, m_HeartAt;
        private readonly MoonkovBreathingAudio m_Breath = new MoonkovBreathingAudio();
        private SoundSystem.SoundInfo m_BreathSound, m_HeartSound;
        private SoundSystem.SoundInfo m_OxygenSupplySound;
        private float m_OxygenSupplyRetryAt;
        private float m_FallSpeed;
        private float m_HitAt;
        private readonly RaycastHit[] m_SurfaceHits = new RaycastHit[8];

        public static bool NewTick(uint tick, ref uint last)
        {
            if (tick == 0 || last != 0 && unchecked((int)(tick - last)) <= 0) return false;
            last = tick;
            return true;
        }

        // Presentation gain only. Posture caps preserve quiet movement; AI hearing is separate.
        public static float FootstepVolume(float speed, DollSingerStance stance)
        {
            if (!float.IsFinite(speed) || speed <= 0) return 0;
            if (stance != DollSingerStance.Standing)
            {
                float cap = stance == DollSingerStance.Tiptoe ? .2f : stance == DollSingerStance.Crouching ? .38f : .65f;
                return cap * Mathf.Clamp01(speed / (2 * DollSingerStances.SpeedScale(stance)));
            }
            // Walk at 2 m/s; retain the full running mix at the production sprint speed.
            return speed <= 2 ? .7f * speed / 2 : Mathf.Lerp(.7f, 1, Mathf.InverseLerp(2, 5.335f, speed));
        }

        public static float TurnVolume(float degreesPerSecond) => float.IsFinite(degreesPerSecond) ?
            .65f * Mathf.Clamp01(degreesPerSecond / 180) : 0;

        public void Update(in PredictedPlayerGhost ghost, Transform player, bool owned, float deltaTime)
        {
            m_CanStep = false;
            var library = MoonkovAudio.Library;
            if (library == null) return;
            Vector3 position = player.position;
            bool cry = m_Pain.Observe(ghost.LastImpactTick, ghost.CurrentHealth > 0, Time.unscaledTime);
            bool jumping = m_Initialized && ghost.LastJumpTick != 0 &&
                unchecked((int)(ghost.LastJumpTick - m_JumpTick)) > 0;
            if (ghost.TacticalCoughSeconds <= 0 || ghost.CurrentHealth <= 0 || ghost.ClashEnabled)
            {
                GameManager.Instance?.SoundSystem?.Stop(m_CoughSound, .08f); m_CoughSound = null;
            }
            bool cough = m_Initialized && ghost.CurrentHealth > 0 && !cry && m_PainSound?.Handle?.IsValid() != true &&
                RaidCoughSound.TryEmit(ref m_NextCough, Time.unscaledTime, ghost.TacticalCoughSeconds, ghost.ClashEnabled);
            bool effort = m_Melee.Update(ghost, position, owned,
                !cry && !cough && m_PainSound?.Handle?.IsValid() != true && m_CoughSound?.Handle?.IsValid() != true);
            if (cry || cough || effort)
            { GameManager.Instance?.SoundSystem?.Stop(m_JumpVoice, .06f); m_JumpVoice = null; }
            bool jumpVoice = jumping && !cry && !cough && !effort && m_PainSound?.Handle?.IsValid() != true &&
                m_CoughSound?.Handle?.IsValid() != true && !m_Melee.VoicePlaying && m_JumpVoice?.Handle?.IsValid() != true;
            var reload = m_Reload.Observe(ghost.LastReloadTick, ghost.EquippedWeaponID, ghost.CurrentAmmo,
                ghost.ControllerState.IsReloadingState, ghost.CurrentHealth > 0);
            var reloadWeapon = WeaponManager.Instance ? WeaponManager.Instance.WeaponRegistry.GetWeaponData(ghost.EquippedWeaponID, ghost.EquippedModules) : null;
            bool stagedReload = reloadWeapon && DollSingerWeapons.IsHalo(ghost.EquippedWeaponID);
            var reloadPhase = m_Reload.ObservePhase(ghost, reloadWeapon);
            if (!m_Initialized)
            {
                m_Initialized = true;
                m_JumpTick = ghost.LastJumpTick;
                m_LandTick = ghost.LastLandTick; m_ImpactTick = ghost.LastImpactTick;
                m_LootSequence = ghost.LootSearchSequence;m_WasLootSearching=ghost.LootSearching;
                m_ConsumableSequence = ghost.ConsumableUseSequence;
                m_Weapon = ghost.EquippedWeaponID; m_WeaponSlot = ghost.EquippedWeaponSlot;
                m_WeaponItem = ghost.EquippedWeaponItem; m_Position = position;
                m_HeartAt = Time.unscaledTime + 1;
                m_Stance = ghost.ControllerState.Stance; m_Yaw = ((Quaternion)ghost.ControllerState.CurrentRotation).eulerAngles.y;
                m_Capsule = player.GetComponent<UnityEngine.CharacterController>();
                return; // A late join must not replay historical events.
            }
            if (ghost.CurrentHealth <= 0) { Stop(); return; }
            if ((reload & ReloadAudioEvent.Stop) != 0)
            {
                GameManager.Instance?.SoundSystem?.Stop(m_ReloadSound, .08f);
                m_ReloadSound = null;
            }
            if (stagedReload && reloadPhase != ReloadSoundPhase.None)
            {
                var window = MoonkovReloadAudio.Window(reloadWeapon.WeaponReloadSfx, reloadPhase);
                GameManager.Instance?.SoundSystem?.Stop(m_ReloadSound, .015f);
                m_ReloadSound = MoonkovAudio.PlayWeaponLayer(reloadWeapon.WeaponReloadSfx, position, 1,
                    owned ? .08f : 1, 0, window.x, window.y);
            }
            else if (!stagedReload && (reload & ReloadAudioEvent.Start) != 0)
            {
                var weapon = WeaponManager.Instance.WeaponRegistry.GetWeaponData(ghost.EquippedWeaponID);
                m_ReloadSound = MoonkovAudio.PlayWeaponLayer(weapon?.WeaponReloadSfx, position, 1, owned ? .08f : 1,
                    MoonkovReloadAudio.PitchForDuration(weapon?.WeaponReloadSfx, ghost.ReloadTimer));
            }
            if ((reload & ReloadAudioEvent.Ready) != 0) LocalOrWorld(library.ReloadReady, position, owned, .9f);
            if (NewTick(ghost.LastJumpTick, ref m_JumpTick)) LocalOrWorld(library.Jump, position, owned, 1);
            if (jumpVoice) m_JumpVoice = MoonkovAudio.PlayWeaponLayer(library.JumpEffort, position + Vector3.up * 1.4f,
                owned ? 1f : .7f, owned ? .05f : 1, follow: player);
            bool searchEnded=m_WasLootSearching && !ghost.LootSearching;
            if(searchEnded)
            {
                GameManager.Instance?.SoundSystem?.Stop(m_LootSound,.08f);m_LootSound=null;
            }
            m_WasLootSearching=ghost.LootSearching;
            if (NewTick(ghost.LootSearchSequence, ref m_LootSequence) && !searchEnded && ghost.CurrentHealth>0)
            {
                GameManager.Instance?.SoundSystem?.Stop(m_LootSound,.04f);
                m_LootSound=LocalOrWorld(library.LootSearchWorld,position,owned);
            }
            if (NewTick(ghost.ConsumableUseSequence, ref m_ConsumableSequence) && !owned && ghost.CurrentHealth > 0)
                LocalOrWorld(library.Cloth, position, false);
            if (NewTick(ghost.LastLandTick, ref m_LandTick))
            {
                SampleSurface(player, library);
                float landingLoad = RaidFootsteps.LoadMultiplier(ghost.CarriedWeight, ghost.ClashEnabled);
                LocalOrWorld(m_Surface, position, owned, Mathf.Lerp(.65f, 1.2f, Mathf.Clamp01(m_FallSpeed / 10)) * landingLoad);
                LocalOrWorld(library.Land, position, owned, Mathf.Lerp(.3f, .8f, Mathf.Clamp01(m_FallSpeed / 10)) * landingLoad);
                m_MovementSound = LocalOrWorld(library.GearMotion, position, owned, Mathf.Lerp(.2f, .4f, Mathf.Clamp01(m_FallSpeed / 10)) * landingLoad);
                m_LastStepTime = Time.unscaledTime; m_FallSpeed = 0;
            }
            if (NewTick(ghost.LastImpactTick, ref m_ImpactTick) && Time.unscaledTime >= m_HitAt)
            {
                LocalOrWorld(library.Hit, position, owned, owned ? 1f : .65f);
                m_HitAt = Time.unscaledTime + .35f;
            }
            bool groan = m_Distress.Observe(ghost.CurrentHealth / Mathf.Max(1, ghost.MaxHealth),
                ghost.CurrentHealth > 0, MoonkovDistressAudio.PainfulStatus(ghost),
                cry || cough || jumpVoice || m_JumpVoice?.Handle?.IsValid() == true ||
                m_CoughSound?.Handle?.IsValid() == true || m_Melee.VoicePlaying, Time.unscaledTime);
            if (!m_Distress.Active || cry || cough || effort || jumpVoice)
            {
                GameManager.Instance?.SoundSystem?.Stop(m_GroanSound, .12f); m_GroanSound = null;
            }
            if (cry)
            {
                GameManager.Instance?.SoundSystem?.Stop(m_CoughSound, .06f); m_CoughSound = null;
                m_PainSound = LocalOrWorld(library.PlayerPain, position, owned, owned ? 1 : .85f);
            }
            else if (cough)
                m_CoughSound = MoonkovAudio.PlayWeaponLayer(library.TacticalCough, position + Vector3.up * 1.4f,
                    owned ? 1 : .85f, owned ? .05f : 1, follow: player);
            else if (groan)
            {
                GameManager.Instance?.SoundSystem?.Stop(m_GroanSound, .08f);
                m_GroanSound = MoonkovAudio.PlayWeaponLayer(library.PlayerGroan, position + Vector3.up * 1.4f,
                    owned ? 1 : .7f, owned ? .08f : 1, follow: player);
            }
            if (owned && (cry || cough || groan || effort || jumpVoice))
            {
                GameManager.Instance?.SoundSystem?.Stop(m_BreathSound, .12f); m_BreathSound = null;
            }
            if (m_Weapon != ghost.EquippedWeaponID || ghost.EquippedWeaponID != DollSingerWeapons.None &&
                (m_WeaponSlot != ghost.EquippedWeaponSlot || !m_WeaponItem.Equals(ghost.EquippedWeaponItem)))
            {
                GameManager.Instance?.SoundSystem?.Stop(m_WeaponSound, .03f);
                m_WeaponSound = MoonkovAudio.PlayWeaponLayer(ghost.EquippedWeaponID == DollSingerWeapons.None ?
                    library.WeaponStow : ghost.EquippedWeaponID == DollSingerWeapons.Revolver ? library.WeaponDrawPistol : library.WeaponDraw,
                    position, owned ? 1 : .8f, owned ? 0 : 1, follow: player);
            }
            if (owned || m_OxygenSupplySound != null) UpdateOxygenSupply(ghost, library, position, owned);
            // Item-use feedback comes from acknowledged inventory actions, never passive regeneration.
            if (owned && ghost.CurrentHealth > 0)
            {
                var breath = MoonkovOxygenFeedback.Breath(ghost);
                // Exhaustion replaces the calm cycle promptly, while pain still takes priority.
                if (breath.Exerted && m_BreathSound != null && m_BreathSound.SoundDefinition == library.SuitBreath)
                { GameManager.Instance?.SoundSystem?.Stop(m_BreathSound, .06f); m_BreathSound = null; }
                if (!breath.Audible && m_BreathSound != null)
                {
                    GameManager.Instance?.SoundSystem?.Stop(m_BreathSound, .3f);
                    m_BreathSound = null;
                }
                bool voiceBusy = m_PainSound?.Handle?.IsValid() == true || m_GroanSound?.Handle?.IsValid() == true ||
                    m_CoughSound?.Handle?.IsValid() == true || m_BreathSound?.Handle?.IsValid() == true ||
                    m_Melee.VoicePlaying || m_JumpVoice?.Handle?.IsValid() == true || cry || cough || groan;
                if (m_Breath.Observe(breath, Time.unscaledTime, voiceBusy))
                {
                    var bank = breath.Exerted && library.SuitBreathEffort ? library.SuitBreathEffort : library.SuitBreath;
                    m_BreathSound = MoonkovAudio.PlayWeaponLayer(bank, position, breath.Volume, 0, breath.Pitch);
                }
                if (ghost.CurrentHealth < ghost.MaxHealth * .28f && Time.unscaledTime >= m_HeartAt)
                {
                    m_HeartSound = LocalOrWorld(library.Heartbeat, position, true, .6f);
                    m_HeartAt = Time.unscaledTime + 1.25f;
                }
            }
            else if (owned) Stop();
            else StopBreathing();
            if (ghost.CurrentHealth > 0 && m_Stance != ghost.ControllerState.Stance)
            {
                m_StanceSound = LocalOrWorld(library.StanceChange, position, owned,
                    ghost.ControllerState.Stance == DollSingerStance.Prone || m_Stance == DollSingerStance.Prone ? .85f : .5f);
                m_TurnTravel = 0;
            }
            m_Stance = ghost.ControllerState.Stance;
            m_Weapon = ghost.EquippedWeaponID;
            m_WeaponSlot = ghost.EquippedWeaponSlot; m_WeaponItem = ghost.EquippedWeaponItem;
            m_FallSpeed = Mathf.Max(m_FallSpeed, -ghost.ControllerState.JumpFallSpeed);

            var travel = position - m_Position;
            travel.y = 0f;
            m_Position = position;
            bool grounded = ghost.ControllerState.MovementType == FirstPersonController.MovementType.Standing;
            float yaw = ((Quaternion)ghost.ControllerState.CurrentRotation).eulerAngles.y;
            float turn = Mathf.Abs(Mathf.DeltaAngle(m_Yaw, yaw)); m_Yaw = yaw;
            if (!grounded || ghost.CurrentHealth <= 0f || !float.IsFinite(deltaTime) || deltaTime <= 0f ||
                deltaTime > .25f || !float.IsFinite(travel.sqrMagnitude) || travel.sqrMagnitude > 4f)
            {
                m_LastMovementTime = float.NegativeInfinity;
                m_MoveSpeed = m_TurnSpeed = 0;
                m_TurnTravel = 0; m_Wall.Reset();
                return;
            }
            // Sample rendered displacement, not requested sprint speed. Smooth across simulation snapshots.
            float response = 1 - Mathf.Exp(-deltaTime / .12f);
            m_MoveSpeed = Mathf.Lerp(m_MoveSpeed, travel.magnitude / deltaTime, response);
            m_TurnSpeed = Mathf.Lerp(m_TurnSpeed, float.IsFinite(turn) && turn < 90 ? turn / deltaTime : 0, response);
            if (travel.sqrMagnitude >= .000001f || turn > .1f && turn < 45f)
                m_LastMovementTime = Time.unscaledTime;
            // Rendered animation can step between two simulation snapshots. Keep
            // the motion gate briefly, without using displacement to schedule sound.
            m_CanStep = Time.unscaledTime - m_LastMovementTime <= .1f;
            if (travel.sqrMagnitude < .000001f)
            {
                m_Wall.Reset();
                if (turn < 90) m_TurnTravel += turn;
                if (m_TurnTravel >= 50 && Time.time >= m_GearAt && m_Stance != DollSingerStance.Prone)
                {
                    m_MovementSound = LocalOrWorld(library.Cloth, position, owned, TurnVolume(m_TurnSpeed));
                    m_TurnTravel = 0; m_GearAt = Time.time + .9f;
                }
                return;
            }
            m_TurnTravel = 0;
            if ((owned || MoonkovAudio.CanHearDetail(position, 12)) && m_MoveSpeed > .15f)
            {
                float height = m_Capsule ? m_Capsule.height : DollSingerStances.Height(m_Stance, 1.7f, .25f);
                Vector3 center = m_Capsule ? player.TransformPoint(m_Capsule.center) : position + Vector3.up * (height * .55f);
                float reach = (m_Capsule ? m_Capsule.radius : .25f) + .12f;
                if (m_Wall.TrySlide(player, center, travel, reach, deltaTime, out var wall))
                {
                    bool metal = RifleImpactSurface.Classify(wall.collider) == RifleImpactKind.Metal;
                    m_WallSound = MoonkovAudio.PlayWeaponLayer(metal ? library.SuitWallMetal : library.SuitWallBrush,
                        wall.point, .8f * FootstepVolume(m_MoveSpeed, DollSingerStance.Standing), owned ? .65f : 1);
                }
            }
        }

        public void OnFootstep(in PredictedPlayerGhost ghost, Transform player, bool owned)
        {
            var library = MoonkovAudio.Library;
            if (!m_Initialized || !m_CanStep || library == null || ghost.CurrentHealth <= 0 ||
                ghost.ControllerState.MovementType != FirstPersonController.MovementType.Standing ||
                Time.unscaledTime - m_LastStepTime < .18f) return;
            m_LastStepTime = Time.unscaledTime;
            Vector3 position = player.position;
            SampleSurface(player, library);
            float volume = FootstepVolume(m_MoveSpeed, m_Stance) * RaidFootsteps.LoadMultiplier(ghost.CarriedWeight, ghost.ClashEnabled);
            if (volume <= 0) return;
            if (m_Stance == DollSingerStance.Prone)
            {
                m_MovementSound = LocalOrWorld(library.Crawl, position, owned, volume);
                return;
            }
            LocalOrWorld(m_Surface ?? library.RegolithFootsteps, position, owned, volume);
        }
        public void Stop()
        {
            m_CanStep = false;
            m_MoveSpeed = m_TurnSpeed = 0;
            m_LastMovementTime = float.NegativeInfinity;
            var system = GameManager.Instance?.SoundSystem;
            m_Melee.Stop();
            system?.Stop(m_LootSound,.08f);m_LootSound=null;m_WasLootSearching=false;
            system?.Stop(m_GroanSound, .12f); m_GroanSound = null;
            m_Distress.Reset();
            system?.Stop(m_PainSound, .08f); m_PainSound = null;
            system?.Stop(m_CoughSound, .08f); m_CoughSound = null; m_NextCough = 0;
            system?.Stop(m_JumpVoice, .08f); m_JumpVoice = null;
            system?.Stop(m_ReloadSound, .08f); m_ReloadSound = null;
            system?.Stop(m_WeaponSound, .04f); m_WeaponSound = null;
            StopBreathing();
            system?.Stop(m_OxygenSupplySound, .12f); m_OxygenSupplySound = null; m_OxygenSupplyRetryAt = 0;
            system?.Stop(m_WallSound, .08f); system?.Stop(m_MovementSound, .08f); system?.Stop(m_StanceSound, .08f);
            m_BreathSound = m_HeartSound = m_WallSound = m_MovementSound = m_StanceSound = null;
            m_Wall.Reset();
        }
        private void StopBreathing()
        {
            var system = GameManager.Instance?.SoundSystem;
            system?.Stop(m_BreathSound, .12f); system?.Stop(m_HeartSound, .12f);
            m_BreathSound = m_HeartSound = null;
            m_Breath.Reset();
        }
        private void UpdateOxygenSupply(in PredictedPlayerGhost ghost, MoonkovAudioLibrary library, Vector3 position, bool owned)
        {
            // A loop represents current authoritative external flow, not a successful canister request.
            if (!owned || !RaidOxygen.ReceivingExternalSupply(ghost))
            {
                GameManager.Instance?.SoundSystem?.Stop(m_OxygenSupplySound, .12f);
                m_OxygenSupplySound = null; m_OxygenSupplyRetryAt = 0;
                return;
            }
            if (m_OxygenSupplySound?.Handle?.IsValid() == true || Time.unscaledTime < m_OxygenSupplyRetryAt) return;
            m_OxygenSupplySound = MoonkovAudio.PlayWeaponLayer(library.OxygenSupply, position, 1, 0);
            m_OxygenSupplyRetryAt = Time.unscaledTime + .5f;
        }
        private static SoundSystem.SoundInfo LocalOrWorld(SoundDef sound, Vector3 position, bool owned, float gain = 1)
            => MoonkovAudio.PlayWeaponLayer(sound, position, gain, owned ? .08f : 1);

        private void SampleSurface(Transform player, MoonkovAudioLibrary library)
        {
            m_Surface = library.RegolithFootsteps;
            int count = player.gameObject.scene.GetPhysicsScene().Raycast(player.position + Vector3.up * .4f,
                Vector3.down, m_SurfaceHits, 1.7f, LayerMask.GetMask("Ground", "Default"), QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = m_SurfaceHits[i];
                if (hit.collider.transform.IsChildOf(player) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                m_Surface = ClassifySurface(hit.collider, library);
            }
        }
        public static SoundDef ClassifySurface(Collider collider, MoonkovAudioLibrary library)
        {
            if (!collider || collider is TerrainCollider) return library.RegolithFootsteps;
            if (RifleImpactSurface.Classify(collider) == RifleImpactKind.Metal) return library.MetalFootsteps;
            string name = collider.name;
            var renderer = collider.GetComponent<Renderer>();
            string material = renderer && renderer.sharedMaterial ? renderer.sharedMaterial.name : "";
            if (material.StartsWith("R27S_", System.StringComparison.OrdinalIgnoreCase) ||
                material.IndexOf("regolith", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("rock", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("regolith", System.StringComparison.OrdinalIgnoreCase) >= 0) return library.RegolithFootsteps;
            return library.HardFootsteps ?? library.RegolithFootsteps;
        }
    }
}
