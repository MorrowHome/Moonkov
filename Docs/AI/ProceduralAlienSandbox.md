# Procedural alien: isolated locomotion foundation

## Scope and evidence

This is the **first ground-only presentation slice**, not the complete enemy.
The intended creature is a fast, fluid black quadruped: heavy upper legs tapering
to sharp tips, a small barely visible body, ground/wall/ceiling movement,
ambush and flanking behavior, and authoritative combat in solo or LAN host +
up to four joining clients. None of the wall/ceiling, combat, navigation or
network behavior is claimed to work in this slice.

Inspected baseline: `a4ede5ee67d799808162cc39e5e2930ce2a75045` (main).
Unity `6000.5.10f1`, URP `17.5.0`, Entities/NetCode `6.5.0`.
Existing `Docs/AI/UnityProjectContext.md` and `DollSingerEnemies.md` describe the
MonoBehaviour/GhostBridge + server ECS architecture. No repository AGENTS.md or
`.agents/skills` appeared in the recursive baseline tree.

No existing scenes, prefabs, player physics, build settings, packages, networking
or gameplay files are changed. Runtime and editor code have isolated assemblies.
All meshes and materials are original runtime-generated geometry; no purchased,
downloaded or third-party assets/code are required. Nothing runs on import.

## Try it locally

1. Use a separate worktree/branch. Allow Unity to compile; inspect Console first.
2. Run **Moonkov > Alien > Run Math and Probe Checks** outside Play. It invokes
   the actual C# math and scene-filtered Unity physics queries.
3. Run **Moonkov > Alien > Create Isolated Sandbox (Additive)**. This creates a
   new unsaved scene, restores the prior active scene and never saves other
   scenes. Undo removes the generated root; close the empty scene separately.
4. For isolated Play, close other gameplay scenes without saving unintended
   changes. The sandbox is placed at (10000,10000,10000), queries only its terrain,
   and adds its own camera. Additive Editor scenes share the default physics world;
   isolation comes from the explicit terrain filter and separation, not a private
   physics world. It does not need MainMenu or a network session.
5. Enter Play. The runtime builder creates black tapered four-leg geometry.
   The driver alternates slow motion/bursts, stops, turns, climbs the ramp and
   small steps, and stops/reverses at unsupported ground. Pause/Run is available.
   Scene view is useful for following it along the course.
6. Save only the new sandbox scene if desired, at a new path of your choice.
   Unload it to remove the extra camera/light. It is never added to build settings.

The wall, underside-ceiling and concave/convex-corner fixtures are explicitly
labelled **PENDING**. They are future acceptance geometry, not a traversal demo.

## Implemented contracts

- Two-bone analytical IK preserves segment lengths and clamps unreachable goals;
  coincident targets and collinear bend hints return finite output.
- Four independent foot states with diagonal alternating swing groups. Planted
  feet stay in world space; swing destination is fixed at lift-off. Landing is
  rechecked against actual support. Missing destinations do not create fake feet.
- Velocity and yaw anticipate placement; swing lift has zero endpoint velocity.
  The body samples its own footprint and aligns smoothly to ground normals.
- Bounded ray buffers fail closed on saturation, ignore triggers, filter to the
  explicit terrain hierarchy and use the owning scene's PhysicsScene. Feet have
  no colliders. The small shell's generated collider is disabled immediately.
- The sandbox driver probes ground ahead and sweeps a small body radius. It is
  a reproducible demo, not a production kinematic controller. No global collision
  settings or player-controller settings are modified.
- On enable or large relocation, contacts reset; runtime materials/mesh are owned
  by the rig and disposed on destruction. No scene searches or per-frame ray arrays.

Known first-slice limits: static terrain only; no moving-platform anchors; no
swing-path obstacle avoidance; no support polygon/dynamics or guaranteed no-slip
at every speed; no full-body collision hull; no arbitrary-surface adhesion.
The ground probe and demo motor intentionally use world up and reject steep
surfaces. The IK and swing math accept arbitrary frames, but that alone does not
solve walls/ceilings. UI reach-clamp counts expose overextension rather than hide it.
A smooth/scary appearance remains a human Play acceptance criterion.

## Local validation matrix (not run in the cloud)

Record Unity version, frame rate, screenshots/video, Console and outcomes.

| Case | Acceptance gate |
|---|---|
| Stationary and pause/resume | Four planted feet settle without swimming; no NaN or duplicate rig |
| Start/stop at 1.6 and 6 m/s | No persistent reach clamp, teleport, foot skating or body penetration |
| 180-degree turn both ways | Alternation remains coherent, knees don't flip, contact targets stay fixed |
| Ramp and stair up/down | Stable normals, sufficient clearance, no oscillation at seams |
| Missing ground and ledge | Driver stops/reverses; no foot claims support across the gap |
| 30/60/144 FPS and hitch | Comparable stride; safe behavior on large frame delta |
| Disable/re-enable, reload scene | Contact reset, no duplicate meshes/materials, correct disposal |
| EditMode checks | Seeded IK lengths, singularities, endpoints, wall/inverted math frame invariance, filtering |
| Wall/ceiling/inner/outer corner | PENDING, unsupported by this motor; never count this as a pass |

Cloud validation is source inspection and Python reference/static checks only.
Unity, a C# compiler, Play, LAN, profiling and visual capture were unavailable.
The Python reference calculations do **not** execute or prove the C# implementation.

## Next milestones and integration boundaries

1. **Adhesion/contact-frame motor.** Introduce explicit surface normal/tangent,
   contact confidence, surface identity, grip range and recovery state. Use
   forward obstruction, down-to-surface and convex-wrap candidate probes,
   hysteresis and continuous orientation transport. Do not instantly flip normals
   or teleport between floor/wall/ceiling. Separate desired path from actual
   supported pose. Ground, walls, upside-down ceilings, inside corners, outside
   corners and missing-surface recovery must pass at low/high speed before use.
   Planted anchors and bend hints must remain coherent throughout transitions.
2. **3D surface routing.** Existing ground NavMesh cannot route walls/ceilings.
   Choose an explicit authored contact graph or bounded surface graph after the
   adhesion prototype is validated. Include body clearance, surface transitions,
   unsupported gaps, unreachable destinations, costs and limited replanning.
   Preserve the current ground NavMesh for existing PMCs.
3. **Authoritative enemy slice.** Reuse current server-world lifecycle and
   GhostBridge patterns. `DollSingerEnemySystem` already has observation-only
   memory, 5 Hz decisions, staggered sensing, complete-path checks and tactical
   candidate reservations. Its humanoid PlayerInput motor and player ghost are
   not automatically a valid alien motor. Keep alien movement, target selection,
   health, attack ticks and damage on the server; client feet remain derived
   presentation. Integrate through a narrowly scoped GhostBridge adapter; no
   second network stack or wholesale ECS rewrite.
4. **Combat gates.** Stalk -> last-known-position flank -> ambush wind-up ->
   committed strike -> recovery, with visible telegraph and cooldown. Server
   validates target, range, occlusion, swept attack volume, attack identity and
   one damage application per target. Do not use decorative leg collisions or
   animation callbacks as hit authority. Damage/death cancels attack consistently.
5. **Solo/LAN acceptance.** Host alone, host + one client, then five total players;
   late join during attack, lag/reordered presentation, target disconnect/death,
   despawn/reload and repeated spawn. Confirm one authoritative damage event,
   consistent death and no server rendering cost. Profile a representative count
   before describing the enemy as ready.

Reference API: Unity [EditorSceneManager.NewScene](https://docs.unity.com/en-us/engine/6000.5/script-reference/unityeditor/scenemanagement/editorscenemanager/newscene),
[PhysicsScene.SphereCast](https://docs.unity.cn/2022.3/Documentation/ScriptReference/PhysicsScene.SphereCast.html).
