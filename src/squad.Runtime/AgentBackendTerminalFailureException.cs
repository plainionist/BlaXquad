namespace squad.Runtime;

/// <summary>
/// Wraps a fatal, backend-wide <see cref="squad.AgentProvider.Abstractions.IAgentBackendFailureSource"/> failure so
/// callers can report it as the provider's own terminal failure instead of mislabeling it as a startup failure.
/// </summary>
public sealed class AgentBackendTerminalFailureException(string message, Exception innerException)
    : Exception(message, innerException);
