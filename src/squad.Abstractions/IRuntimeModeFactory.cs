namespace squad.Abstractions;

public interface IRuntimeModeFactory
{
    string Name { get; }
    bool IsAvailable { get; }
    bool UsesPhotino { get; }
    IAgentBackend CreateBackend(Func<AgentBackendContext> context);
    Task PrepareAsync(Func<AgentBackendContext> context, CancellationToken cancellationToken);
    bool TryRunPrivateCommand(string command, string[] arguments, out int exitCode);
}



