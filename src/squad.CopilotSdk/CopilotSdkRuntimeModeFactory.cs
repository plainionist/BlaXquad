using global::squad.AgentProvider.Abstractions;

namespace squad.CopilotSdk;

public sealed class CopilotSdkRuntimeModeFactory : IRuntimeModeFactory
{
    public string Name => "sdk";
    public bool IsAvailable => true;
    public bool UsesPhotino => true;

    public IAgentBackend CreateBackend(Func<AgentBackendContext> context) =>
        new CopilotSdkBackend(context);

    public Task PrepareAsync(Func<AgentBackendContext> context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public bool TryRunPrivateCommand(string command, string[] arguments, out int exitCode)
    {
        switch (command)
        {
            case "test-context-window":
                Console.WriteLine(CopilotSdkRuntimeSession.CalculateContextLimit(
                    long.Parse(arguments[0]),
                    long.Parse(arguments[1]),
                    null,
                    null));
                exitCode = 0;
                return true;
            case "test-context-window-cache":
                var lookupCount = 0;
                var cache = CopilotSdkRuntimeSession.CreateContextLimitCache(() =>
                {
                    lookupCount++;
                    return Task.FromResult(528000L);
                });
                _ = cache.Value.GetAwaiter().GetResult();
                _ = cache.Value.GetAwaiter().GetResult();
                Console.WriteLine(lookupCount);
                exitCode = 0;
                return true;
            default:
                exitCode = 0;
                return false;
        }
    }
}



