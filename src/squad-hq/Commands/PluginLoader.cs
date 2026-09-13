using System.Reflection;
using squad.Process;

namespace squadHQ.Commands;

/// <summary>Domain-specific diagnostics one plug-in family's entry point supplies to <see cref="PluginLoader"/>,
/// keeping shared loading mechanics separate from each family's own wording.</summary>
internal sealed record PluginLoaderDiagnostics(
    Func<string, CliExitException> AssemblyNotFound,
    Func<string, Exception, CliExitException> AssemblyLoadFailed,
    Func<string, string, CliExitException> TypeIncompatible,
    Func<string, CliExitException> MissingConstructor,
    Func<string, Exception, CliExitException> ConstructorFailed);

/// <summary>Shared mechanics for loading one runtime plug-in type into its own, non-collectible
/// <see cref="PluginLoadContext"/>: descriptor-driven assembly loading, type/constructor validation, and
/// construction. Provider and hosting entry points each keep their own load-context lifetime list and their own
/// domain-specific diagnostics; this type owns none of it.</summary>
static class PluginLoader
{
    public static TContract Load<TContract>(
        string assemblyPath,
        string typeName,
        IReadOnlySet<string> sharedAssemblyNames,
        List<PluginLoadContext> loadContexts,
        PluginLoaderDiagnostics diagnostics)
        where TContract : class
    {

        if (!File.Exists(assemblyPath))
        {
            throw diagnostics.AssemblyNotFound(assemblyPath);
        }

        Assembly assembly;

        try
        {
            var context = new PluginLoadContext(assemblyPath, sharedAssemblyNames);
            loadContexts.Add(context);
            assembly = context.LoadFromAssemblyPath(assemblyPath);
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            throw diagnostics.AssemblyLoadFailed(assemblyPath, exception);
        }

        var type = assembly.GetType(typeName, throwOnError: false);

        if (type is null || !type.IsPublic || !type.IsClass || type.IsAbstract || !typeof(TContract).IsAssignableFrom(type))
        {
            throw diagnostics.TypeIncompatible(typeName, assemblyPath);
        }

        var constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, binder: null, Type.EmptyTypes, modifiers: null);

        if (constructor is null)
        {
            throw diagnostics.MissingConstructor(typeName);
        }

        try
        {
            return (TContract)constructor.Invoke(null);
        }
        catch (TargetInvocationException exception)
        {
            throw diagnostics.ConstructorFailed(typeName, exception.InnerException ?? exception);
        }
    }
}
