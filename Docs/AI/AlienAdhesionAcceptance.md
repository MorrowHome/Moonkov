# Adhesion acceptance prototype (stacked on ground foundation)

This is an **uncompiled, unrun sandbox candidate**, not proof of finished climbing.
It stacks on draft PR #6, head `c014d11f9bc3fe85204f8a702aff2748543d1751`.
No main scene, prefab, global collision, package or network changes are included.

## What changes

- Surface-relative ray queries support ground, walls and ceiling normals; the
  original ground-query entry point still rejects steep surfaces.
- The gait consumes an explicit contact frame, transports its tangent through
  inversion, predicts in the surface plane and captures the swing lift frame.
  Planted feet remain world anchored on static surfaces.
- A bounded authored pose route guides the central shell through two acceptance
  courses: floor/wall/ceiling inner corners and a pillar's convex outside edges.
  Position and normal advance together, rather than rotate first at an edge.
- Each proposed centre must find an allowed physical support with a five-ray local
  footprint (central and tangent offsets, including near convex edges) within grip range
  and normal tolerance, pass a current-centre overlap test, a swept sphere test and an endpoint overlap
  test. The .53 m sphere encloses the .55 x .22 x .85 m visible shell in any
  orientation. In-place rotation is therefore covered by the same envelope.
  The legs do not form collision or hit-registration authority.
- Missing/blocked support holds the last accepted pose. There is no automatic
  warp, gap jump, route looping teleport or world-Y falling shortcut.

This is deliberately **authored route guidance**. It does not discover walls,
plan a flank, route around obstacles, implement general adhesion dynamics, or
validate the support polygon. The current candidate checks a bounded central support footprint;
it does not guarantee all feet can stay planted around every corner. Static
terrain and unit-scale rig/course transforms only. The motor rejects non-unit rig scale.
Moving/removed surfaces can invalidate previous contacts; safe hold
means no further movement, not a physically simulated fall or recovered stance.

The ground-only demo remains available separately. Its original future fixtures
remain labelled PENDING; the new course has its own explicit menu entry.

## Try and record evidence

Use Unity 6000.5.10f1 in an isolated worktree. Compile and inspect Console first.
Run **Moonkov > Alien > Run Math and Probe Checks**, then
**Run Adhesion Frame and Clearance Checks**, outside Play.
Run **Create Adhesion Acceptance Course (Additive)**, close unrelated gameplay
scenes without saving unintended changes, and enter Play. Additive scenes share
default physics; ancestry filtering/distant placement limit queries, and the
new scene adds its own camera. No assets are saved automatically.

Two black quadrupeds traverse separate authored inner/outer-corner courses.
The readout exposes support hold reasons, planted counts and IK reach clamps.
Pause/Run and 1.5/6 m/s buttons are supplied. A course ends without warping back;
Stop/Play restarts it. The default angular rate is 90 degrees/s, intentionally
conservative until contact continuity and clearance are validated.

Required observations at 30/60/144 FPS, slow and fast:

1. Floor -> wall -> inverted ceiling: continuous centre/frame, no body penetration,
   knee flip or uncontrolled oscillation; feet must visibly land on real surfaces.
2. Convex pillar edges: no normal snap, air-walking, radius shrink into solid, or
   foot targets cutting through the pillar. Check planted and swing legs separately.
3. Insert a solid blocker ahead: swept body collision holds before impact. Insert
   at the current body centre: the explicit current-centre overlap rejects continued movement.
4. Remove next support: no new movement into a gap. Remove current support: hold
   without a warp; this is a deliberate prototype fallback, not gravity recovery.
5. Pause mid-transition, re-enable, unload/reload: no duplicate rig/materials;
   same route state after pause and supported pose preserved after re-enable.
6. Re-run ground demo and all foundation checks: no regression in slopes/steps,
   start/stop, turning, scene isolation or cleanup.

The C# menu checks exercise actual Unity query/frame APIs, but were not executed
in the cloud. Python geometric course checks are independent reference models,
not a substitute for C# compilation, physics or Play. No visual, FPS, LAN or
combat pass is claimed.

## Next gate

Do not integrate a server enemy until contact continuity and body/leg clearance
have been verified visually. Then add generic forward/down/convex probe selection,
contact hysteresis and slow one-foot transition stepping as required by evidence.
Only after that choose a 3D surface graph/authored links and add server-authoritative
stalk/ambush/flank/telegraph/strike/damage/death through the existing GhostBridge
architecture. Preserve the original PMC ground NavMesh.
