# Alien combat contract and private fake-health proof

Remote publication is stacked on observation sandbox PR #8, exact parent
`1b70760c2b61d5af63b4d9598a4058a7d30687b9`.
The cloud checkout uses synthetic local snapshot `2277f69c3893237be61addbc70ba95fdb6c27bee`;
all 48 unchanged snapshot blobs were verified equal to that remote parent.
This milestone is isolated deterministic rules plus a fake-health test sink.
There are no production player-health, weapon, Ghost, physics or networking edits.

## Contract boundary

The runtime contract is plain System-only C#. It accepts a committed single-target
attack receipt and trusted adapter evidence, not a client-reported hit. Every hit
validation requires evidence from that exact simulation tick, known-live attacker
and target, authority permission and valid geometry. Wind-up start requires current authority and known-live actors;
physical geometry is validated when accepting the strike. Those booleans are **not a
network security boundary**; the future server adapter must obtain them from its
own Ghost role, current physics scene and authoritative target state.

An attack has positive wind-up, strike and recovery durations. Strike acceptance
uses an upper-exclusive tick window. Time cannot regress; skipped/expired windows
do not reopen. Attack sequence never wraps or resets, including across respawn.
Actor identity includes a nonzero ID and explicit lifecycle generation; a reset
must keep that ID and advance generation. Old tokens/receipts cannot target a new
life. The supplied clock is nonnegative long ticks; a future NetCode adapter must
map its wrapping tick representation explicitly rather than assume equivalence.

Only one committed target/current attack is retained. One accepted hit consumes
that receipt; repeats, another target, stale attack ID, expired time, cancellation
or death reject. Storage is O(1), without a forever-growing seen-ID set. Damage
comes from the committed contract, not a caller-editable receipt amount.
Wind-up cancellation frees the slot. At or after strike opening, cancellation
disables further hits but preserves the original recovery deadline, including
already-consumed strikes; repeat cancellation rejects. Only an explicit new
actor life resets that lifecycle state.

The fake sink verifies its own current identity and alive state, consumes through
the contract, then applies positive finite damage clamped at zero. It exposes no
raw unguarded damage entry point. Finite positive damage smaller than the
representable spacing at the current health can yield zero actual subtraction.
That hit is still consumed once; `AppliedDamage` records zero honestly. Death is terminal for that lifecycle; respawn
requires a new generation. No fake-health value is connected to player health,
body parts, oxygen, hunger/thirst, inventory or persistence.

## Same-tick death policy

The future simulation adapter must resolve **known fatal/death state before hit
validation**. A dead attacker or target at the validation phase cannot emit/apply
a strike on that tick. The pure checks exercise both death-at-strike cases.
This contract does not retroactively undo a hit already committed before an
unknown later event. Deterministic phase ordering is part of integration, not a
promise that this isolated helper schedules a real network world.

## Run the actual C# checks

The same check source can run by either route:

- Unity: **Moonkov > Alien > Run Combat Contract Checks**, outside Play.
- Existing .NET 10 SDK: `dotnet run --project Backend/AlienCombatChecks/AlienCombatChecks.csproj`.

The .NET project links the actual runtime and check files, rather than mirrored
logic or Unity stubs. It uses no added package dependencies. `net10.0` matches
existing backend check projects. This runner was not executed in the cloud:
no C# compiler, .NET SDK or Unity was installed there.

The suite contains 35 check groups covering exact time boundaries, duplicate/old identity, target/lifecycle
mismatch, stale evidence, cancellation, attacker/target death, respawn replay,
invalid damage/windows/time and single-consumption application. Passing them
would establish those rules only, not real physics or network authority.

## Integration remains gated

1. Run exact-head Unity compilation and all prior ground/adhesion/observation
   sandboxes. Resolve gait/clearance/scene-lifecycle issues with visual evidence.
2. Run this actual C# contract suite, then review its mapping to server ticks.
3. Add an opt-in isolated GhostBridge server/client test scene; server owns
   movement/brain/HP/attack timing, clients derive cosmetic feet. No main-map spawn.
4. Independently review the narrow projectile and hitscan damage seam. Existing
   code only accepts player health ghosts. Coordinate with body-part health work
   before touching its production integration or creating another player HP store.
5. Validate one authoritative damage/death result, cancellation, late join,
   disconnect/reordered snapshots, then host plus four clients. Author and validate
   a real-map surface graph and complete audio/hit/death presentation and profiling.

Only the final runtime/multiplayer/visual gates support calling the alien complete.
