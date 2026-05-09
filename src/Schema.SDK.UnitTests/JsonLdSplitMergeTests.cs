using CTDL.SchemaAPI;
using System.Text.Json.Nodes;

namespace Schema.SDK.UnitTests;

[TestClass]
public class JsonLdSplitMergeTests
{
    private const string InputFile = "Schema/Merged/ctdl-schema.jsonld";
    private const string SplitDir = "Out/Split";
    private const string MergedFile = "Out/Merged/ctdl-schema.jsonld";

    [TestMethod]
    public void RoundTrip_ShouldBeSemanticallyIdentical()
    {
        if (Directory.Exists(SplitDir))
            Directory.Delete(SplitDir, true);

        if (File.Exists(MergedFile))
            File.Delete(MergedFile);

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
        var files = Directory.GetFiles(SplitDir, "*.jsonld", SearchOption.AllDirectories);

        var ids = new HashSet<string>();

        foreach (var file in files)
        {
            var obj = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            var id = obj["@id"]?.ToString();

            Assert.IsNotNull(id);
            Assert.IsTrue(ids.Add(id), $"Duplicate @id detected: {id}");
        }
    }
}