using squad.AgentProvider.Abstractions;
using System.Text.Json;
using squad.Specs.Support.Agents.Control;

namespace squad.Specs.Support.Agents;

/// <summary>
/// Provider-side runtime for <see cref="FakeAgentProviderFactory"/>. Establishes one <see cref="FakeAgentSession"/>
/// per configured role through the production startup callback, and disposes every session it created - the
/// normal production <see cref="IAgentRuntime"/> lifecycle this slice needs to prove. When the environment names a
/// fake-provider control pipe (<see cref="FakeProviderControlServer.PipeNameEnvironmentVariable"/>), also connects
/// to it and reports every session start and disposal across it; otherwise behaves exactly as it did before the
/// control transport existed. Also routes the control pipe's "fail-backend" command to the given
/// <paramref name="failBackend"/> handler, letting a specification fault
/// <see cref="squad.AgentProvider.Abstractions.IAgentBackendFailureSource.Failure"/> on the owning
/// <see cref="FakeAgentBackend"/> at any point after this runtime connects - independent of any individual role's
/// session. Mirrors the real <c>squad.AgentProvider.CopilotSdk</c> runtime by sending each role's configured
/// initial instruction as a harness message once its session starts, so the "harness messages" event family is
/// observable through the same real session lifecycle a production provider uses - never a product test hook.
/// </summary>
internal sealed class FakeAgentRuntime(AgentBackendContext context, FakeProviderFailBackendHandler failBackend) : IAgentRuntime
{
    private readonly List<FakeAgentSession> mySessions = [];
    private FakeProviderControlClient? myControl;

    public async Task StartAsync(Func<IAgentSession, Task> sessionStarted, CancellationToken cancellationToken = default)
    {
        myControl = await FakeProviderControlClient.ConnectIfConfiguredAsync(
            HandleReplyAsync, HandleEmitAsync, failBackend, cancellationToken);

        var gateAfterSessions = ReadStartupGateAfterSessions();
        var failAfterSessions = ReadFailAfterSessions();
        var sessionIndex = 0;
        foreach (var role in context.Roles)
        {
            if (gateAfterSessions == sessionIndex)
            {
                // Blocks on the same cancellation token SquadRuntimeController.StartAsync was given, which
                // SquadApplication.RunAsync cancels the instant its own shutdown-vs-startup race resolves in
                // shutdown's favor - proving a host-control shutdown requested while provider startup is paused
                // here still terminates cleanly and disposes every session already registered above, without
                // ever needing a synthetic pause a production caller could actually observe.
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var session = new FakeAgentSession(role.Role, myControl);
            mySessions.Add(session);
            await sessionStarted(session);
            if (myControl is not null)
            {
                await myControl.NotifySessionStartedAsync(session.Role, session.SessionId, cancellationToken);
            }
            await session.SendHarnessAsync(role.InitialInstruction, cancellationToken);
            sessionIndex++;
            if (failAfterSessions == sessionIndex)
            {
                // Mirrors a real provider runtime throwing partway through establishing sessions: every session
                // already started above (and reported across the control pipe) stays registered with
                // SquadApplication so its normal teardown still disposes it, while every role not yet reached is
                // never started at all.
                throw new InvalidOperationException($"fake provider failed after starting {sessionIndex} session(s)");
            }
        }
    }

    /// <summary>Reads the test-owned startup gate position from the environment - the number of sessions that
    /// must already be registered before this runtime pauses - or null if no gate was configured, matching
    /// ordinary behavior exactly for every specification that never sets it.</summary>
    private static int? ReadStartupGateAfterSessions()
    {
        var raw = Environment.GetEnvironmentVariable(FakeProviderControlServer.StartupGateAfterSessionsEnvironmentVariable);
        return int.TryParse(raw, out var value) ? value : null;
    }

    /// <summary>Reads the test-owned failure position from the environment - the number of sessions that must
    /// already be started before this runtime throws - or null if no failure was configured, matching ordinary
    /// behavior exactly for every specification that never sets it.</summary>
    private static int? ReadFailAfterSessions()
    {
        var raw = Environment.GetEnvironmentVariable(FakeProviderControlServer.FailAfterSessionsEnvironmentVariable);
        return int.TryParse(raw, out var value) ? value : null;
    }

    /// <summary>Routes one "reply" pushed across the control pipe to whichever live session it names, returning
    /// an explicit diagnostic instead if no such session exists or it has already been disposed.</summary>
    private Task<string?> HandleReplyAsync(string role, string sessionId, string content, CancellationToken cancellationToken)
    {
        var session = FindSession(role, sessionId, out var error);
        if (session is null)
        {
            return Task.FromResult(error);
        }
        session.DeliverReply(content);
        return Task.FromResult<string?>(null);
    }

    /// <summary>Routes one "emit" pushed across the control pipe to whichever live session it names, returning an
    /// explicit diagnostic instead if no such session exists, it has already been disposed, or the given kind is
    /// unsupported.</summary>
    private Task<string?> HandleEmitAsync(string role, string sessionId, string kind, JsonElement data, CancellationToken cancellationToken)
    {
        var session = FindSession(role, sessionId, out var error);
        return Task.FromResult(session is null ? error : session.Emit(kind, data));
    }

    private FakeAgentSession? FindSession(string role, string sessionId, out string? error)
    {
        var session = mySessions.FirstOrDefault(candidate => candidate.Role == role && candidate.SessionId == sessionId);
        if (session is null)
        {
            error = $"No session '{sessionId}' for role '{role}' exists.";
            return null;
        }
        if (session.IsDisposed)
        {
            error = $"Session '{sessionId}' for role '{role}' has been disposed.";
            return null;
        }
        error = null;
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in mySessions)
        {
            await session.DisposeAsync();
            if (myControl is not null)
            {
                await myControl.NotifySessionDisposedAsync(session.Role, session.SessionId, CancellationToken.None);
            }
        }
        if (myControl is not null)
        {
            await myControl.DisposeAsync();
        }

        // Read only now - after every session this runtime owned has already been genuinely disposed (and
        // reported disposed) and the control client has already disconnected - so a configured failure here
        // proves cleanup continued through every owned resource first, rather than short-circuiting it, and
        // still surfaces this runtime's own retirement failure with a distinct diagnostic independent of
        // whatever primary startup or runtime failure (if any) is what triggered cleanup in the first place.
        var disposalFailureMessage = ReadDisposalFailureMessage();
        if (disposalFailureMessage is not null)
        {
            throw new InvalidOperationException(disposalFailureMessage);
        }
    }

    /// <summary>Reads the test-owned disposal-failure message from the environment - the distinct diagnostic this
    /// runtime's own disposal must fail with - or null if none was configured, matching ordinary behavior exactly
    /// for every specification that never sets it.</summary>
    private static string? ReadDisposalFailureMessage() =>
        Environment.GetEnvironmentVariable(FakeProviderControlServer.FailDisposalMessageEnvironmentVariable);
}

