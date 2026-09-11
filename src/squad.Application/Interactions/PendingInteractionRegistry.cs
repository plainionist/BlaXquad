using squad.AgentProvider.Abstractions.Agents;

namespace squad.Application.Interactions;

/// <summary>
/// Owns pending permission, input, and elicitation requests plus the protected transcript entry each one holds
/// open. Requests are keyed by role and request ID, so the same request ID can be pending independently for
/// multiple roles at once. Provider request records carry no role of their own, so this registry pairs each one
/// with the role it was registered for. Callers never hold its internal lock while awaiting provider or role
/// operations.
/// </summary>
internal sealed class PendingInteractionRegistry
{
    private readonly object myLock = new();
    private readonly Dictionary<string, (string Role, AgentPermissionRequest Request)> myPermissions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Role, AgentInputRequest Request)> myInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Role, AgentElicitationRequest Request)> myElicitations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProtectedTranscriptEntry> myProtectedTranscriptEntries = new(StringComparer.Ordinal);

    public IReadOnlyCollection<(string Role, AgentPermissionRequest Request)> Permissions
    {
        get { lock (myLock) return myPermissions.Values.ToArray(); }
    }

    public IReadOnlyCollection<(string Role, AgentInputRequest Request)> Inputs
    {
        get { lock (myLock) return myInputs.Values.ToArray(); }
    }

    public IReadOnlyCollection<(string Role, AgentElicitationRequest Request)> Elicitations
    {
        get { lock (myLock) return myElicitations.Values.ToArray(); }
    }

    public AgentElicitationRequest GetElicitation(string role, string requestId)
    {
        lock (myLock)
        {
            if (myElicitations.TryGetValue(Key(role, requestId), out var entry))
            {
                return entry.Request;
            }
            throw new InvalidOperationException($"No pending interaction with ID '{requestId}' exists for role '{role}'.");
        }
    }

    public void RegisterPermission(string role, AgentPermissionRequest request) =>
        Register(myPermissions, role, request.RequestId, request);

    public void RegisterInput(string role, AgentInputRequest request) =>
        Register(myInputs, role, request.RequestId, request);

    public void RegisterElicitation(string role, AgentElicitationRequest request) =>
        Register(myElicitations, role, request.RequestId, request);

    public void ProtectTranscriptEntry(string role, string requestId, int entryIndex)
    {
        lock (myLock)
            myProtectedTranscriptEntries[Key(role, requestId)] = new ProtectedTranscriptEntry(role, entryIndex);
    }

    public (string Role, AgentPermissionRequest Request) RemovePermission(string expectedRole, string requestId) =>
        Remove(myPermissions, expectedRole, requestId);

    public (string Role, AgentInputRequest Request) RemoveInput(string expectedRole, string requestId) =>
        Remove(myInputs, expectedRole, requestId);

    public (string Role, AgentElicitationRequest Request) RemoveElicitation(string expectedRole, string requestId) =>
        Remove(myElicitations, expectedRole, requestId);

    public ProtectedTranscriptEntry? TryRemoveProtectedTranscriptEntry(string role, string requestId)
    {
        lock (myLock)
            return myProtectedTranscriptEntries.Remove(Key(role, requestId), out var entry) ? entry : null;
    }

    public IReadOnlyList<ProtectedTranscriptEntry> RemoveForRole(string role)
    {
        lock (myLock)
        {
            var keyPrefix = role + "\u001f";
            RemoveForRole(myPermissions, keyPrefix);
            RemoveForRole(myInputs, keyPrefix);
            RemoveForRole(myElicitations, keyPrefix);
            var keys = myProtectedTranscriptEntries
                .Where(pair => pair.Value.Role == role)
                .Select(pair => pair.Key)
                .ToArray();
            var removed = keys.Select(key => myProtectedTranscriptEntries[key]).ToArray();
            foreach (var key in keys)
            {
                myProtectedTranscriptEntries.Remove(key);
            }
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

    private void Register<TRequest>(Dictionary<string, (string Role, TRequest Request)> requests, string role, string requestId, TRequest request)
    {
        var key = Key(role, requestId);
        lock (myLock)
        {
            if (!requests.TryAdd(key, (role, request)))
            {
                throw new InvalidOperationException($"Interaction '{requestId}' is already pending for role '{role}'.");
            }
        }
    }

    private (string Role, TRequest Request) Remove<TRequest>(
        Dictionary<string, (string Role, TRequest Request)> requests,
        string expectedRole,
        string requestId)
    {
        lock (myLock)
        {
            if (requests.Remove(Key(expectedRole, requestId), out var entry))
            {
                return entry;
            }
            throw new InvalidOperationException($"No pending interaction with ID '{requestId}' exists for role '{expectedRole}'.");
        }
    }

    private static void RemoveForRole<TRequest>(Dictionary<string, (string Role, TRequest Request)> requests, string keyPrefix)
    {
        foreach (var key in requests.Keys.Where(key => key.StartsWith(keyPrefix, StringComparison.Ordinal)).ToArray())
        {
            requests.Remove(key);
        }
    }

    private static string Key(string role, string requestId) => $"{role}\u001f{requestId}";
}
