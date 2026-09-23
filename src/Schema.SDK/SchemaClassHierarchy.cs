using System.Text.Json.Nodes;

namespace Schema.SDK;

/// <summary>
/// Provides class hierarchy helpers on top of a loaded <see cref="SchemaApi"/>.
/// </summary>
public sealed class SchemaClassHierarchy
{
    private readonly SchemaApi _schemaApi;

    public SchemaClassHierarchy(SchemaApi schemaApi)
    {
        _schemaApi = schemaApi ?? throw new ArgumentNullException(nameof(schemaApi));
    }

    /// <summary>
    /// Gets the highest loaded class in the same namespace as the supplied class.
    /// A class with no loaded parent in that namespace is its own top-level class.
    /// </summary>
    public string GetTopLevelClass(string classTerm)
    {
        ValidateClass(classTerm);
        var namespacePrefix = GetNamespacePrefix(classTerm);
        var ancestors = GetAncestors(classTerm);

        return ancestors
            .Where(x => x.StartsWith(namespacePrefix, StringComparison.Ordinal))
            .LastOrDefault() ?? classTerm;
    }

    /// <summary>
    /// Gets the loaded ancestor chain for a class, nearest parent first.
    /// Traversal stops when there is no loaded parent in the class namespace.
    /// </summary>
    public IReadOnlyList<string> GetAncestors(string classTerm)
    {
        ValidateClass(classTerm);
        var namespacePrefix = GetNamespacePrefix(classTerm);
        var visited = new HashSet<string>(StringComparer.Ordinal) { classTerm };
        var ancestors = new List<string>();
        AddAncestors(classTerm, namespacePrefix, visited, ancestors);
        return ancestors;
    }

    /// <summary>
    /// Gets a mapping of every loaded schema class to its top-level class in the same namespace.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetTopLevelClassMap()
    {
        return _schemaApi.GetAllClasses()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToDictionary(
                classTerm => classTerm,
                GetTopLevelClass,
                StringComparer.Ordinal);
    }

    private void AddAncestors(
        string current,
        string namespacePrefix,
        HashSet<string> visited,
        List<string> ancestors)
    {
        var parent = GetSingleParentInNamespace(current, namespacePrefix);
        if (parent == null)
            return;

        if (!visited.Add(parent))
            throw new InvalidOperationException($"Circular class hierarchy detected at: {parent}");

        ancestors.Add(parent);
        AddAncestors(parent, namespacePrefix, visited, ancestors);
    }

    private string? GetSingleParentInNamespace(string classTerm, string namespacePrefix)
    {
        var item = _schemaApi.GetSchemaItem(classTerm)
            ?? throw new InvalidOperationException($"Schema class not found: {classTerm}");

        var parents = GetParentTerms(item)
            .Where(x => x.StartsWith(namespacePrefix, StringComparison.Ordinal))
            .Where(_schemaApi.ClassExists)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return parents.Count switch
        {
            0 => null,
            1 => parents[0],
            _ => throw new InvalidOperationException(
                $"Class '{classTerm}' has multiple parent classes in namespace '{namespacePrefix}': " +
                string.Join(", ", parents))
        };
    }

    private static IEnumerable<string> GetParentTerms(JsonObject item)
    {
        return item["rdfs:subClassOf"] switch
        {
            JsonValue value => new[] { value.ToString() },
            JsonArray array => array.Select(x => x?.ToString()).OfType<string>(),
            _ => Enumerable.Empty<string>()
        };
    }

    private void ValidateClass(string classTerm)
    {
        if (string.IsNullOrWhiteSpace(classTerm))
            throw new ArgumentException("Class term is required.", nameof(classTerm));

        if (!_schemaApi.ClassExists(classTerm))
            throw new InvalidOperationException($"Schema class not found: {classTerm}");
    }

    private static string GetNamespacePrefix(string classTerm)
    {
        var separatorIndex = classTerm.IndexOf(':');
        if (separatorIndex <= 0)
            throw new InvalidOperationException($"Class term does not contain a namespace prefix: {classTerm}");

        return classTerm.Substring( 0, separatorIndex + 1 );
	}
}
