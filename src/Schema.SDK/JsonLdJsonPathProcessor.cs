using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Json.Path;

namespace Final;

/// <summary>
/// Helper for applying JsonPath-based transformations to JSON-LD files.
/// Provides methods to remove nodes, set values and manipulate arrays using JsonPath expressions.
/// </summary>
public class JsonLdJsonPathProcessor
{
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        NewLine = "\n"
    };

    /// <summary>
    /// Remove nodes matching any of the provided JsonPath expressions from the input file and write the result.
    /// </summary>
    /// <param name="inputFile">Path to the input JSON file.</param>
    /// <param name="outputFile">Path to write the modified JSON.</param>
    /// <param name="jsonPaths">JsonPath expressions to remove.</param>
    public void CleanFileByJsonPath(
        string inputFile,
        string outputFile,
        params string[] jsonPaths)
    {
        var root = Load(inputFile);

        foreach (var path in jsonPaths)
        {
            var jsonPath = JsonPath.Parse(path);
            var matches = jsonPath.Evaluate(root).Matches.ToList();

            foreach (var match in matches) RemoveNode(match.Value);
        }

        Save(outputFile, root);
    }

    /// <summary>
    /// Set the value at nodes matched by the JsonPath expression.
    /// </summary>
    /// <param name="inputFile">Path to the input JSON file.</param>
    /// <param name="outputFile">Path to write the modified JSON.</param>
    /// <param name="jsonPath">JsonPath expression to target nodes.</param>
    /// <param name="value">Value to set.</param>
    public void SetValue(string inputFile, string outputFile, string jsonPath, object value)
    {
        var root = Load(inputFile);
        var newValue = JsonValue.Create(value);

        var path = JsonPath.Parse(jsonPath);
        var matches = path.Evaluate(root).Matches.ToList();

        foreach (var match in matches) ReplaceNode(match.Value, newValue);

        Save(outputFile, root);
    }

    /// <summary>
    /// Add a value to arrays identified by the JsonPath expression.
    /// </summary>
    /// <param name="inputFile">Path to the input JSON file.</param>
    /// <param name="outputFile">Path to write the modified JSON.</param>
    /// <param name="jsonPath">JsonPath expression targeting arrays.</param>
    /// <param name="value">Value to append.</param>
    public void AddValueToArray(string inputFile, string outputFile, string jsonPath, object value)
    {
        var root = Load(inputFile);
        var newValue = JsonValue.Create(value);

        var path = JsonPath.Parse(jsonPath);
        var matches = path.Evaluate(root).Matches.ToList();

        foreach (var match in matches)
            if (match.Value is JsonArray array)
                array.Add(newValue);
            else
                throw new InvalidOperationException($"Path '{jsonPath}' did not resolve to an array.");

        Save(outputFile, root);
    }

    /// <summary>
    /// Remove occurrences of a value from arrays matched by the JsonPath expression.
    /// </summary>
    /// <param name="inputFile">Path to the input JSON file.</param>
    /// <param name="outputFile">Path to write the modified JSON.</param>
    /// <param name="jsonPath">JsonPath expression targeting arrays.</param>
    /// <param name="value">Value to remove.</param>
    public void RemoveValueFromArray(string inputFile, string outputFile, string jsonPath, object value)
    {
        var root = Load(inputFile);
        var target = JsonValue.Create(value);

        var path = JsonPath.Parse(jsonPath);
        var matches = path.Evaluate(root).Matches.ToList();

        foreach (var match in matches)
            if (match.Value is JsonArray array)
            {
                var toRemove = array
                    .Where(x => JsonNode.DeepEquals(x, target))
                    .ToList();

                foreach (var item in toRemove) array.Remove(item);
            }
            else
            {
                throw new InvalidOperationException($"Path '{jsonPath}' did not resolve to an array.");
            }

        Save(outputFile, root);
    }

    // -----------------------
    // Internal helpers
    // -----------------------

    private JsonNode Load(string file)
    {
        if (!File.Exists(file))
            throw new FileNotFoundException(file);

        var json = File.ReadAllText(file);
        return JsonNode.Parse(json)!;
    }

    private void Save(string file, JsonNode node)
    {
        File.WriteAllText(file, node.ToJsonString(_serializerOptions));
    }

    private void RemoveNode(JsonNode? node)
    {
        if (node == null)
            return;

        if (node.Parent is JsonObject obj)
        {
            var prop = obj.FirstOrDefault(p => p.Value == node);
            if (prop.Key != null) obj.Remove(prop.Key);
        }
        else if (node.Parent is JsonArray array)
        {
            array.Remove(node);
        }
    }

    private void ReplaceNode(JsonNode? node, JsonNode? newValue)
    {
        if (node == null || node.Parent == null)
            return;

        if (node.Parent is JsonObject obj)
        {
            var prop = obj.FirstOrDefault(p => p.Value == node);
            if (prop.Key != null) obj[prop.Key] = newValue;
        }
        else if (node.Parent is JsonArray array)
        {
            var index = array.IndexOf(node);
            if (index >= 0) array[index] = newValue;
        }
    }
}