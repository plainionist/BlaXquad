using squad.Handoffs;

namespace squad.Specs.Support;

public sealed class RecordingRoleNotifier : IRoleNotifier
{
    public const string WakeMessage = "You have new handoff mail. If idle, run squad ready-for-next.";

    public bool Fail { get; set; }
    public List<(string Recipient, string Message)> Notifications { get; } = [];

    public Task NotifyAsync(string role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Notifications.Add((role, WakeMessage));
        return Fail
            ? Task.FromException(new InvalidOperationException("notifier failed"))
            : Task.CompletedTask;
    }
}



