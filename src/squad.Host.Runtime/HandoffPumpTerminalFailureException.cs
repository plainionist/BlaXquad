namespace squad.Host.Runtime;

/// <summary>
/// Wraps a fatal <see cref="squad.Handoffs.Delivery.InProcessHandoffPoller.Failure"/> so callers can report it as the
/// handoff pump's own terminal failure instead of mislabeling it as a startup failure.
/// </summary>
public sealed class HandoffPumpTerminalFailureException(string message, Exception innerException)
    : Exception(message, innerException);
