using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CTDL.SchemaAPI;

public class JsonLdGraphSplitter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IndentSize = 4,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void Split(string inputPath, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        var root = JsonNode.Parse(File.ReadAllText(inputPath))!.AsObject();

        var context = root["@context"];
        var graph = root["@graph"]?.AsArray()
                    ?? throw new InvalidOperationException("No @graph found.");

        var metadata = new SplitMetadata
        {
            Context = context?.DeepClone()
        };

        foreach (var node in graph)
        {
            var obj = node!.AsObject();

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

    public void Merge(string inputDir, string outputPath)
    {
        var metaPath = Path.Combine(inputDir, "_meta.json");

        var metadata = JsonSerializer.Deserialize<SplitMetadata>(
            File.ReadAllText(metaPath)
        )!;

        var graph = new JsonArray();

        foreach (var relativePath in metadata.FileOrder)
        {
            var fullPath = Path.Combine(inputDir, relativePath);

            var obj = JsonNode.Parse(File.ReadAllText(fullPath))!.AsObject();
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
            .Select(c => invalid.Contains(c) ? '_' : c)
            .ToArray());
    }

    private string GetTypeFolder(JsonObject obj)
    {
        var type = obj["@type"]?.ToString();

        return type switch
        {
            "rdfs:Class" => "classes",
            "rdf:Property" => "properties",
            "skos:Concept" => "concepts",
            "skos:ConceptScheme" => "conceptschemes",
            _ => "other"
        };
    }

    public class SplitMetadata
    {
        public JsonNode? Context { get; set; }
        public List<string> FileOrder { get; set; } = new();
    }
}