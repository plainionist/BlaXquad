using squad.Hosting.Abstractions;

namespace squad.Hosting.Fake;

/// <summary>Valid <see cref="IHostingFactory"/> implementation whose constructor always throws, used to prove
/// that "--hosting" selection surfaces construction failures with a clear diagnostic.</summary>
public sealed class ThrowingHostingFixtureFactory : IHostingFactory
{
    public ThrowingHostingFixtureFactory() =>
        throw new InvalidOperationException("fixture construction failure");

    public string Name => "throwing-hosting-fixture";

    public HostingRuntime Create(HostingContext context) => throw new NotSupportedException();
}
