# Health rules: isolated first slice

This is an unused, portable domain model under `Networking/Shared`, following the existing shared-rule/check-project layout. It does **not** replace the existing scalar health. No gameplay, ECS, Ghost, hitbox, medical transaction, scene, UI, audio, backend or persistence path references it. The console checks link the exact Unity source files and require no database, credentials, Unity packages or NuGet packages.

## Contract

- Seven regions: head, chest, stomach, left/right arm, left/right leg. Zero head or chest means terminal death. Damage after death and healing after death do nothing; ordinary healing cannot revive.
- Excess finite damage saturates the target at zero without spilling into other regions. Zero stomach/limbs does not kill. Ordinary targeted healing can restore those regions, consistent with the first prototype's no-surgery/no-revive rule.
- Any depleted arm slows stability recovery and increases equip duration. Any depleted leg disables sprint and reduces walking speed. Both arms/legs use the same penalty as one, without stacking. Depleted stomach exposes a hunger/thirst drain multiplier; actual hunger/thirst resources and consumption are a later slice.
- Effects are outputs for a later authoritative adapter. They do not change input, camera, movement or weapon behavior here. Death must gate actions in that adapter; effect scales alone are not a death controller.
- Config constructors reject invalid values. Initial health clamps finite values to `[0, maximum]`; NaN and infinities fail closed to zero. Negative/zero/nonfinite damage and heal amounts are no-ops. Excess finite healing saturates without overflow. Invalid region/environment enums and negative elapsed ticks throw.
- State is copied by value; config is immutable. Use one unchanged config for a state's lifetime. These types are not a save/wire schema, a Burst job contract or an automatic respawn/reset policy. `Create`/`Full` are explicit factory calls only.

## Oxygen time and boundaries

`Advance` accepts a nonnegative integer count of simulation ticks and one authoritative environment. `TickMilliseconds` describes a fixed tick's duration; it is not read from Unity Time. The future server adapter accumulates clock time, retains the fractional-tick remainder and passes whole ticks. It must split calls at environment changes. Each call accepts at most `OxygenRules.MaxTicksPerAdvance` (4096) ticks. Larger requests throw before changing state; the adapter must retain and split the complete backlog. No time is silently discarded. Intermediates use 64-bit integer arithmetic.

Oxygen capacity, drain and refill are integer resource units per tick. This deliberately quantizes the model. A tick with any oxygen at its start is supplied, even if its last units are consumed during that tick. Exhausted exposure begins on the following tick, so there is at most one simulation tick of quantization grace, independent of batch size. Fixed-config state and pulse behavior is deterministic under tick subdivision: every due pulse performs the same double subtraction in order, including non-binary-exact damage such as 0.1. Floating-point rounding is therefore consistent between batching and single ticks, though a tiny positive remainder can require one more pulse. The bounded loop performs at most 4096 pulse applications per call.

- Vacuum drains oxygen. Warnings are Normal, Low, Danger, Exhausted, inclusive at configured thresholds. Exhaustion itself causes no immediate damage. Every configured number of fully exhausted ticks applies a chest-damage pulse.
- An oxygenated interior immediately stops oxygen drain and hypoxia damage and refills gradually. It never resets capacity instantly. Unpaid exhausted-exposure ticks pause across indoor visits and supplied outdoor ticks rather than reset, so crossing a boundary repeatedly cannot cancel an approaching damage pulse. The next pulse still requires actual exhausted outdoor exposure.
- `Warning` describes reserve, while `IsHypoxic` additionally requires a living player in vacuum at zero reserve. An exhausted reserve indoors does not mean current suffocation.
- A batched lethal exposure stops at its lethal pulse. No postmortem pulse, refill or state change occurs. `DamagePulses` and `ChestDamageApplied` report what was applied, not every hypothetical future pulse in the requested interval.
- Advance returns the final warning. A future visual/audio adapter must process regular ticks/threshold crossings and render progressive breathing/device cues plus readable icons/text. It must not rely on one large catch-up call to replay every cue. No blur, flashing or forced camera shake is introduced.

## Prototype parameters, not validated balance

Health maxima: 35/85/70/60/60/65/65. Any depleted arm: 0.6 stability-recovery speed and 1.4 equip duration; any depleted leg: 0.6 walking speed; depleted stomach: 1.5 resource drain. Oxygen: 100 ms ticks, 1000 capacity, 1 consumed per outdoor tick, 5 refilled per indoor tick, low at 250, danger at 100, 3 chest damage per 10 exhausted ticks. All are explicit constructor values and require play tuning.

## Run checks

From the repository root with the existing .NET 10 SDK:

```sh
dotnet run --project Backend/HealthChecks/HealthChecks.csproj
```

No project-root argument, PostgreSQL, secret file, HTTP service or extra package is needed. The check program fails nonzero on a broken assertion. It covers all seven region boundaries, no spill, terminal death, targeted recovery, penalties, NaN/infinity/config rejection, warning thresholds, partial oxygen ticks, interior/refill transitions, anti-reset carry, oversized-batch rejection, large numeric values and batch-versus-single-tick equivalence (including 0.1 damage).

In Unity 6000.5.10f1, import the two scripts with their committed `.meta` files and check for new compiler errors. Import alone does not enable the model. The rule check is not a Unity integration test.

## Validation status and next gate

At publication preparation: source/diff inspection only in the cloud workspace. No .NET SDK/C# compiler or Unity Editor is available here, so **the C# check executable and Unity compilation have not run**. Run the command above on the exact PR head before accepting this slice. Keep the PR draft until these results are recorded.

The next independently scoped slice should establish the single server-owned regional state and hit mapping, then extend the existing validated/atomic medical transaction with target region. Separate gates remain for authority, duplicate requests, statistics, UI/audio, hunger/thirst, indoor volumes, single-player/Host, and extraction/death saves. This patch makes no decision or change to health persistence across raids or deployment resets.
