namespace squad.Core.Handoffs;

public interface IRoleNotifier
{
    Task NotifyAsync(string role, CancellationToken cancellationToken = default);
}



