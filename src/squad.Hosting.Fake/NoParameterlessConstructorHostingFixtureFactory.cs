using squad.Hosting.Abstractions;

namespace squad.Hosting.Fake;

/// <summary>Valid <see cref="IHostingFactory"/> implementation with no public parameterless constructor, used to
/// prove that "--hosting" selection rejects it with a clear diagnostic rather than an unhandled reflection
/// failure.</summary>
public sealed class NoParameterlessConstructorHostingFixtureFactory : IHostingFactory
{
    public NoParameterlessConstructorHostingFixtureFactory(string requiredArgument)
    {
    }

    public string Name => "no-parameterless-constructor-hosting-fixture";

    public HostingRuntime Create(HostingContext context) => throw new NotSupportedException();
}
