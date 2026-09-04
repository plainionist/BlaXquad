using squad.AgentProvider.Abstractions;
using squad.Hosting.Abstractions;

namespace squad_hq.Commands;

internal sealed record RuntimeMode(
    IAgentBackend AgentBackend,
    IWindowHost WindowHost,
    ISleepInhibitor SleepInhibitor,
    Func<CancellationToken, Task> PrepareAsync);



