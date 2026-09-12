namespace squad.AgentProvider.CopilotSdk;

/// <summary>
/// One provider-side pending interaction awaiting a response from the operator or dashboard, keyed only by
/// <see cref="squad.Domain.InteractionRequestId"/> in <see cref="CopilotSdkAgentSession"/>. The non-generic surface
/// lets the session cancel or fail every pending interaction uniformly regardless of its request kind; only the
/// typed <see cref="Typed{TResponse}"/> variant knows how to complete with its specific response type, so a
/// response whose type does not match the registered interaction leaves the real pending completion untouched.
/// </summary>
internal abstract class PendingProviderInteraction
{
    internal abstract void Cancel(CancellationToken cancellationToken);

    internal abstract void Fail(Exception exception);

    internal sealed class Typed<TResponse> : PendingProviderInteraction
    {
        private readonly TaskCompletionSource<TResponse> myCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task<TResponse> Task => myCompletion.Task;

        internal void Complete(TResponse response) => myCompletion.TrySetResult(response);

        internal override void Cancel(CancellationToken cancellationToken) => myCompletion.TrySetCanceled(cancellationToken);

        internal override void Fail(Exception exception) => myCompletion.TrySetException(exception);
    }
}
