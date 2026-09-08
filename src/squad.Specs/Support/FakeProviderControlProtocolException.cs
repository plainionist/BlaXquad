namespace squad.Specs.Support;

/// <summary>
/// Reports that <see cref="FakeProviderControlServer"/> rejected a request sent across the fake-provider control
/// pipe - an invalid authentication token, an unsupported protocol version, a missing or unknown correlation id,
/// or an unrecognized command - with the server's own explicit diagnostic message.
/// </summary>
public sealed class FakeProviderControlProtocolException : InvalidOperationException
{
    public FakeProviderControlProtocolException(string message)
        : base(message)
    {
    }
}
