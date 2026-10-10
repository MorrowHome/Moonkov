# Observation-driven ambush sandbox

Stacked on PR #7, exact base `0422242ebb4057442d69d864057a45bca03c7547`.
This is a source candidate. Unity compile/Play, physical gait and network behavior
remain unrun. It is neither production AI nor a completed combat enemy.

## New behavior

An original black quadruped starts on an authored ceiling perch. A physical
range/FOV/LOS sensor copies visible dummy torso positions into a deterministic
brain. The graph planner receives only that last observation, never the hidden
live Transform. The dummy has front/behind-cover/walk test controls; its position
changes do not directly update the brain.

The 12-node course contains ceiling/wall transitions plus two floor flanks around
solid cover. Dijkstra chooses reachable authored links using travel/turn cost and
exposure to the remembered point. Physical support and body-clearance checks still
run for every motor pose. A blocked link is disabled, then the creature holds and
searches; no path/budget failure also holds/searches. Search here means bounded
memory/state expiry, not an implemented sweeping head or spatial search animation.

The brain progresses through Perch, Stalk, Approach, Telegraph, Strike, Recover,
Search and Dead. Telegraph requires fresh physical visibility and attack range, and loss of either
cancels the wind-up. Its attack point stays locked; later observations
cannot home that strike onto a hidden moving dummy. Each strike emits one numbered
**diagnostic intent**, shown as a red Scene-view wire sphere. No health damage,
hit registration, real player lookup, weapon changes, Ghost registration or RPCs
exist in this sandbox. Production damage must later recheck range/LOS/swept volume
and authority before applying anything.

## Bounds and separation

- Graph caps: 128 nodes, 512 directed edges; this course has 12 nodes and 24 edges.
- Search uses fixed buffers, at most 128 settled nodes per request; equal costs
  have deterministic node-index ties. Graph edges are author assertions, not
  proof that arbitrary geometry can be traversed.
- Observation snapshots are at most 10 Hz, decisions at most 5 Hz, replanning
  at most 1 Hz. Attack eligibility separately rechecks physical LOS/FOV/range every
  frame and supplies only a boolean to the brain.
  Contact motor remains frame-driven sandbox code; it is not authoritative NetCode
  movement. Route arrays allocate only when planning, not every movement frame.
- Observation expires after four seconds; route failures clear it. Hidden target
  motion does not change remembered position. Invalid/nonmonotonic time is rejected.
- The central shell uses prior clearance guards. Feet/legs are decorative and
  still lack guaranteed full obstacle clearance. Static unit-scale course only.
- A new opt-in motor flag faces route travel; defaults off to preserve the prior
  authored acceptance course. Replacing a route retains the accepted pose.
- No automatic scene generation/import or modification to existing game scenes.
  Additive scenes still share default physics; probes filter explicit course terrain.

## Local run order

1. Use the exact stacked branch in a separate worktree, Unity 6000.5.10f1.
2. Compile and record Console baseline/new errors.
3. Run foundation Math and Probe Checks, Adhesion Frame and Clearance Checks,
   then the new **Moonkov > Alien > Run Surface Graph and Ambush Checks**.
4. Run **Create Observation Ambush Sandbox (Additive)** outside Play. Unload
   unrelated gameplay scenes without saving unintended changes. Enter Play.
5. Observe ceiling departure, wall/ground transition, stance, and approach to the
   front dummy. Confirm wind-up precedes each diagnostic attack, with recovery.
6. Move dummy behind cover before the creature reaches it. Last observation must
   remain fixed and expire; the alien must not magically know its new position.
   Use Scene view to place the dummy on a visible flank, then hide it again to test
   remembered-route pursuit. Gizmos expose planned route and locked attack point.
7. Remove a link's supporting collider or insert a blocker. Confirm physical hold,
   invalidation and bounded search; no teleport, repeated route-reset storm or
   unbounded graph search. Stop/Play resets the graph's disabled links.
8. Test equal-cost and altered-exposure paths, unreachable target, paused/re-enabled
   adapter, death during wind-up and repeated intents. Then re-run prior ground
   and authored adhesion courses for regression.

GUI pause stops alien logical time/movement, not the dummy or the whole game.
Kill is a terminal logic test only, with no corpse/death animation. FOV/LOS can
legitimately prevent acquisition from some orientations; do not bypass the sensor
to make a demonstration work.

## Validation truth

C# menu checks exercise the actual bounded planner/state classes when run locally.
Source inspection/reference calculations cannot establish Unity compilation,
physical LOS behavior, visually convincing motion or multiplayer correctness.
Independent Python reference checks sampled 3,915 authored edge poses in both
directions at two speeds and compared 500 random shortest-path models with
Floyd-Warshall; these do not execute the C# implementation.
All graphical/physics acceptance remains pending. The existing production combat
and networking integration gates in the earlier documents still apply.
