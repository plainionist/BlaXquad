using squad.AgentProvider.Abstractions;

namespace squad.AgentProvider.CopilotSdk;

/// <summary>
/// Creates independent Copilot SDK runtime generations and surfaces failures that require the shared provider
/// process to be abandoned.
/// </summary>
internal sealed class CopilotSdkBackend : IAgentBackend, IAgentBackendFailureSource
{
    private readonly AgentBackendContext myContext;
    private readonly TaskCompletionSource myFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CopilotSdkBackend(AgentBackendContext context)
    {
        myContext = context;
    }

    public Task Failure => myFailure.Task;

    public async Task<IAgentRuntime> CreateRuntimeAsync(CancellationToken cancellationToken = default)
    {
        var client = await CopilotSdkClient.StartAsync(myContext.WorkingDirectory, myContext.Environment, cancellationToken);
        return new CopilotSdkAgentRuntime(client, myContext, ReportFatalFailure);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void ReportFatalFailure(Exception fatalFailure) => myFailure.TrySetException(fatalFailure);
}
