namespace squad.Hosting.Abstractions;

/// <summary>Process-time factory loaded at startup to construct one complete hosting bundle. A hosting plug-in
/// must explicitly implement both resources in <see cref="HostingRuntime"/>; no default implementation is
/// provided here.</summary>
public interface IHostingFactory
{
    string Name { get; }

    HostingRuntime Create(HostingContext context);
}
