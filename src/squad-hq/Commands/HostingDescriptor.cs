namespace squadHQ.Commands;

/// <summary>Identifies the assembly and public factory type used to load one
/// <see cref="squad.Hosting.Abstractions.IHostingFactory"/>.</summary>
internal sealed record HostingDescriptor(string AssemblyPath, string TypeName);
