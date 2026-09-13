using squad.Specs.Support.Scenarios;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Configuration-author language for describing a squad's roles as they are declared in `blaxquad/squad.json`.
/// Requests the scenario's single <see cref="BackendScenario"/> instance rather than constructing its own, so
/// configuration performed here is visible to every other language module's steps within the same scenario.
/// </summary>
[Binding]
public sealed class ProjectConfigurationSteps
{
    /// <summary>The only columns this slice's `ConfigureRoles` can actually apply. A table naming any other
    /// column would silently appear to configure a field (e.g. a model) that is never written.</summary>
    private static readonly IReadOnlySet<string> SupportedColumns = new HashSet<string>(StringComparer.Ordinal) { "role", "receive mode" };

    private readonly BackendScenario myScenario;

    public ProjectConfigurationSteps(BackendScenario scenario)
    {
        myScenario = scenario;
    }

    [Given("`blaxquad\\/squad.json` configures:")]
    public void GivenBlaxquadSquadJsonConfigures(Table table)
    {
        var unknownColumns = table.Header.Where(column => !SupportedColumns.Contains(column)).ToList();

        if (unknownColumns.Count > 0)
        {
            throw new ArgumentException(
                $"Project configuration table declares unsupported column(s): {string.Join(", ", unknownColumns)}. " +
                $"Supported column(s): {string.Join(", ", SupportedColumns)}.");
        }

        if (!table.Header.Contains("role"))
        {
            throw new ArgumentException("Project configuration table must declare a \"role\" column.");
        }

        var declaresReceiveMode = table.Header.Contains("receive mode");
        var roles = table.Rows.Select((row, index) =>
        {
            var role = row["role"];

            if (string.IsNullOrWhiteSpace(role))
            {
                throw new ArgumentException($"Project configuration table row {index + 1} has an empty \"role\".");
            }

            var receiveMode = declaresReceiveMode ? row["receive mode"] : null;
            return (Role: role, ReceiveMode: receiveMode);
        }).ToArray();

        myScenario.ConfigureRoles(roles);
    }
}
