using System.Reflection;
using System.Runtime.Loader;

namespace squadHQ.Commands;

/// <summary>Loads a provider assembly and its private dependencies, deferring to the default load context
/// for anything it cannot resolve locally so shared contract assemblies (e.g. squad.AgentProvider.Abstractions)
/// unify with the host instead of loading a duplicate, type-incompatible copy.</summary>
sealed class ProviderLoadContext : AssemblyLoadContext
{
    // Must unify with the host's own copy, so it is never resolved locally even if a copy of it
    // happens to sit beside the provider assembly on disk (e.g. because it is also the host's own dependency).
    private const string SharedAbstractionsAssemblyName = "squad.AgentProvider.Abstractions";

    private readonly AssemblyDependencyResolver myResolver;

    public ProviderLoadContext(string assemblyPath) : base(isCollectible: false)
    {
        myResolver = new AssemblyDependencyResolver(assemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == SharedAbstractionsAssemblyName)
        {
            return null;
        }

        var path = myResolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = myResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}
