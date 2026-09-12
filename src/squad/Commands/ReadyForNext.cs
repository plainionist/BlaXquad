using squad.Configuration;
using squad.Process;

namespace squad.Commands;

static class ReadyForNext
{
    public static int Run(string[] args)
    {
        try
        {
            var roleWorktreeRoot = Path.GetFullPath(ProjectRoot.ResolveViaGit());
            var projectRoot = ProjectRoot.ResolveProjectRoot(roleWorktreeRoot);
            var members = SquadConfig.ReadMembers(projectRoot);
            var member = CurrentRoleResolver.Resolve(members, roleWorktreeRoot);
            var role = member.Id.Value;
            if (string.IsNullOrEmpty(member.ReceiveMode))
            {
                Console.Error.WriteLine($"Unknown role: {role}");
                return 1;
            }

            return member.ReceiveMode switch
            {
                "batch" => ReadyForNextBatch.Run(args),
                "task" => ReadyForNextTask.Run(args),
                _ => Invalid(member.ReceiveMode, role),
            };
        }
        catch (CliExitException ex)
        {
            if (!string.IsNullOrEmpty(ex.Message))
            {
                Console.Error.WriteLine(ex.Message);
            }
            return ex.ExitCode;
        }
    }

    static int Invalid(string receiveMode, string role)
    {
        Console.Error.WriteLine($"INVALID_RECEIVE_MODE: {receiveMode} for role {role}");
        return 2;
    }
}



