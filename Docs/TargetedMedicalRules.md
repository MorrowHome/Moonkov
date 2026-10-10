# Targeted medical transaction preparation

This stacked slice depends on the health foundation in PR #5 (`96f1c28aaa6786101fea029031a772565d60a908`), whose C# and Unity validation is still pending. It adds a pure shared wrapper and standalone checks only. The original scalar `InventoryGraph.UseMedical` source/signature and all existing gameplay, RPC, persistence and UI callers remain unchanged.

## Transaction contract

`TargetedMedicalRules.Apply` receives an inventory snapshot/version, regional health, target region, injector ID and immutable health config. Success returns **both** a new inventory graph and healed state. Inputs are untouched, including on success. Failure returns the existing `InventoryError`, null updated inventory and unchanged health.

- Reuse the existing M-40's 40 HP healing and pockets/equipped-chest-rig accessibility. Backpack/stash items remain inaccessible.
- Validate graph, identity, version, region and all health/config bounds before preparing changes. Reject malformed state and version overflow.
- Check whole-character death independently from the selected region. Zero head/chest cannot be revived. A living actor's zero stomach/arm/leg can recover. This avoids incorrectly feeding a depleted limb's zero into the old scalar method, which rejects zero health.
- A full target, dead actor or invalid request consumes nothing. Healing affects only the target and is capped at its maximum.
- Only after a positive heal is computed: deep-clone inventory, remove exactly one injector, increment version exactly once, validate, then expose the complete pair. A failure never exposes a half-prepared result.
- A future single-threaded authoritative server adapter must commit both outputs together only after existing source-connection, active-raid, player-ownership and request-ID checks. This helper does not perform concurrency control or request replay tracking. Replaying the old version against the committed graph fails stale; reusing an uncommitted old snapshot is not a replay defense.
- Do not call this wrapper from the account API or a client and treat the result as authority. It has no network entry point.

## Next runtime boundary (design only)

`PredictedPlayerGhost.CurrentHealth` currently gates player death/settlement, movement, equipment, AI targeting, audio and HUD. Regional health must become the single server-owned source, with any temporary scalar compatibility field **derived** from it. On head/chest death the projection must be zero even while other regions have health; a plain region sum is incorrect. While alive, a proposed compatibility display is normalized total remaining/max health on the legacy 0–100 range. That display formula is provisional, not implemented here, and must never receive independent damage or healing writes.

The runtime migration must handle hitscan (`ServerPlayerMovementSystem`), direct/area projectile damage (`Projectile`), medical RPC (`ServerGameSystem.Inventory`), spawn initialization, death/kill accounting and compatibility projection as a reviewed coherent transaction boundary. NPCs currently share the scalar ghost type; their migration/fallback needs an explicit policy. No schema, cross-raid health storage, respawn reset, enemy behavior or existing medical flow is changed by this slice.

`PlayerGhost` currently creates a head sphere/body capsule, updates the head on the client, and has an empty `UpdateServer`. Seven-region hit mapping therefore needs its own authoritative pose/headless test gate; adding labels to visual bones alone is insufficient. New limb colliders must not be masked by the existing body/movement capsule or collide across Host client/server replicas. Area-damage region allocation remains an explicit later decision, not an arbitrary first-overlap choice.

## Validation

From repository root, using the existing .NET 10 SDK:

```sh
dotnet run --project Backend/HealthChecks/HealthChecks.csproj
dotnet run --project Backend/TargetedMedicalChecks/TargetedMedicalChecks.csproj
```

Both check projects are dependency-free and require no DB, credentials or project-root argument. Targeted checks link the unchanged inventory/battery sources plus the actual health/wrapper files. Deterministic fixture IDs cover depleted-target recovery, no resurrection, capped healing, stack removal, deep-copy atomicity, stale replay, access restrictions, malformed inputs, version overflow, damage-before-heal ordering and legacy scalar compatibility.

**Neither C# executable nor Unity compilation was run in the cloud workspace because those runners are unavailable.** Source review and whitespace/dependency checks are not runtime results. Keep both PRs draft until exact-head .NET output/exit and Unity 6000.5.10f1 import/compiler results are recorded. No scene or prefab setup is needed; this patch does not enable targeted medical in gameplay.
