using System.Reflection;
using System.Runtime.Loader;

namespace squadHQ.Commands;

/// <summary>Assembly load context shared by every runtime-loaded plug-in family (agent providers, hosting
/// adapters): loads a plug-in assembly and its private dependencies, deferring to the default load context for
/// every assembly named in <paramref name="sharedAssemblyNames"/> so those contract assemblies unify with the
/// host's own copy instead of loading a duplicate, type-incompatible copy - even one that happens to sit beside
/// the plug-in assembly on disk (for example because it is also one of the plug-in's own build outputs).</summary>
sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver myResolver;
    private readonly IReadOnlySet<string> mySharedAssemblyNames;

    public PluginLoadContext(string assemblyPath, IReadOnlySet<string> sharedAssemblyNames) : base(isCollectible: false)
    {
        myResolver = new AssemblyDependencyResolver(assemblyPath);
        mySharedAssemblyNames = sharedAssemblyNames;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {

        if (assemblyName.Name is not null && mySharedAssemblyNames.Contains(assemblyName.Name))
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
