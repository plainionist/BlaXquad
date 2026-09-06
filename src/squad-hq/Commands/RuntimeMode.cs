using squad.AgentProvider.Abstractions;
using squad.Hosting.Abstractions;

namespace squadHQ.Commands;

/// <summary>Bundles the provider and platform resources selected by headquarters for one launch.</summary>
internal sealed record RuntimeMode(
    IAgentBackend AgentBackend,
    IWindowHost WindowHost,
    ISleepInhibitor SleepInhibitor,
    Func<CancellationToken, Task> PrepareAsync);


