using global::squad.Configuration;

using System.Text.Json;

namespace squad.Commands;

static class Context
{
    public static int Run(string[] args)
    {
        if (args is ["-h" or "--help" or "help"])
        {
            Console.WriteLine("Usage: squad context [--field role | --json]");
            return 0;
        }

        var context = Resolve();
        if (args.Length == 0)
        {
            Console.WriteLine($"Role: {context.Role}");
            Console.WriteLine($"ProjectRoot: {context.ProjectRoot}");
            Console.WriteLine($"RoleWorktreeRoot: {context.RoleWorktreeRoot}");
            Console.WriteLine($"SharedSourcePath: {context.SharedSourcePath}");
            return 0;
        }

        if (args is ["--field", "role"])
        {
            Console.WriteLine(context.Role);
            return 0;
        }

        if (args is ["--json"])
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                role = context.Role,
                projectRoot = context.ProjectRoot,
                roleWorktreeRoot = context.RoleWorktreeRoot,
                sharedSourcePath = context.SharedSourcePath,
            }));
            return 0;
        }

        Console.Error.WriteLine("Usage: squad context [--field role | --json]");
        return 1;
    }

    private static ContextInfo Resolve()
    {
        var roleWorktreeRoot = Path.GetFullPath(ProjectRoot.ResolveViaGit());
        var projectRoot = ProjectRoot.ResolveProjectRoot(roleWorktreeRoot);
        var roles = SquadConfig.ReadRoles(projectRoot);
        var role = CurrentRoleResolver.Resolve(roles, roleWorktreeRoot);
        var sourcePath = Environment.GetEnvironmentVariable("BLAXQUAD_SRC");
        return new ContextInfo(
            role.Role,
            projectRoot,
            roleWorktreeRoot,
            string.IsNullOrWhiteSpace(sourcePath) ? projectRoot : Path.GetFullPath(sourcePath));
    }

    private sealed record ContextInfo(string Role, string ProjectRoot, string RoleWorktreeRoot, string SharedSourcePath);
}



