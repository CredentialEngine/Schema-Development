using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Schema.SDK;

/// <summary>
/// Splits a merged JSON-LD file (with @graph) into per-item files and
/// can merge split files back into a single JSON-LD document.
/// </summary>
public class JsonLdGraphSplitter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IndentSize = 4,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NewLine = "\n"
    };

    private const string RDFS_CLASS_TERM = "rdfs:Class";
    private const string RDF_PROPERTY_TERM = "rdf:Property";
    private const string SKOS_CONCEPT_TERM = "skos:Concept";
    private const string SKOS_CONCEPT_SCHEME_TERM = "skos:ConceptScheme";

    /// <summary>
    /// Split a merged JSON-LD file into multiple files under outputDir.
    /// </summary>
    /// <param name="inputPath">Path to the merged JSON-LD file.</param>
    /// <param name="outputDir">Directory to write split files and metadata.</param>
    public void Split(string inputPath, string outputDir)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Merged schema file was not found.", inputPath);

        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, true);

        Directory.CreateDirectory(outputDir);

        var rootNode = JsonNode.Parse(File.ReadAllText(inputPath));
        if (rootNode is not JsonObject root)
            throw new InvalidOperationException($"Expected a JSON object in '{inputPath}'.");

        var context = root["@context"];
        var graph = root["@graph"]?.AsArray()
                    ?? throw new InvalidOperationException("No @graph found.");

        var metadata = new SplitMetadata
        {
            Context = context?.DeepClone(),
            MergedFileName = Path.GetFileName(inputPath)
        };

        foreach (var node in graph)
        {
            if (node is not JsonObject obj)
                throw new InvalidOperationException("Every @graph item must be a JSON object.");

            var id = obj["@id"]?.ToString()
                     ?? throw new InvalidOperationException("Missing @id");

            var folder = GetTypeFolder(obj);
            var folderPath = Path.Combine(outputDir, folder);

            Directory.CreateDirectory(folderPath);

            var fileName = $"{MakeSafeFileName(id)}.jsonld";
            var fullPath = Path.Combine(folderPath, fileName);

            var output = new JsonObject();

            if (context != null)
                output["@context"] = context.DeepClone();

            foreach (var kvp in obj)
                output[kvp.Key] = kvp.Value?.DeepClone();

            File.WriteAllText(fullPath, ToIndentedJson(output));

            metadata.FileOrder.Add(Path.Combine(folder, fileName));
        }

        File.WriteAllText(
            Path.Combine(outputDir, "_meta.json"),
            JsonSerializer.Serialize(metadata, JsonOptions)
        );
    }

    /// <summary>
    /// Merge split JSON-LD files (and metadata) back into a single JSON-LD document.
    /// </summary>
    /// <param name="inputDir">Directory containing split files and _meta.json.</param>
    /// <param name="outputPath">Path to write the merged JSON-LD file.</param>
    /// <param name="defaultContext">Root context to use when split metadata and files do not provide one.</param>
    public void Merge(string inputDir, string outputPath, JsonNode? defaultContext = null)
    {
        var outputDir = Path.GetDirectoryName(outputPath);
        if (outputDir != null)
            Directory.CreateDirectory(outputDir);

        if (!Directory.Exists(inputDir))
            throw new DirectoryNotFoundException($"Split schema folder was not found: {inputDir}");

        var metaPath = Path.Combine(inputDir, "_meta.json");
        var metadata = File.Exists(metaPath)
            ? JsonSerializer.Deserialize<SplitMetadata>(File.ReadAllText(metaPath))
              ?? throw new InvalidOperationException("Split metadata could not be read.")
            : BuildMetadataFromSplitFiles(inputDir);

        metadata.Context ??= defaultContext?.DeepClone();

        var graph = new JsonArray();

        foreach (var relativePath in metadata.FileOrder)
        {
            var fullPath = Path.Combine(inputDir, relativePath);

            var itemNode = JsonNode.Parse(File.ReadAllText(fullPath));
            if (itemNode is not JsonObject obj)
                throw new InvalidOperationException($"Expected a JSON object in '{fullPath}'.");
            obj.Remove("@context");

            graph.Add(obj);
        }

        var root = new JsonObject();

        if (metadata.Context != null)
            root["@context"] = metadata.Context;

        root["@graph"] = graph;

        File.WriteAllText(outputPath, ToIndentedJson(root));
    }

    private string ToIndentedJson(JsonNode node)
    {
        return node.ToJsonString(JsonOptions);
    }

    private string MakeSafeFileName(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(input
            .Select(c => invalid.Contains(c) || c is ':' or '/' or '\\' ? '_' : c)
            .ToArray());
    }

    private static bool IsJsonFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jsonld", StringComparison.OrdinalIgnoreCase);
    }

    private static SplitMetadata BuildMetadataFromSplitFiles(string inputDir)
    {
        var files = Directory
            .GetFiles(inputDir, "*", SearchOption.AllDirectories)
            .Where(IsJsonFile)
            .Where(path => !Path.GetFileName(path).Equals("_meta.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => GetFolderSortOrder(Path.GetFileName(Path.GetDirectoryName(path))))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
            return new SplitMetadata();

        var first = JsonNode.Parse(File.ReadAllText(files[0]))?.AsObject();
        return new SplitMetadata
        {
            Context = first?["@context"]?.DeepClone(),
            FileOrder = files
                .Select(path => GetRelativePath(inputDir, path))
                .ToList()
        };
    }


    private static string GetRelativePath(string baseDirectory, string path)
    {
        var normalizedBase = AppendDirectorySeparator(Path.GetFullPath(baseDirectory));
        var baseUri = new Uri(normalizedBase, UriKind.Absolute);
        var pathUri = new Uri(Path.GetFullPath(path), UriKind.Absolute);
        var relativeUri = baseUri.MakeRelativeUri(pathUri);
        return Uri.UnescapeDataString(relativeUri.ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string AppendDirectorySeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
            path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            return path;

        return path + Path.DirectorySeparatorChar;
    }
    private static int GetFolderSortOrder(string? folder)
    {
        return folder?.ToLowerInvariant() switch
        {
            "classes" => 0,
            "properties" => 1,
            "conceptschemes" => 2,
            "concepts" => 3,
            _ => 999
        };
    }

    private string GetTypeFolder(JsonObject obj)
    {
        var type = obj["@type"]?.ToString();

        return type switch
        {
            RDFS_CLASS_TERM => "classes",
            RDF_PROPERTY_TERM => "properties",
            SKOS_CONCEPT_TERM => "concepts",
            SKOS_CONCEPT_SCHEME_TERM => "conceptschemes",
            _ => "other"
        };
    }

    public sealed class SplitMetadata
    {
        public JsonNode? Context { get; set; }
        public string? MergedFileName { get; set; }
        public List<string> FileOrder { get; set; } = new();
    }
}