using Newtonsoft.Json.Linq;

namespace CTDL.SchemaAPI;

public class UrlCacheBuilder
{
    private readonly string _cacheRoot;
    private readonly HashSet<string> _visited = new();

    public UrlCacheBuilder(string cacheRoot)
    {
        _cacheRoot = cacheRoot;
    }

    public void BuildFromFile(string jsonPath)
    {
        var root = JToken.Parse(File.ReadAllText(jsonPath));
        ScanToken(root);
    }

    // =========================
    // RECURSIVE SCAN
    // =========================
    private void ScanToken(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                foreach (var prop in ((JObject)token).Properties())
                    ScanToken(prop.Value);
                break;

            case JTokenType.Array:
                foreach (var item in (JArray)token)
                    ScanToken(item);
                break;

            case JTokenType.String:
                var str = token.ToString();
                if (IsUrl(str))
                    CacheUrl(str);
                break;
        }
    }

    // =========================
    // CACHE URL
    // =========================
    private void CacheUrl(string url)
    {
        if (_visited.Contains(url))
            return;

        _visited.Add(url);

        try
        {
            Console.WriteLine($"Caching: {url}");

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };

            using var client = new HttpClient(handler);
            var response = client.GetAsync(url).Result;

            // Detect if redirect limit was hit
            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
            {
                Console.WriteLine($"❌ Redirect limit exceeded for {url} (still {response.StatusCode})");
                return;
            }

            response.EnsureSuccessStatusCode();

            var finalUrl = response.RequestMessage!.RequestUri!.ToString();

            if (finalUrl != url) Console.WriteLine($"✔ Redirected: {url} → {finalUrl}");

            var content = response.Content.ReadAsStringAsync().Result;

            if (!response.Content.Headers.ContentType?.MediaType?.Contains("json") ?? true)
            {
                Console.WriteLine($"Skipping non-JSON: {finalUrl}");
                return;
            }

            var localPath = MapUrlToPath(finalUrl);

            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            File.WriteAllText(localPath, content);

            // 🔥 Also map original URL to same file (optional but useful)
            var originalPath = MapUrlToPath(url);
            if (originalPath != localPath)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
                File.WriteAllText(originalPath, content);
            }
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"❌ Failed (network) {url}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to cache {url}: {ex.Message}");
        }
    }

    // =========================
    // URL → FILE PATH
    // =========================
    private string MapUrlToPath(string url)
    {
        var uri = new Uri(url);

        var path = Path.Combine(
            _cacheRoot,
            uri.Host,
            uri.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)
        );

        // If URL ends with slash, give it a name
        if (string.IsNullOrWhiteSpace(Path.GetFileName(path)))
            path = Path.Combine(path, "index");

        if (!path.EndsWith(".json"))
            path += ".json";

        return path;
    }

    // =========================
    // URL DETECTION
    // =========================
    private bool IsUrl(string str)
    {
        return Uri.TryCreate(str, UriKind.Absolute, out var uri)
               && (uri.Scheme == "http" || uri.Scheme == "https");
    }
}