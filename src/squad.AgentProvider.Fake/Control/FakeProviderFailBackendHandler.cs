namespace squad.AgentProvider.Fake.Control;

/// <summary>Handles one "fail-backend" command pushed from <see cref="FakeProviderControlServer"/> to
/// <see cref="FakeProviderControlClient"/>, faulting the fake provider's backend-wide
/// <see cref="squad.AgentProvider.Abstractions.IAgentBackendFailureSource.Failure"/> task with the given message -
/// mirroring a real provider's fatal, backend-wide failure that is independent of any individual role's session.
/// Never fails itself: this command names no role or session, so it has no "unknown session" diagnostic to
/// return.</summary>
internal delegate Task FakeProviderFailBackendHandler(string message, CancellationToken cancellationToken);
