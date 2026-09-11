namespace squad.Tools.Issues;

/// <summary>Represents title and priority independently resolved from one issue's frontmatter YAML content.</summary>
internal sealed record IssueYamlFields(string? Title, int? Priority);
