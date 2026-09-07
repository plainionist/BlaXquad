using squad.AgentProvider.Abstractions;
using squad.Hosting.Abstractions;

namespace squadHQ.Commands;

/// <summary>Bundles the selected provider factory and platform resources selected by headquarters for one launch.</summary>
internal sealed record RuntimeMode(
    IAgentProviderFactory AgentProviderFactory,
    IWindowHost WindowHost,
    ISleepInhibitor SleepInhibitor);


