namespace squad.Handoffs.Delivery;

public interface IRoleNotifier
{
    Task NotifyAsync(string role, CancellationToken cancellationToken = default);
}



