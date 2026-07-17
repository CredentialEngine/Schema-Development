using System.Text.Json.Nodes;

namespace Schema.SDK;

/// <summary>
/// Resolves remote JSON URLs from a local cache directory. If a URL maps to
/// a JSON document that has an outer @context, the inner @context node is returned.
/// </summary>
public class UrlResolver
{
    private readonly string _cacheRoot;

    public UrlResolver(string cacheRoot)
    {
        _cacheRoot = cacheRoot;
    }

    public JsonNode Resolve(string url)
    {
        // Map URL → local file path
        var localPath = MapUrlToPath(url);

        if (!File.Exists(localPath))
            throw new FileNotFoundException($"No cached copy for {url}");

        var json = JsonNode.Parse(File.ReadAllText(localPath))!;

        // If it's a context file, return inner @context
        if (json is JsonObject obj && obj["@context"] != null)
            return obj["@context"]!.DeepClone();

        return json;
    }

    private string MapUrlToPath(string url)
    {
        // Example mapping:
        // https://credreg.net/ctdl/schema/context/json
        // to cache/credreg.net/ctdl/schema/context/json.json

        var uri = new Uri(url);

        var path = Path.Combine(
            _cacheRoot,
            uri.Host,
            uri.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)
        );

        // Add extension.
        if (!path.EndsWith(".json"))
            path += ".json";

        return path;
    }
}