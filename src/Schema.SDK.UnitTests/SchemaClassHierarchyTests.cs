using System.Text.Json;
using Schema.SDK;

namespace Schema.SDK.UnitTests;

[TestClass]
public class SchemaClassHierarchyTests
{
    private static readonly string Input = Path.Combine(AppContext.BaseDirectory, "Schema");

    [TestMethod]
    public void GetTopLevelClass_AssociateDegree_ReturnsCredential()
    {
        var hierarchy = CreateHierarchy();

        var result = hierarchy.GetTopLevelClass("ceterms:AssociateDegree");

        Assert.AreEqual("ceterms:Credential", result);
    }

    [TestMethod]
    public void GetTopLevelClass_Credential_ReturnsCredential()
    {
        var hierarchy = CreateHierarchy();

        var result = hierarchy.GetTopLevelClass("ceterms:Credential");

        Assert.AreEqual("ceterms:Credential", result);
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
    public void GetTopLevelClassMap_ReturnsAllLoadedClasses()
    {
        var api = CreateApi();
        var hierarchy = new SchemaClassHierarchy(api);

        var result = hierarchy.GetTopLevelClassMap();

        Assert.AreEqual(api.GetAllClasses().Count(), result.Count);
        Assert.AreEqual("ceterms:Credential", result["ceterms:AssociateDegree"]);
        Assert.AreEqual("ceterms:Credential", result["ceterms:BachelorDegree"]);
        Assert.AreEqual("ceterms:Credential", result["ceterms:MasterDegree"]);

        TestContext.WriteLine(JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    public TestContext TestContext { get; set; } = null!;

    private static SchemaClassHierarchy CreateHierarchy()
    {
        return new SchemaClassHierarchy(CreateApi());
    }

    private static SchemaApi CreateApi()
    {
        var api = new SchemaApi();
        api.LoadFromFolder(Input);
        return api;
    }
}
