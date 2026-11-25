using Unity.MP_FPS;

public sealed record ResolveProfile(string GuestToken, string DisplayName);

public sealed record Settlement(
    Guid PlayerId,
    Guid SettlementId,
    string Outcome,
    int Dust,
    int Alloy,
    int Cells,
    Guid? DeploymentId = null,
    string? InventoryJson = null,
    string? Mode = null,
    Guid? MatchId = null,
    int RulesVersion = 0,
    int TeamId = 0,
    Guid? BountyEventId = null,
    ResearchQuestResult? ResearchQuests = null);

public sealed record Profile(
    Guid PlayerId,
    int Dust,
    int Alloy,
    int Cells,
    string DisplayName,
    Guid? DeploymentId = null,
    int CarriedCells = 0,
    Guid? CellStackId = null,
    IReadOnlyList<InventoryStack>? Items = null,
    string? InventoryJson = null,
    string? RaidInventoryJson = null,
    Guid? ActiveDeploymentId = null,
    int MissionSchemaVersion = 1,
    string? RecoveryJson = null,
    string? Mode = null,
    Guid? MatchId = null,
    int RulesVersion = 0,
    int TeamId = 0,
    ResearchQuestProgress? ResearchQuests = null);

public sealed class ReceiptConflictException : Exception;
public sealed class ProfileNotFoundException : Exception;
