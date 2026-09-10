using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace squad.Issues;

/// <summary>
/// Parses frontmatter YAML content into title and priority fields, resolving each independently so an invalid or
/// absent field does not discard the other. A valid title is a non-empty scalar; a valid priority is an
/// invariant-culture integer. Malformed YAML that cannot be parsed at all falls back to both fields being absent.
/// </summary>
internal static class IssueFrontmatterYamlParser
{
    private static readonly IDeserializer myDeserializer = new DeserializerBuilder().Build();

    public static IssueYamlFields Parse(string yamlContent)
    {
        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            return new IssueYamlFields(null, null);
        }

        Dictionary<string, object>? fields;
        try
        {
            fields = myDeserializer.Deserialize<Dictionary<string, object>>(yamlContent);
        }
        catch (YamlException)
        {
            return new IssueYamlFields(null, null);
        }
        if (fields is null)
        {
            return new IssueYamlFields(null, null);
        }

        return new IssueYamlFields(ResolveTitle(fields), ResolvePriority(fields));
    }

    private static string? ResolveTitle(IReadOnlyDictionary<string, object> fields)
    {
        if (!fields.TryGetValue("title", out var value)
            || value is null
            || value is IDictionary<object, object>
            || value is List<object>)
        {
            return null;
        }
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static int? ResolvePriority(IReadOnlyDictionary<string, object> fields)
    {
        if (!fields.TryGetValue("priority", out var value) || value is null)
        {
            return null;
        }
        return value switch
        {
            int intValue => intValue,
            long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            string stringValue when int.TryParse(
                stringValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null,
        };
    }
}
