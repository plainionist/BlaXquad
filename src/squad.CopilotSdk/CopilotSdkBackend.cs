using global::squad.AgentProvider.Abstractions;

namespace squad.CopilotSdk;

public sealed class CopilotSdkBackend : IAgentBackend, IAgentBackendFailureSource
{
    private readonly Func<AgentBackendContext> myContext;
    private readonly TaskCompletionSource myFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CopilotSdkBackend(Func<AgentBackendContext> context)
    {
        myContext = context;
    }

    public Task Failure => myFailure.Task;

    public async Task<IAgentRuntime> CreateRuntimeAsync(CancellationToken cancellationToken = default)
    {
        var context = myContext();
        var client = await CopilotSdkClient.StartAsync(context.WorkingDirectory, context.Environment, cancellationToken);
        return new CopilotSdkAgentRuntime(client, context, ReportFatalFailure);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void ReportFatalFailure(Exception fatalFailure) => myFailure.TrySetException(fatalFailure);
}
