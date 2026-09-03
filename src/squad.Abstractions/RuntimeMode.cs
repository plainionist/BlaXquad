namespace squad.Abstractions;

public sealed record RuntimeMode(
    IAgentBackend AgentBackend,
    IWindowHost WindowHost,
    ISleepInhibitor SleepInhibitor,
    Func<CancellationToken, Task> PrepareAsync);



