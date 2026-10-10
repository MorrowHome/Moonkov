# Opt-in alien dummy combat

Stacked on PR #9 remote head `5508fb860ffe47ea84c9bae818595191c2596ff5`.
The local source snapshot is synthetic; publication preserves that exact remote
parent and its tree. This is the first opt-in connection from the alien's visible
sandbox behavior to the tested contract and a damageable **fake** target.

## What changes

Use **Moonkov > Alien > Create Dummy Combat Sandbox (Additive)** outside Play.
It reuses the observation course and adds a dedicated trigger hitbox and combat
adapter. **Create Observation Ambush Sandbox (Additive)** still generates the
original diagnostic-only mode. Neither menu edits existing scenes or player data.

The dummy starts at 100 fake HP; accepted strikes subtract 25. Contract-owned
phases use one monotonic 100 Hz integer clock: 70 ticks wind-up, 18 strike and
120 recovery. A slow frame advances directly to current time, without replaying
missed hit windows. The brain's opt-in external mode requests a locked wind-up;
it does not run another float attack timer or emit diagnostic strike intents.
Default observation mode retains its existing timing.

The locked world-space attack direction is committed once. Each strike sample
requires the matching enabled same-scene dummy collider within centre range,
clear current LOS, clear swept path and a physical sphere sweep (radius 0.16m).
It never follows the target by turning the committed direction. Explicit initial
target overlaps are handled; query-buffer saturation fails closed. Decorative
legs have no role in hit registration. Terrain ancestry limits cover checks and
the target collider is separate from terrain. Wrong colliders cannot grant hits.

The fixture uses its owner's PhysicsScene; additive scenes still share the
ordinary physics world. Its distant position and explicit ancestry/target checks
are filtering, not an independent world. Transform-driven colliders are flushed
with Physics.SyncTransforms once per running sample, without changing auto-sync
or collision settings. This needs profiling before any production adaptation.

Commands queue until the next running sample, before sensing/hit validation.
Death wins when death and respawn are queued together. Respawn increments life
generation; it preserves pose and never recreates the attack contract. Target
respawn clears fake-health diagnostics and cancels an old-life commitment while
preserving any already-started recovery. Attacker respawn creates a new tactical
brain but advances the same contract lifecycle. Pause freezes the clock and
cancels a wind-up on resume; disabled/re-enabled components do not duplicate
initialization. The fixture stops after a bounded 24-hour simulation lifetime.

The permission boolean sent to the contract means local test-harness permission
only. It is not a real server role check, authentication or network authority.

## Acceptance order

1. Compile the exact candidate in Unity 6000.5.10f1. Run all existing math/probe,
   adhesion, surface graph/ambush (31 groups, original 21 plus 10 external-mode
   regressions) and combat contract (35 groups) menus outside Play.
2. Verify the authored hitbox survives ordinary scene/domain reload on entering Play.
   Run **Moonkov > Alien > Run Dummy Combat Integration Checks** outside Play.
   It exercises actual collider casts, sideways miss/wrong target, cover and
   starting overlap, saturation, arbitrary sweep directions, damage once,
   lifecycle reset, death ordering, skipped windows and pause. Temporary scene
   cleanup asserts prior handles, active scene, roots and dirty states unchanged.
3. Create the opt-in combat scene, close unrelated gameplay scenes and enter Play.
   Capture four valid hits producing 100→75→50→25→0. A dead dummy is not sensed
   as an attackable target. Capture a yellow locked telegraph followed by red
   sweep gizmos, fake-health values and accepted sequence diagnostics.
4. Move the dummy sideways during wind-up while still in range/LOS: the locked
   sweep must miss. Move it behind cover/out of range: no accepted damage.
   Kill attacker/target during wind-up; respawn and verify no old attack lands.
5. Pause/re-enable during wind-up and recovery. Remove support/insert an obstacle;
   safe-hold behavior and the original cooldown must survive. Check 30/60/144 FPS
   and a frame hitch long enough to skip the strike window.
6. Check the original observation sandbox and its default brain tests unchanged.
   Validate ground/wall/ceiling posture, solid-body clearance and scene cleanup
   visually; no all-leg obstacle or multiplayer guarantee is implied.

## Evidence limits

The original 35 System-only contract groups passed under .NET SDK 10.0.401 against
exact PR #9 source. The same suite is rerun for this candidate. It does **not**
compile the Unity-dependent brain, adapter, builder or physics checks. Those still
require real Unity execution; no stubs/mirrors substitute for it. Independent
source review and static checks are additional evidence, not a Unity pass.

Production GhostBridge/server health integration remains gated on that feedback,
a reviewed projectile/hitscan seam coordinated with body-health work, and solo/
LAN host plus four-client acceptance. No main-map spawn or production damage is
introduced by this candidate.
