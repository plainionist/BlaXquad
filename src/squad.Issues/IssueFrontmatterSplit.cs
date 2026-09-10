namespace squad.Issues;

/// <summary>
/// Represents the result of splitting one issue file's raw text into its frontmatter and body. <see cref="HasBody"/>
/// is false only when an opening delimiter was found with no closing delimiter, in which case no body preview is
/// produced.
/// </summary>
internal readonly record struct IssueFrontmatterSplit(
    string Frontmatter,
    string YamlContent,
    string Body,
    bool HasBody);
