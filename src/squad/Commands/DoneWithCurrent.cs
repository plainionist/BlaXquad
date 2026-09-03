using global::squad.Agent;

namespace squad.Commands;

static class DoneWithCurrent
{
    public static int Run(string[] args)
    {
        try
        {
            var roleWorktreeRoot = Path.GetFullPath(ProjectRoot.ResolveViaGit());
            var projectRoot = ProjectRoot.ResolveProjectRoot(roleWorktreeRoot);
            var roles = SquadConfig.ReadRoles(projectRoot);
            var roleRow = CurrentRoleResolver.Resolve(roles, roleWorktreeRoot);
            var role = roleRow.Role;
            if (string.IsNullOrEmpty(roleRow.ReceiveMode))
            {
                Console.Error.WriteLine($"Unknown role: {role}");
                return 1;
            }

            return roleRow.ReceiveMode switch
            {
                "batch" => DoneWithCurrentBatch.Run(args),
                "task" => DoneWithCurrentTask.Run(args),
                _ => Invalid(roleRow.ReceiveMode, role),
            };
        }
        catch (CliExitException ex)
        {
            if (!string.IsNullOrEmpty(ex.Message))
                Console.Error.WriteLine(ex.Message);
            return ex.ExitCode;
        }
    }

    static int Invalid(string receiveMode, string role)
    {
        Console.Error.WriteLine($"INVALID_RECEIVE_MODE: {receiveMode} for role {role}");
        return 2;
    }
}



