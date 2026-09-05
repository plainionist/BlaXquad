using global::squad.Handoffs;

namespace squad.Specs.Support;

public sealed class ObservingHandoffPump : IHandoffPump
{
    private readonly IHandoffPump myInner;
    private readonly Action myOnStart;

    public ObservingHandoffPump(IHandoffPump inner, Action onStart)
    {
        myInner = inner;
        myOnStart = onStart;
    }

    public Task Failure => myInner.Failure;
    public Task RecoverAsync(CancellationToken cancellationToken = default) =>
        myInner.RecoverAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        myOnStart();
        return myInner.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        myInner.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => myInner.DisposeAsync();
}



