using Schema.SDK;
using System.Text.Json.Nodes;

namespace Schema.SDK.UnitTests;

[TestClass]
public class JsonLdSplitMergeTests
{
    private static readonly string TestRoot = AppContext.BaseDirectory;
    private static readonly string InputFile = Path.Combine(TestRoot, "Schema/Merged/ctdl-schema.jsonld");
    private static readonly string SplitDir = Path.Combine(TestRoot, "Out/Split");
    private static readonly string MergedDir = Path.Combine(TestRoot, "Out/Merged");
    private static readonly string MergedFile = Path.Combine(TestRoot, "Out/Merged/ctdl-schema.jsonld");

    [TestMethod]
    public void RoundTrip_ShouldBeSemanticallyIdentical()
    {
        if (Directory.Exists(SplitDir))
            Directory.Delete(SplitDir, true);

        if (Directory.Exists(MergedDir))
            Directory.Delete(MergedDir, true);

        var splitter = new JsonLdGraphSplitter();

        splitter.Split(InputFile, SplitDir);
        splitter.Merge(SplitDir, MergedFile);

        var original = JsonNode.Parse(File.ReadAllText(InputFile));
        var merged = JsonNode.Parse(File.ReadAllText(MergedFile));

        Assert.IsTrue(
            JsonNode.DeepEquals(original, merged),
            "JSON structures differ after round trip"
        );
    }

    [TestMethod]
    public void NoDuplicateIdsAfterSplit()
    {
        if (Directory.Exists(SplitDir))
            Directory.Delete(SplitDir, true);

        new JsonLdGraphSplitter().Split(InputFile, SplitDir);

        var files = Directory.GetFiles(SplitDir, "*.jsonld", SearchOption.AllDirectories);

        var ids = new HashSet<string>();

        foreach (var file in files)
        {
            var obj = (JsonNode.Parse(File.ReadAllText(file)) ?? throw new InvalidOperationException("Expected valid JSON object.")).AsObject();
            var id = obj["@id"]?.ToString();

            Assert.IsNotNull(id);
            Assert.IsTrue(ids.Add(id), $"Duplicate @id detected: {id}");
        }
    }
}