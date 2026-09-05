namespace squad.Handoffs;

public interface IRoleNotifier
{
    Task NotifyAsync(string role, CancellationToken cancellationToken = default);
}



