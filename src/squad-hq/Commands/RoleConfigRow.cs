namespace squadHQ.Commands;

public record RoleConfigRow(
    string Role,
    string DisplayName,
    string WorktreeName,
    string WorktreePath,
    string ReceiveMode,
    string Permissions = "prompt",
    string? Model = null,
    string? Effort = null);



