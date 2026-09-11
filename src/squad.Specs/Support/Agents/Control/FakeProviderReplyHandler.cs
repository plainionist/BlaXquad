namespace squad.Specs.Support.Agents.Control;

/// <summary>
/// Handles one "reply" command pushed from <see cref="FakeProviderControlServer"/> to
/// <see cref="FakeProviderControlClient"/> for the given role and session id, delivering the given assistant
/// content to whatever owns that session. Returns null on success, or an explicit diagnostic message - "unknown
/// role or session" or "session has been disposed" - that the client turns into a protocol-error reply and the
/// server, in turn, surfaces as an <see cref="InvalidOperationException"/> to the caller of
/// <see cref="FakeProviderControlServer.ReplyAsync"/>.
/// </summary>
internal delegate Task<string?> FakeProviderReplyHandler(string role, string sessionId, string content, CancellationToken cancellationToken);
