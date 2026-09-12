namespace squad.Specs.Support.Scenarios;

/// <summary>
/// The one provider-observable effect a rejected UI-protocol command would otherwise have produced, and that the
/// no-provider-side-effect assertion must therefore prove never happened. Every invalid envelope in
/// <c>UiProtocolValidation.feature</c>'s matrix - plus the well-formed but unknown-role case - names exactly one of
/// these two effects, never a broader "nothing at all happened" sweep the fake session has no API to express.
/// </summary>
internal enum RejectedCommandEffect
{
    /// <summary>The rejected command would have sent a prompt to the agent.</summary>
    Prompt,

    /// <summary>The rejected command would have delivered a permission response to the agent.</summary>
    PermissionResponse,
}
