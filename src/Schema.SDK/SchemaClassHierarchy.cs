using System.Text.Json.Nodes;

namespace Schema.SDK;

/// <summary>
/// Provides class hierarchy helpers across the schemas loaded by <see cref="SchemaApi"/>.
/// </summary>
public sealed class SchemaClassHierarchy
{
    private readonly IReadOnlyList<SchemaApi> _schemaApis;

    public SchemaClassHierarchy(SchemaApi schemaApi)
        : this(new[] { schemaApi })
    {
    }

    public SchemaClassHierarchy(IEnumerable<SchemaApi> schemaApis)
    {
        if (schemaApis == null)
            throw new ArgumentNullException(nameof(schemaApis));

        _schemaApis = schemaApis
            .Where(api => api != null)
            .ToList();

        if (_schemaApis.Count == 0)
            throw new ArgumentException("At least one loaded SchemaApi is required.", nameof(schemaApis));
    }

    /// <summary>
    /// Loads all schema folders directly beneath a schema root. Each child schema folder must
    /// contain its own Merged and Split folders and is loaded independently through SchemaApi.
    /// A single schema folder may also be supplied directly.
    /// </summary>
    public static SchemaClassHierarchy LoadFromFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("Schema folder is required.", nameof(folder));

        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Schema folder not found: {folder}");

        var schemaFolders = IsSchemaFolder(folder)
            ? new[] { folder }
            : Directory.GetDirectories(folder)
                .Where(IsSchemaFolder)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (schemaFolders.Length == 0)
            throw new InvalidOperationException(
                $"No schema folders containing both Merged and Split were found under: {folder}");

        var schemaApis = new List<SchemaApi>();
        foreach (var schemaFolder in schemaFolders)
        {
            var api = new SchemaApi();
            api.LoadFromFolder(schemaFolder);
            schemaApis.Add(api);
        }

        return new SchemaClassHierarchy(schemaApis);
    }

    /// <summary>
    /// Gets the highest loaded class in the same namespace as the supplied class.
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
    /// Gets the ancestor chain for a class, nearest parent first.
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
    /// Gets every loaded class mapped to its top-level class in the same namespace.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetTopLevelClassMap()
    {
        return GetAllClasses()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToDictionary(
                classTerm => classTerm,
                GetTopLevelClass,
                StringComparer.Ordinal);
    }

    private static bool IsSchemaFolder(string folder)
    {
        return Directory.Exists(Path.Combine(folder, "Merged")) &&
               Directory.Exists(Path.Combine(folder, "Split"));
    }

    private IEnumerable<string> GetAllClasses()
    {
        return _schemaApis
            .SelectMany(api => api.GetAllClasses())
            .Distinct(StringComparer.Ordinal);
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
        var item = GetSchemaItem(classTerm)
            ?? throw new InvalidOperationException($"Schema class not found: {classTerm}");

        var parents = GetParentTerms(item)
            .Where(x => x.StartsWith(namespacePrefix, StringComparison.Ordinal))
            .Where(ClassExists)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (parents.Count == 0)
            return null;

        if (parents.Count == 1)
            return parents[0];

        throw new InvalidOperationException(
            $"Class '{classTerm}' has multiple parent classes in namespace '{namespacePrefix}': " +
            string.Join(", ", parents));
    }

    private JsonObject? GetSchemaItem(string classTerm)
    {
        foreach (var api in _schemaApis)
        {
            var item = api.GetSchemaItem(classTerm);
            if (item != null)
                return item;
        }

        return null;
    }

    private bool ClassExists(string classTerm)
    {
        return _schemaApis.Any(api => api.ClassExists(classTerm));
    }

    private static IEnumerable<string> GetParentTerms(JsonObject item)
    {
        var value = item["rdfs:subClassOf"];

        if (value is JsonValue singleValue)
            return new[] { singleValue.ToString() };

        if (value is JsonArray array)
            return array.Select(x => x?.ToString()).OfType<string>();

        return Enumerable.Empty<string>();
    }

    private void ValidateClass(string classTerm)
    {
        if (string.IsNullOrWhiteSpace(classTerm))
            throw new ArgumentException("Class term is required.", nameof(classTerm));

        if (!ClassExists(classTerm))
            throw new InvalidOperationException($"Schema class not found: {classTerm}");
    }

    private static string GetNamespacePrefix(string classTerm)
    {
        var separatorIndex = classTerm.IndexOf(':');
        if (separatorIndex <= 0)
            throw new InvalidOperationException($"Class term does not contain a namespace prefix: {classTerm}");

        return classTerm.Substring(0, separatorIndex + 1);
    }
}
