using CTDL.SchemaAPI;

namespace Schema.SDK.UnitTests;

[TestClass]
public class SchemaValidationTests
{
    [TestMethod]
    public void CTDL_Schema_Should_Pass_Shacl()
    {
        var api = new SchemaApi();
        var folder = Path.Combine(AppContext.BaseDirectory, "Schema");
        api.LoadFromFolder(folder);

        var (conforms, report) = api.ValidateWithShacl("schema_shape.ttl");

        if (!conforms) Assert.Fail("SHACL failed:\n" + report);

        Assert.IsTrue(conforms);
    }
}