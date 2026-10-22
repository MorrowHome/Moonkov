using Unity.MP_FPS.DollSinger;

int checks = 0;
void Check(bool condition, string label)
{ if (!condition) throw new Exception(label); checks++; }
void Close(float actual, float expected, string label)
{ Check(Math.Abs(actual - expected) < .0001f, label + ": " + actual + " != " + expected); }
MeleeCombatState Equipped(MeleeWeaponKind weapon)
{ var state = default(MeleeCombatState); MeleeRules.Select(ref state, weapon); return state; }

foreach (var weapon in new[] { MeleeWeaponKind.Fists, MeleeWeaponKind.Dagger, MeleeWeaponKind.EnergySword })
{
    Check(MeleeRules.TryDefinition(weapon, out var definition), weapon + " has real rules");
    foreach (var attack in new[] { MeleeAttackKind.Light, MeleeAttackKind.Heavy })
    {
        var state = Equipped(weapon);
        Check(MeleeRules.TryStart(ref state, attack, true, false, out _), weapon + " starts " + attack);
        Close(state.Stamina, 100 - definition.Cost(attack), "Attack spends its own stamina once");
        var timing = MeleeRules.Timing(state);
        Check(state.Phase == MeleePhase.Windup, "No immediate active damage");
        Check(!MeleeRules.Tick(ref state, timing.Windup * .5f, false).Valid, "First half of windup cannot hit");
        var active = MeleeRules.Tick(ref state, timing.Windup * .5f + timing.Active * .5f, false);
        Check(active.Valid && active.From == 0 && active.To > 0, "A crossed active boundary is not lost");
        Check(state.Phase == MeleePhase.Active, "Attack enters its active window");
        float stamina = state.Stamina;
        Check(!MeleeRules.TryStart(ref state, attack, true, false, out var error) && error == MeleeStartError.Busy,
            "Repeated input cannot stack attacks");
        Close(state.Stamina, stamina, "Rejected attack spends nothing");
        MeleeRules.Tick(ref state, timing.Active, false);
        Check(state.Phase == MeleePhase.Recovery, "Attack has a recovery interval");
        Check(!MeleeRules.Tick(ref state, timing.Recovery + 1, false).Valid, "Recovery has no hit window");
        Check(state.Phase == MeleePhase.Idle && state.RecoveryRemaining == 0, "Attack finishes");
    }
    var skipped = Equipped(weapon);
    MeleeRules.TryStart(ref skipped, MeleeAttackKind.Heavy, true, false, out _);
    var entire = MeleeRules.Tick(ref skipped, 10, false);
    Check(entire.Valid && entire.Completed, "A long server step still exposes the entire active sweep");
    Close(entire.From, 0, "Skipped active starts at zero"); Close(entire.To, 1, "Skipped active ends at one");

    var cancelled = Equipped(weapon);
    MeleeRules.TryStart(ref cancelled, MeleeAttackKind.Heavy, true, false, out _);
    float paid = cancelled.Stamina, lockTime = cancelled.RecoveryRemaining;
    MeleeRules.Cancel(ref cancelled);
    Check(cancelled.Phase == MeleePhase.Idle && cancelled.CancelSequence == 1, "Interruption stops the active pose");
    Close(cancelled.Stamina, paid, "Interruption never refunds stamina");
    Close(cancelled.RecoveryRemaining, lockTime, "Interruption does not erase recovery");
    Check(!MeleeRules.TryStart(ref cancelled, MeleeAttackKind.Light, true, false, out _), "Cancel cannot bypass attack cadence");
    MeleeRules.Cancel(ref cancelled);
    Check(cancelled.CancelSequence == 1, "Repeated cancellation is idempotent");
    MeleeRules.Select(ref cancelled, MeleeWeaponKind.None); MeleeRules.Select(ref cancelled, weapon);
    Check(!MeleeRules.TryStart(ref cancelled, MeleeAttackKind.Light, true, false, out _), "Weapon switching cannot bypass recovery");
    Check(!MeleeRules.Tick(ref cancelled, lockTime + .1f, false).Valid, "Cancelled attack never resumes damage");
    Check(MeleeRules.TryStart(ref cancelled, MeleeAttackKind.Light, true, false, out _), "Recovery eventually permits another attack");
}

var combo = Equipped(MeleeWeaponKind.Dagger);
for (int i = 0; i < 4; i++)
{
    Check(MeleeRules.TryStart(ref combo, MeleeAttackKind.Light, true, true, out _), "Light combo starts");
    Check(combo.ComboIndex == i % 3, "Three light variants cycle in order");
    MeleeRules.Tick(ref combo, MeleeRules.Timing(combo).Total + .01f, true);
}
MeleeRules.Tick(ref combo, MeleeRules.ComboGraceSeconds + .1f, true);
MeleeRules.TryStart(ref combo, MeleeAttackKind.Light, true, false, out _);
Check(combo.ComboIndex == 0, "A deliberate pause resets the combo");

var tired = Equipped(MeleeWeaponKind.EnergySword); tired.Stamina = 0;
Check(!MeleeRules.TryStart(ref tired, MeleeAttackKind.Heavy, true, false, out var exhausted) && exhausted == MeleeStartError.Exhausted,
    "Heavy attack requires sufficient combat stamina");
Check(MeleeRules.TryStart(ref tired, MeleeAttackKind.Heavy, true, true, out _), "Actual stamina boost permits an exhausted heavy attack");
Close(tired.Stamina, 100, "Boost neither consumes nor invents a separate buff timer");
MeleeRules.Tick(ref tired, 10, false); Close(tired.Stamina, 100, "Rest restores stamina within capacity");

var invalid = Equipped(MeleeWeaponKind.None);
Check(!MeleeRules.TryStart(ref invalid, MeleeAttackKind.Light, true, false, out _), "A firearm mode cannot issue a melee attack");
MeleeRules.Select(ref invalid, MeleeWeaponKind.Fists);
Check(!MeleeRules.TryStart(ref invalid, MeleeAttackKind.Light, false, false, out var inactive) && inactive == MeleeStartError.Inactive,
    "Inactive/downed actors cannot attack");
MeleeRules.TryStart(ref invalid, MeleeAttackKind.Light, true, false, out _);
float age = invalid.Elapsed;
Check(!MeleeRules.Tick(ref invalid, float.NaN, false).Valid, "Invalid delta time cannot cause damage");
Close(invalid.Elapsed, age, "Invalid delta time leaves time unchanged");
// Authored action contract only; execution remains separate from real collider/animation validation.
foreach (var item in new[]
{
    (MeleeWeaponKind.Fists, MeleeAttackKind.Light, 0, MeleeTrajectoryKind.ForwardThrust),
    (MeleeWeaponKind.Fists, MeleeAttackKind.Light, 1, MeleeTrajectoryKind.ForwardThrust),
    (MeleeWeaponKind.Fists, MeleeAttackKind.Light, 2, MeleeTrajectoryKind.LeftToRight),
    (MeleeWeaponKind.Fists, MeleeAttackKind.Heavy, 0, MeleeTrajectoryKind.ForwardThrust),
    (MeleeWeaponKind.Dagger, MeleeAttackKind.Light, 0, MeleeTrajectoryKind.RightToLeft),
    (MeleeWeaponKind.Dagger, MeleeAttackKind.Light, 1, MeleeTrajectoryKind.LeftToRight),
    (MeleeWeaponKind.Dagger, MeleeAttackKind.Light, 2, MeleeTrajectoryKind.ForwardThrust),
    (MeleeWeaponKind.Dagger, MeleeAttackKind.Heavy, 0, MeleeTrajectoryKind.ForwardThrust),
    (MeleeWeaponKind.EnergySword, MeleeAttackKind.Light, 0, MeleeTrajectoryKind.UpperRightToLowerLeft),
    (MeleeWeaponKind.EnergySword, MeleeAttackKind.Light, 1, MeleeTrajectoryKind.UpperLeftToLowerRight),
    (MeleeWeaponKind.EnergySword, MeleeAttackKind.Light, 2, MeleeTrajectoryKind.ForwardThrust),
    (MeleeWeaponKind.EnergySword, MeleeAttackKind.Heavy, 0, MeleeTrajectoryKind.Downward)
}) Check(MeleeRules.Trajectory(item.Item1, item.Item2, item.Item3) == item.Item4, "Authored melee action selects its own trajectory");
Console.WriteLine($"Melee rules: {checks} checks passed.");
