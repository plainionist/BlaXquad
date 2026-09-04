using global::squad.AgentProvider.Abstractions.Agents;

namespace squad.Core.Interactions;

/// <summary>
/// Owns pending permission, input, and elicitation requests plus the protected transcript entry each one holds
/// open. Requests are keyed by role and request ID, so the same request ID can be pending independently for
/// multiple roles at once. Every member takes and releases its own lock; callers must never hold this lock while
/// awaiting a session operation or acquiring a role lock.
/// </summary>
internal sealed class PendingInteractionRegistry
{
    private readonly object myLock = new();
    private readonly Dictionary<string, AgentPermissionRequest> myPermissions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AgentInputRequest> myInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AgentElicitationRequest> myElicitations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProtectedTranscriptEntry> myProtectedTranscriptEntries = new(StringComparer.Ordinal);

    public IReadOnlyCollection<AgentPermissionRequest> Permissions
    {
        get { lock (myLock) return myPermissions.Values.ToArray(); }
    }

    public IReadOnlyCollection<AgentInputRequest> Inputs
    {
        get { lock (myLock) return myInputs.Values.ToArray(); }
    }

    public IReadOnlyCollection<AgentElicitationRequest> Elicitations
    {
        get { lock (myLock) return myElicitations.Values.ToArray(); }
    }

    public AgentElicitationRequest GetElicitation(string role, string requestId)
    {
        lock (myLock)
        {
            if (myElicitations.TryGetValue(Key(role, requestId), out var request))
                return request;
            throw new InvalidOperationException($"No pending interaction with ID '{requestId}' exists for role '{role}'.");
        }
    }

    public void RegisterPermission(AgentPermissionRequest request) =>
        Register(myPermissions, request.Role, request.RequestId, request);

    public void RegisterInput(AgentInputRequest request) =>
        Register(myInputs, request.Role, request.RequestId, request);

    public void RegisterElicitation(AgentElicitationRequest request) =>
        Register(myElicitations, request.Role, request.RequestId, request);

    public void ProtectTranscriptEntry(string role, string requestId, int entryIndex)
    {
        lock (myLock)
            myProtectedTranscriptEntries[Key(role, requestId)] = new ProtectedTranscriptEntry(role, entryIndex);
    }

    public (string Role, AgentPermissionRequest Request) RemovePermission(string? expectedRole, string requestId) =>
        Remove(myPermissions, expectedRole, requestId, request => request.Role, request => request.RequestId);

    public (string Role, AgentInputRequest Request) RemoveInput(string? expectedRole, string requestId) =>
        Remove(myInputs, expectedRole, requestId, request => request.Role, request => request.RequestId);

    public (string Role, AgentElicitationRequest Request) RemoveElicitation(string? expectedRole, string requestId) =>
        Remove(myElicitations, expectedRole, requestId, request => request.Role, request => request.RequestId);

    public ProtectedTranscriptEntry? TryRemoveProtectedTranscriptEntry(string role, string requestId)
    {
        lock (myLock)
            return myProtectedTranscriptEntries.Remove(Key(role, requestId), out var entry) ? entry : null;
    }

    public IReadOnlyList<ProtectedTranscriptEntry> RemoveForRole(string role)
    {
        lock (myLock)
        {
            RemoveForRole(myPermissions, role, request => request.Role);
            RemoveForRole(myInputs, role, request => request.Role);
            RemoveForRole(myElicitations, role, request => request.Role);
            var keys = myProtectedTranscriptEntries
                .Where(pair => pair.Value.Role == role)
                .Select(pair => pair.Key)
                .ToArray();
            var removed = keys.Select(key => myProtectedTranscriptEntries[key]).ToArray();
            foreach (var key in keys)
                myProtectedTranscriptEntries.Remove(key);
            return removed;
        }
    }

    public void Clear()
    {
        lock (myLock)
        {
            myPermissions.Clear();
            myInputs.Clear();
            myElicitations.Clear();
        }
    }

    private void Register<TRequest>(Dictionary<string, TRequest> requests, string role, string requestId, TRequest request)
    {
        var key = Key(role, requestId);
        lock (myLock)
        {
            if (!requests.TryAdd(key, request))
                throw new InvalidOperationException($"Interaction '{requestId}' is already pending for role '{role}'.");
        }
    }

    private (string Role, TRequest Request) Remove<TRequest>(
        Dictionary<string, TRequest> requests,
        string? expectedRole,
        string requestId,
        Func<TRequest, string> role,
        Func<TRequest, string> id)
    {
        lock (myLock)
        {
            if (expectedRole is not null)
            {
                if (requests.Remove(Key(expectedRole, requestId), out var request))
                    return (expectedRole, request);
                throw new InvalidOperationException($"No pending interaction with ID '{requestId}' exists for role '{expectedRole}'.");
            }

            var matches = requests.Values.Where(request => id(request) == requestId).ToArray();
            switch (matches.Length)
            {
                case 1:
                    var request = matches[0];
                    requests.Remove(Key(role(request), requestId));
                    return (role(request), request);
                case 0:
                    throw new InvalidOperationException($"No pending interaction with ID '{requestId}' exists.");
                default:
                    throw new InvalidOperationException($"Interaction ID '{requestId}' is pending for multiple roles; specify the role.");
            }
        }
    }

    private static void RemoveForRole<TRequest>(Dictionary<string, TRequest> requests, string role, Func<TRequest, string> roleSelector)
    {
        foreach (var key in requests.Where(pair => roleSelector(pair.Value) == role).Select(pair => pair.Key).ToArray())
            requests.Remove(key);
    }

    private static string Key(string role, string requestId) => $"{role}\u001f{requestId}";
}
