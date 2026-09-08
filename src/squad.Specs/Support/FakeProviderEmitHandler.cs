using System.Text.Json;

namespace squad.Specs.Support;

/// <summary>Routes one "emit" command pushed across the fake-provider control pipe to whichever live session it
/// names, publishing the resulting production <c>AgentEvent</c> (or completing/failing the session) and returning
/// an explicit diagnostic instead of throwing when no such session exists, it has already been disposed, or the
/// given emit kind is unsupported.</summary>
internal delegate Task<string?> FakeProviderEmitHandler(
    string role, string sessionId, string kind, JsonElement data, CancellationToken cancellationToken);
