namespace squad.Hosting.Fake;

/// <summary>Public type that does not implement <see cref="squad.Hosting.Abstractions.IHostingFactory"/>, used to
/// prove that "--hosting" selection rejects an incompatible type with a clear diagnostic.</summary>
public sealed class IncompatibleHostingFixture
{
}
