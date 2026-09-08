namespace squad.Specs.Support;

/// <summary>
/// Reports a bounded wait failure on the fake-provider control transport - for example a session-started or
/// session-disposed observation that never arrived across the pipe. Carries a single pre-formatted diagnostics
/// block of every observation and protocol error the server has seen, so a timeout can be diagnosed without
/// re-running the scenario or inspecting raw pipe traffic by hand.
/// </summary>
public sealed class FakeProviderControlTimeoutException : TimeoutException
{
    public FakeProviderControlTimeoutException(string description, string diagnostics)
        : base($"Timed out waiting for {description}.\n{diagnostics}")
    {
    }
}
