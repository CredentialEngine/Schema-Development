using System.Text.Json;
using Schema.SDK;

namespace Schema.SDK.UnitTests;

[TestClass]
public class SchemaClassHierarchyTests
{
    [TestMethod]
    public void GetTopLevelClass_AssociateDegree_ReturnsCredential()
    {
        var hierarchy = CreateHierarchy();

        var result = hierarchy.GetTopLevelClass("ceterms:AssociateDegree");

        Assert.AreEqual("ceterms:Credential", result);
    }

    [TestMethod]
    public void GetTopLevelClass_UsesAllSchemasUnderSchemaRoot()
    {
        var hierarchy = CreateHierarchy();

        Assert.AreEqual("ceterms:Credential", hierarchy.GetTopLevelClass("ceterms:AssociateDegree"));
        Assert.AreEqual("ceasn:Competency", hierarchy.GetTopLevelClass("ceasn:Competency"));
        Assert.AreEqual("qdata:DataSetProfile", hierarchy.GetTopLevelClass("qdata:DataSetProfile"));
    }

    [TestMethod]
    public void GetAncestors_AssociateDegree_ReturnsDegreeThenCredential()
    {
        var hierarchy = CreateHierarchy();

        var result = hierarchy.GetAncestors("ceterms:AssociateDegree");

        CollectionAssert.AreEqual(
            new[] { "ceterms:Degree", "ceterms:Credential" },
            result.ToArray());
    }

    [TestMethod]
    public void GetTopLevelClassMap_ReturnsClassesFromAllSchemas()
    {
        var hierarchy = CreateHierarchy();

        var result = hierarchy.GetTopLevelClassMap();

        Assert.AreEqual("ceterms:Credential", result["ceterms:AssociateDegree"]);
        Assert.AreEqual("ceterms:Credential", result["ceterms:BachelorDegree"]);
        Assert.AreEqual("ceterms:Credential", result["ceterms:MasterDegree"]);
        Assert.AreEqual("ceasn:Competency", result["ceasn:Competency"]);
        Assert.AreEqual("qdata:DataSetProfile", result["qdata:DataSetProfile"]);

        TestContext.WriteLine(JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    public TestContext TestContext { get; set; } = null!;

    private static SchemaClassHierarchy CreateHierarchy()
    {
        return SchemaClassHierarchy.LoadFromFolder(GetRepositorySchemaRoot());
    }

    private static string GetRepositorySchemaRoot()
    {
        var schemaRoot = Path.Combine(AppContext.BaseDirectory, "Schema");

        if (!Directory.Exists(schemaRoot))
            throw new DirectoryNotFoundException(
                $"Test Schema folder was not found: {schemaRoot}");

        return schemaRoot;
    }
}
