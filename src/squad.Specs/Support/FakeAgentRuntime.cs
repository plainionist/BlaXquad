using squad.AgentProvider.Abstractions;
using System.Text.Json;

namespace squad.Specs.Support;

/// <summary>
/// Provider-side runtime for <see cref="FakeAgentProviderFactory"/>. Establishes one <see cref="FakeAgentSession"/>
/// per configured role through the production startup callback, and disposes every session it created - the
/// normal production <see cref="IAgentRuntime"/> lifecycle this slice needs to prove. When the environment names a
/// fake-provider control pipe (<see cref="FakeProviderControlServer.PipeNameEnvironmentVariable"/>), also connects
/// to it and reports every session start and disposal across it; otherwise behaves exactly as it did before the
/// control transport existed. Mirrors the real <c>squad.CopilotSdk</c> runtime by sending each role's configured
/// initial instruction as a harness message once its session starts, so the "harness messages" event family is
/// observable through the same real session lifecycle a production provider uses - never a product test hook.
/// </summary>
internal sealed class FakeAgentRuntime(AgentBackendContext context) : IAgentRuntime
{
    private readonly List<FakeAgentSession> mySessions = [];
    private FakeProviderControlClient? myControl;

    public async Task StartAsync(Func<IAgentSession, Task> sessionStarted, CancellationToken cancellationToken = default)
    {
        myControl = await FakeProviderControlClient.ConnectIfConfiguredAsync(HandleReplyAsync, HandleEmitAsync, cancellationToken);

        foreach (var role in context.Roles)
        {
            var session = new FakeAgentSession(role.Role, myControl);
            mySessions.Add(session);
            await sessionStarted(session);
            if (myControl is not null)
            {
                await myControl.NotifySessionStartedAsync(session.Role, session.SessionId, cancellationToken);
            }
            await session.SendHarnessAsync(role.InitialInstruction, cancellationToken);
        }
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
    }
}

