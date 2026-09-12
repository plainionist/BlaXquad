namespace squad.Runtime.Control;

/// <summary>The readiness of one agent role as observed through the control protocol, plus the local outcomes a
/// client can reach without ever exchanging a message with Headquarters. <see cref="QueryAgentStatusAsync"/> in
/// <see cref="HeadquartersControlClient"/> is this enum's only producer, translating both the version-1 control
/// protocol's <c>ready</c>/<c>not-ready</c>/<c>unknown-role</c>/<c>initializing</c> message tokens and its own
/// local unavailable outcomes into exactly one of these members, so every later branch reads the enum instead of
/// re-comparing protocol strings.</summary>
internal enum AgentReadinessStatus
{
    /// <summary>The named role is provider-ready.</summary>
    Ready,

    /// <summary>The named role exists but is not yet provider-ready.</summary>
    NotReady,

    /// <summary>Headquarters has no agent role by that name.</summary>
    UnknownRole,

    /// <summary>Headquarters has not yet installed an agent readiness provider.</summary>
    Initializing,

    /// <summary>No live Headquarters instance exists for the project - either no metadata was found, or metadata
    /// was found but stale and has been removed.</summary>
    Unavailable,

    /// <summary>Headquarters metadata exists, but its control endpoint could not be reached or did not respond in
    /// time.</summary>
    EndpointUnavailable,
}
