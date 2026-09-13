using squad.Domain;
using System.Text.Json;

namespace squad.AgentProvider.Fake.Control;

/// <summary>
/// Synchronized observation state for one <see cref="FakeProviderControlServer"/> connection: the session
/// lifecycle list (in arrival order), the currently active session id per role, the latest prompt and latest
/// generic observation per role/kind with running counts, and every protocol error the server has rejected a
/// request with. Owns every bounded wait over that state, undisposed-session reporting, and the single
/// diagnostics rendering every timeout is built from, so <see cref="FakeProviderControlServer"/> itself stays the
/// authenticated protocol/semantic command owner without also tracking or polling this state directly.
/// </summary>
internal sealed class ObservationJournal
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    private readonly object myLock = new();
    private readonly List<(SquadMemberId MemberId, string Type, string SessionId)> myObservations = [];
    private readonly List<string> myProtocolErrors = [];
    private readonly Dictionary<SquadMemberId, MemberObservationJournal> myMembers = new();

    /// <summary>Records a session-lifecycle notification ("session-started" or "session-disposed") for the given
    /// role and session id, marking that session as the role's active one when it started.</summary>
    public void RecordLifecycle(SquadMemberId memberId, string type, string sessionId)
    {
        lock (myLock)
        {
            myObservations.Add((memberId, type, sessionId));

            if (type == "session-started")
            {
                GetMember(memberId).ActiveSessionId = sessionId;
            }

        }
    }

    /// <summary>Records the latest prompt reported for the given role.</summary>
    public void RecordPrompt(SquadMemberId memberId, string prompt)
    {
        lock (myLock)
        {
            GetMember(memberId).LatestPrompt = prompt;
        }
    }

    /// <summary>Records one generic observation of the given kind for the given role, replacing any earlier one
    /// of the same kind and incrementing its running count.</summary>
    public void RecordObservation(SquadMemberId memberId, string kind, JsonElement data)
    {
        lock (myLock)
        {
            var member = GetMember(memberId);

            if (member.Observations.TryGetValue(kind, out var state))
            {
                state.Record(data.Clone());
            }
            else
            {
                member.Observations[kind] = new ObservationState(data.Clone());
            }

        }
    }

    /// <summary>Records one protocol error message this server rejected a request with.</summary>
    public void RecordProtocolError(string message)
    {
        lock (myLock)
        {
            myProtocolErrors.Add(message);
        }
    }

    /// <summary>Returns the session id currently active for the given role, or false if none has ever been
    /// observed.</summary>
    public bool TryGetActiveSession(string role, out string sessionId)
    {
        lock (myLock)
        {

            if (myMembers.TryGetValue(new SquadMemberId(role), out var member) && member.ActiveSessionId is not null)
            {
                sessionId = member.ActiveSessionId;
                return true;
            }

            sessionId = null!;
            return false;
        }
    }

    /// <summary>Whether a "session-started" notification has ever been observed for the given role - a snapshot
    /// read (no waiting) so a specification can prove a session was never created, not merely that it has not yet
    /// been observed.</summary>
    public bool HasSessionStarted(string role)
    {
        var memberId = new SquadMemberId(role);
        lock (myLock)
        {
            return myObservations.Any(observation => observation.MemberId == memberId && observation.Type == "session-started");
        }
    }

    /// <summary>Returns whether a generic observation of the given kind has been reported for the given role,
    /// without waiting - a snapshot read used to prove another role's owning session never observed a response
    /// addressed to a different role.</summary>
    public bool HasObservation(string role, string kind)
    {
        lock (myLock)
        {
            return myMembers.TryGetValue(new SquadMemberId(role), out var member)
                && member.Observations.ContainsKey(kind);
        }
    }

    /// <summary>Returns the content of the most recent prompt reported for the given role, or null if none has
    /// been reported yet - a snapshot read (no waiting).</summary>
    public string? LatestPrompt(string role)
    {
        lock (myLock)
        {
            return myMembers.TryGetValue(new SquadMemberId(role), out var member) ? member.LatestPrompt : null;
        }
    }

    /// <summary>Returns the content of the most recent harness message reported for the given role, or null if
    /// none has been reported yet - a snapshot read (no waiting).</summary>
    public string? LatestHarnessMessage(string role)
    {
        lock (myLock)
        {
            return myMembers.TryGetValue(new SquadMemberId(role), out var member)
                && member.Observations.TryGetValue("harness-message", out var state)
                ? state.Data.GetProperty("content").GetString()
                : null;
        }
    }

    /// <summary>Waits until a lifecycle notification of the given type has been recorded for the given role.</summary>
    public async Task WaitForLifecycleAsync(string role, string type, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var memberId = new SquadMemberId(role);
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (true)
        {
            lock (myLock)
            {

                if (myObservations.Any(observation => observation.MemberId == memberId && observation.Type == type))
                {
                    return;
                }

            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out waiting for role '{role}' to report '{type}' across the fake-provider control pipe.\n{DescribeDiagnostics(additionalDiagnostics)}");
            }

            await Task.Delay(PollInterval);
        }
    }

    /// <summary>Waits until a prompt has been reported for the given role, and returns its content.</summary>
    public async Task<string> WaitForPromptAsync(string role, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var memberId = new SquadMemberId(role);
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (true)
        {
            lock (myLock)
            {

                if (myMembers.TryGetValue(memberId, out var member) && member.LatestPrompt is not null)
                {
                    return member.LatestPrompt;
                }

            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out waiting for role '{role}' to report a prompt across the fake-provider control pipe.\n{DescribeDiagnostics(additionalDiagnostics)}");
            }

            await Task.Delay(PollInterval);
        }
    }

    /// <summary>Waits until a prompt satisfying the given predicate has been reported for the given role. Unlike
    /// <see cref="WaitForPromptAsync(string,TimeSpan?,Func{string}?)"/>, this keeps polling past an
    /// already-observed prompt that does not satisfy the predicate, so a caller can distinguish a later, distinct
    /// prompt from an earlier one.</summary>
    public async Task<string> WaitForPromptAsync(string role, Func<string, bool> matches, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (true)
        {
            var prompt = LatestPrompt(role);

            if (prompt is not null && matches(prompt))
            {
                return prompt;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out waiting for role '{role}' to report a matching prompt across the fake-provider control pipe.\n{DescribeDiagnostics(additionalDiagnostics)}");
            }

            await Task.Delay(PollInterval);
        }
    }

    /// <summary>Waits until a harness message has been reported for the given role, and returns its content.</summary>
    public async Task<string> WaitForHarnessMessageAsync(string role, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var data = await WaitForObservationDataAsync(role, "harness-message", timeout, additionalDiagnostics);
        return data.GetProperty("content").GetString()!;
    }

    /// <summary>Waits until a harness message satisfying the given predicate has been reported for the given
    /// role. Unlike <see cref="WaitForHarnessMessageAsync(string,TimeSpan?,Func{string}?)"/>, this keeps polling
    /// past an already-observed harness message that does not satisfy the predicate (such as the role's own
    /// initial instruction, sent once at session start), so a caller can distinguish a later, distinct harness
    /// message - for example a delivery wake-up - from that earlier one.</summary>
    public async Task<string> WaitForHarnessMessageAsync(
        string role, Func<string, bool> matches, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (true)
        {
            var content = LatestHarnessMessage(role);

            if (content is not null && matches(content))
            {
                return content;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out waiting for role '{role}' to report a matching harness message across the fake-provider control pipe.\n{DescribeDiagnostics(additionalDiagnostics)}");
            }

            await Task.Delay(PollInterval);
        }
    }

    /// <summary>Waits until the given role's session has reported rejecting a harness send, and returns its
    /// content.</summary>
    public async Task<string> WaitForHarnessRejectedAsync(string role, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var data = await WaitForObservationDataAsync(role, "harness-rejected", timeout, additionalDiagnostics);
        return data.GetProperty("content").GetString()!;
    }

    /// <summary>Waits until an abort has been reported for the given role.</summary>
    public async Task WaitForAbortAsync(string role, TimeSpan? timeout, Func<string>? additionalDiagnostics) =>
        await WaitForObservationDataAsync(role, "abort", timeout, additionalDiagnostics);

    /// <summary>Waits until at least the given number of distinct aborts have been reported for the given role -
    /// proving a repeated abort produced a genuinely new observation rather than re-matching an earlier one
    /// already reported (unlike <see cref="WaitForAbortAsync"/>, which only ever inspects the latest).</summary>
    public Task WaitForAbortCountAsync(string role, int minimumCount, TimeSpan? timeout, Func<string>? additionalDiagnostics) =>
        WaitForObservationCountAsync(role, "abort", minimumCount, timeout, additionalDiagnostics);

    /// <summary>Waits until the host cancelling this role's pending interactions has been reported.</summary>
    public async Task WaitForPendingInteractionsCancelledAsync(string role, TimeSpan? timeout, Func<string>? additionalDiagnostics) =>
        await WaitForObservationDataAsync(role, "pending-interactions-cancelled", timeout, additionalDiagnostics);

    /// <summary>Waits until a response to a permission request has been reported for the given role, and returns
    /// the request id and whether it was approved.</summary>
    public async Task<(string RequestId, bool Approved)> WaitForPermissionResponseAsync(
        string role, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var data = await WaitForObservationDataAsync(role, "permission-response", timeout, additionalDiagnostics);
        return (data.GetProperty("requestId").GetString()!, data.GetProperty("approved").GetBoolean());
    }

    /// <summary>Waits until a response to an input request has been reported for the given role, and returns the
    /// request id, the answer (or null if none was given), and whether it was freeform.</summary>
    public async Task<(string RequestId, string? Answer, bool WasFreeform)> WaitForInputResponseAsync(
        string role, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var data = await WaitForObservationDataAsync(role, "input-response", timeout, additionalDiagnostics);
        return (
            data.GetProperty("requestId").GetString()!,
            data.TryGetProperty("answer", out var answer) && answer.ValueKind != JsonValueKind.Null ? answer.GetString() : null,
            data.GetProperty("wasFreeform").GetBoolean());
    }

    /// <summary>Waits until a response to an elicitation request has been reported for the given role, and
    /// returns the request id, the chosen action, and the accepted content (or null if none was given).</summary>
    public async Task<(string RequestId, string Action, JsonElement? Content)> WaitForElicitationResponseAsync(
        string role, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var data = await WaitForObservationDataAsync(role, "elicitation-response", timeout, additionalDiagnostics);
        return (
            data.GetProperty("requestId").GetString()!,
            data.GetProperty("action").GetString()!,
            data.TryGetProperty("content", out var content) && content.ValueKind != JsonValueKind.Null ? content : null);
    }

    /// <summary>Waits until the given role's session has reported that its disposal is being held, and returns
    /// whether an admitted send on this same session had already reached its own canceled terminal outcome by the
    /// moment disposal began.</summary>
    public async Task<bool> WaitForDisposalHeldAsync(string role, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var data = await WaitForObservationDataAsync(role, "disposal-held", timeout, additionalDiagnostics);
        return data.TryGetProperty("sendCanceledBeforeDisposal", out var flag) && flag.GetBoolean();
    }

    /// <summary>Waits until at least the given number of observations of the given kind have been reported for
    /// the given role.</summary>
    private async Task WaitForObservationCountAsync(
        string role, string kind, int minimumCount, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var memberId = new SquadMemberId(role);
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (true)
        {
            lock (myLock)
            {

                if (myMembers.TryGetValue(memberId, out var member)
                    && member.Observations.TryGetValue(kind, out var state)
                    && state.Count >= minimumCount)
                {

                    return;
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out waiting for role '{role}' to report at least {minimumCount} '{kind}' observations across the fake-provider control pipe.\n{DescribeDiagnostics(additionalDiagnostics)}");
            }

            await Task.Delay(PollInterval);
        }
    }

    /// <summary>Waits until a generic observation of the given kind has been reported for the given role, and
    /// returns its kind-specific data.</summary>
    private async Task<JsonElement> WaitForObservationDataAsync(
        string role, string kind, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var memberId = new SquadMemberId(role);
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (true)
        {
            lock (myLock)
            {

                if (myMembers.TryGetValue(memberId, out var member)
                    && member.Observations.TryGetValue(kind, out var state))
                {

                    return state.Data.Clone();
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out waiting for role '{role}' to report '{kind}' across the fake-provider control pipe.\n{DescribeDiagnostics(additionalDiagnostics)}");
            }

            await Task.Delay(PollInterval);
        }
    }

    /// <summary>Describes every role whose session was observed to start but never observed to be disposed, or
    /// null if none - so a scenario's emergency teardown can flag a leaked session instead of silently discarding
    /// it.</summary>
    public string? DescribeUndisposedSessions()
    {
        lock (myLock)
        {
            var started = myObservations.Where(observation => observation.Type == "session-started")
                .Select(observation => (observation.MemberId, observation.SessionId));
            var disposed = myObservations.Where(observation => observation.Type == "session-disposed")
                .Select(observation => (observation.MemberId, observation.SessionId))
                .ToHashSet();
            var leaked = started.Where(session => !disposed.Contains(session)).ToList();
            return leaked.Count == 0
                ? null
                : string.Join('\n', leaked.Select(session => $"role='{session.MemberId}' session='{session.SessionId}'"));
        }
    }

    /// <summary>
    /// Builds a diagnostics snapshot of every session-lifecycle observation, latest prompt per role, and
    /// protocol error this journal has seen, so a bounded-wait timeout - or a caller beyond this journal's own
    /// waits - can report the same diagnostics without inspecting raw control-pipe traffic by hand.
    /// </summary>
    public string DescribeDiagnostics(Func<string>? additionalDiagnostics)
    {
        lock (myLock)
        {
            var observations = myObservations.Count switch
            {
                0 => "(none)",
                <= 10 => string.Join('\n', myObservations.Select(o => $"{o.Type} role='{o.MemberId}' session='{o.SessionId}'")),
                _ => $"... ({myObservations.Count - 10} earlier observations omitted)\n"
                    + string.Join('\n', myObservations.TakeLast(10).Select(o => $"{o.Type} role='{o.MemberId}' session='{o.SessionId}'")),
            };
            var prompts = myMembers.Any(entry => entry.Value.LatestPrompt is not null)
                ? string.Join(
                    '\n',
                    myMembers
                        .Where(entry => entry.Value.LatestPrompt is not null)
                        .Select(entry => $"role='{entry.Key}' prompt='{entry.Value.LatestPrompt}'"))
                : "(none)";
            var genericObservations = myMembers.Any(entry => entry.Value.Observations.Count > 0)
                ? string.Join(
                    '\n',
                    myMembers.SelectMany(entry => entry.Value.Observations.Select(observation =>
                        $"role='{entry.Key}' kind='{observation.Key}' data={FormatObservationData(observation.Value.Data)}")))
                : "(none)";
            var protocolErrors = myProtocolErrors.Count == 0 ? "(none)" : string.Join('\n', myProtocolErrors);
            var provider = $"""
                Observations:
                {observations}
                Latest prompts:
                {prompts}
                Latest generic observations:
                {genericObservations}
                Protocol errors:
                {protocolErrors}
                """;
            return additionalDiagnostics is null ? provider : $"{provider}\n{additionalDiagnostics()}";
        }
    }

    private static string FormatObservationData(JsonElement data)
    {
        var raw = data.ToString();
        return raw.Length <= 160 ? raw : raw[..157] + "...";
    }

    /// <summary>Returns this role's member journal, creating an empty one on first use.</summary>
    private MemberObservationJournal GetMember(SquadMemberId memberId)
    {

        if (!myMembers.TryGetValue(memberId, out var member))
        {
            myMembers[memberId] = member = new MemberObservationJournal();
        }

        return member;
    }
}
