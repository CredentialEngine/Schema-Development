using System.Text.Json.Nodes;
using CTDL.SchemaAPI;
using VDS.RDF;

namespace Schema.SDK.UnitTests;

[TestClass]
public class LosslessTests
{
    private static readonly string TestRoot =
    AppContext.BaseDirectory;

    private static readonly string Input = Path.Combine(TestRoot, "Schema");

    private static readonly string Output =
        Path.Combine(TestRoot, "Out");

    [TestInitialize]
    public void TestInitialize()
    {
        if (Directory.Exists(Output))
            Directory.Delete(Output, true);
    }
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void RoundTrip_Should_Be_ByteIdentical()
    {
        var api = new SchemaApi();

        api.LoadFromFolder(Input);
        api.SaveFolder(Output);

        var inputSplit = Path.Combine(Input, "Split");
        var outputSplit = Path.Combine(Output, "Split");

        TestContext.WriteLine($"Input split dir: {inputSplit}");
        TestContext.WriteLine($"Output split dir: {outputSplit}");

        var inputFiles = Directory.GetFiles(inputSplit, "*.jsonld", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) != "_meta.json")
            .OrderBy(Path.GetFileName)
            .ToList();

        var outputFiles = Directory.GetFiles(outputSplit, "*.jsonld", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) != "_meta.json")
            .OrderBy(Path.GetFileName)
            .ToList();

        Assert.AreEqual(inputFiles.Count, outputFiles.Count);

        foreach (var f in inputFiles)
        {
            var name = Path.GetFileName(f);
            var outFile = outputFiles
                .FirstOrDefault(x => Path.GetFileName(x) == name);

            Assert.IsNotNull(
                outFile,
                $"No output match for input file '{name}'.\n\nOutput files:\n" +
                string.Join("\n", outputFiles.Select(Path.GetFileName)));

            var a = File.ReadAllText(f);
            var b = File.ReadAllText(outFile);

            Assert.AreEqual(a, b, $"Mismatch in {name}");
        }
    }

    [TestMethod]
    public void CreatePropertyAndClass_Then_AddDomain_Should_Work()
    {
        var api = new SchemaApi();
        api.LoadFromFolder(Input);

        var prop = "https://example.org/p";
        var cls = "https://example.org/c";

        api.AddContextTerm("p", prop);
        api.AddContextTerm("c", cls);

        api.CreateProperty("p");
        api.CreateClass("c");

        api.AddFieldValue(
            "p",
            "schema:domainIncludes",
            "c"
        );

        api.SaveFolder(Output);

        var file = Directory
            .GetFiles(Path.Combine(Output, "Split"), "*", SearchOption.AllDirectories)
            .First(f => Path.GetFileName(f) == "p.jsonld");

        var json = File.ReadAllText(file);
        var obj = JsonNode.Parse(json)!.AsObject();

        Assert.IsTrue(obj["schema:domainIncludes"] != null);
    }

    [TestMethod]
    public void SetProperty_Should_Persist()
    {
        var api = new SchemaApi();
        api.LoadFromFolder(Input);

        var cls = "https://example.org/c";

        api.AddContextTerm("c", cls);
        api.CreateClass("c");

        api.SetField("c", "vs:term_status", "stable");

        api.SaveFolder(Output);

        var file = Directory
            .GetFiles(Path.Combine(Output, "Split"), "*", SearchOption.AllDirectories)
            .First(f => Path.GetFileName(f) == "c.jsonld");

        var json = File.ReadAllText(file);

        Assert.IsTrue(json.Contains("stable"));
    }

    [TestMethod]
    public void RemoveProperty_Should_Remove_FromJsonAndGraph()
    {
        var api = new SchemaApi();
        api.LoadFromFolder(Input);

        var cls = "https://example.org/c";

        api.AddContextTerm("c", cls);
        api.CreateClass("c");

        api.SetField("c", "vs:term_status", "stable");
        api.RemoveField("c", "vs:term_status");

        api.SaveFolder(Output);

        var file = Directory
            .GetFiles(Path.Combine(Output, "Split"), "*", SearchOption.AllDirectories)
            .First(f => Path.GetFileName(f) == "c.jsonld");

        var json = File.ReadAllText(file);
        var obj = JsonNode.Parse(json)!.AsObject();

        Assert.IsTrue(obj["vs:term_status"] == null);

        var triples = api.GetGraph()
            .GetTriplesWithSubjectPredicate(
                api.GetGraph().CreateUriNode(UriFactory.Create(cls)),
                api.GetGraph().CreateUriNode(UriFactory.Create("vs:term_status"))
            );

        Assert.IsFalse(triples.Any());
    }

    [TestMethod]
    public void GetAllClasses_Should_ReturnCreatedClasses()
    {
        var api = new SchemaApi();

        api.AddContextTerm("c1", "https://example.org/c1");
        api.AddContextTerm("c2", "https://example.org/c2");

        api.CreateClass("c1");
        api.CreateClass("c2");

        var result = api.GetAllClasses().ToList();

        Assert.AreEqual(2, result.Count);
        CollectionAssert.Contains(result, "c1");
        CollectionAssert.Contains(result, "c2");
    }

    [TestMethod]
    public void GetAllClasses_Should_ReturnEmpty_WhenNoneExist()
    {
        var api = new SchemaApi();

        var result = api.GetAllClasses().ToList();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void GetAllProperties_Should_ReturnCreatedProperties()
    {
        var api = new SchemaApi();

        api.AddContextTerm("p1", "https://example.org/p1");
        api.AddContextTerm("p2", "https://example.org/p2");

        api.CreateProperty("p1");
        api.CreateProperty("p2");

        var result = api.GetAllProperties().ToList();

        Assert.AreEqual(2, result.Count);
        CollectionAssert.Contains(result, "p1");
        CollectionAssert.Contains(result, "p2");
    }

    [TestMethod]
    public void GetAllConcepts_Should_ReturnCreatedConcepts()
    {
        var api = new SchemaApi();

        api.AddContextTerm("concept1", "https://example.org/concept1");

        api.CreateConcept("concept1");

        var result = api.GetAllConcepts().ToList();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("concept1", result[0]);
    }

    [TestMethod]
    public void GetAllConceptSchemes_Should_ReturnCreatedSchemes()
    {
        var api = new SchemaApi();

        api.AddContextTerm("scheme1", "https://example.org/scheme1");

        api.CreateConceptScheme("scheme1");

        var result = api.GetAllConceptSchemes().ToList();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("scheme1", result[0]);
    }

    [TestMethod]
    public void GetSchemaItem_Should_ReturnItem_ByTerm()
    {
        var api = new SchemaApi();

        api.AddContextTerm("c", "https://example.org/c");
        api.CreateClass("c");

        var item = api.GetSchemaItem("c");

        Assert.IsNotNull(item);
        Assert.AreEqual("rdfs:Class", item["@type"]!.ToString());
    }

    [TestMethod]
    public void GetSchemaItem_Should_ReturnNull_WhenMissing()
    {
        var api = new SchemaApi();

        var item = api.GetSchemaItem("missing");

        Assert.IsNull(item);
    }

    [TestMethod]
    public void GetPropertiesOfClass_Should_ReturnMatchingProperties()
    {
        var api = new SchemaApi();

        api.AddContextTerm("c", "https://example.org/c");
        api.AddContextTerm("p", "https://example.org/p");

        api.CreateClass("c");
        api.CreateProperty("p");

        api.AddFieldValue("p", "schema:domainIncludes", "c");

        var result = api.GetPropertiesOfClass("c").ToList();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("p", result[0]);
    }

    [TestMethod]
    public void GetPropertiesOfClass_Should_ReturnEmpty_WhenNoMatches()
    {
        var api = new SchemaApi();

        api.AddContextTerm("c", "https://example.org/c");
        api.CreateClass("c");

        var result = api.GetPropertiesOfClass("c").ToList();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void GetClassesUsingProperty_Should_ReturnDomains()
    {
        var api = new SchemaApi();

        api.AddContextTerm("c", "https://example.org/c");
        api.AddContextTerm("p", "https://example.org/p");

        api.CreateClass("c");
        api.CreateProperty("p");

        api.AddFieldValue("p", "schema:domainIncludes", "c");

        var result = api.GetClassesUsingProperty("p").ToList();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("c", result[0]);
    }

    [TestMethod]
    public void GetClassesUsingProperty_Should_ReturnEmpty_WhenNoDomain()
    {
        var api = new SchemaApi();

        api.AddContextTerm("p", "https://example.org/p");
        api.CreateProperty("p");

        var result = api.GetClassesUsingProperty("p").ToList();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void GetRangeOfProperty_Should_ReturnRanges()
    {
        var api = new SchemaApi();

        api.AddContextTerm("range", "https://example.org/range");
        api.AddContextTerm("p", "https://example.org/p");

        api.CreateClass("range");
        api.CreateProperty("p");

        api.AddFieldValue("p", "schema:rangeIncludes", "range");

        var result = api.GetRangeOfProperty("p").ToList();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("range", result[0]);
    }

    [TestMethod]
    public void GetRangeOfProperty_Should_ReturnEmpty_WhenNoRange()
    {
        var api = new SchemaApi();

        api.AddContextTerm("p", "https://example.org/p");
        api.CreateProperty("p");

        var result = api.GetRangeOfProperty("p").ToList();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void GetPropertiesWithRange_Should_ReturnMatchingProperties()
    {
        var api = new SchemaApi();

        api.AddContextTerm("range", "https://example.org/range");
        api.AddContextTerm("p", "https://example.org/p");

        api.CreateClass("range");
        api.CreateProperty("p");

        api.AddFieldValue("p", "schema:rangeIncludes", "range");

        var result = api.GetPropertiesWithRange("range").ToList();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("p", result[0]);
    }

    [TestMethod]
    public void GetPropertiesWithRange_Should_ReturnEmpty_WhenNoMatches()
    {
        var api = new SchemaApi();

        api.AddContextTerm("range", "https://example.org/range");
        api.CreateClass("range");

        var result = api.GetPropertiesWithRange("range").ToList();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void GetSubClasses_Should_ReturnChildren()
    {
        var api = new SchemaApi();

        api.AddContextTerm("parent", "https://example.org/parent");
        api.AddContextTerm("child", "https://example.org/child");

        api.CreateClass("parent");
        api.CreateClass("child");

        api.AddFieldValue("child", "rdfs:subClassOf", "parent");

        var result = api.GetSubClasses("parent").ToList();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("child", result[0]);
    }

    [TestMethod]
    public void GetSubClasses_Should_ReturnEmpty_WhenNoChildren()
    {
        var api = new SchemaApi();

        api.AddContextTerm("parent", "https://example.org/parent");
        api.CreateClass("parent");

        var result = api.GetSubClasses("parent").ToList();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void GetConceptsInScheme_Should_ReturnConcepts()
    {
        var api = new SchemaApi();

        api.AddContextTerm("scheme", "https://example.org/scheme");
        api.AddContextTerm("concept", "https://example.org/concept");

        api.CreateConceptScheme("scheme");
        api.CreateConcept("concept");

        api.AddFieldValue("concept", "skos:inScheme", "scheme");

        var result = api.GetConceptsInScheme("scheme").ToList();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("concept", result[0]);
    }

    [TestMethod]
    public void GetConceptsInScheme_Should_ReturnEmpty_WhenNoneExist()
    {
        var api = new SchemaApi();

        api.AddContextTerm("scheme", "https://example.org/scheme");
        api.CreateConceptScheme("scheme");

        var result = api.GetConceptsInScheme("scheme").ToList();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ClassExists_Should_ReturnTrue_WhenClassExists()
    {
        var api = new SchemaApi();

        api.AddContextTerm("c", "https://example.org/c");
        api.CreateClass("c");

        Assert.IsTrue(api.ClassExists("c"));
    }

    [TestMethod]
    public void ClassExists_Should_ReturnFalse_WhenMissing()
    {
        var api = new SchemaApi();

        Assert.IsFalse(api.ClassExists("missing"));
    }

    [TestMethod]
    public void PropertyExists_Should_ReturnTrue_WhenPropertyExists()
    {
        var api = new SchemaApi();

        api.AddContextTerm("p", "https://example.org/p");
        api.CreateProperty("p");

        Assert.IsTrue(api.PropertyExists("p"));
    }

    [TestMethod]
    public void PropertyExists_Should_ReturnFalse_WhenMissing()
    {
        var api = new SchemaApi();

        Assert.IsFalse(api.PropertyExists("missing"));
    }

    [TestMethod]
    public void ConceptExists_Should_ReturnTrue_WhenConceptExists()
    {
        var api = new SchemaApi();

        api.AddContextTerm("concept", "https://example.org/concept");
        api.CreateConcept("concept");

        Assert.IsTrue(api.ConceptExists("concept"));
    }

    [TestMethod]
    public void ConceptExists_Should_ReturnFalse_WhenMissing()
    {
        var api = new SchemaApi();

        Assert.IsFalse(api.ConceptExists("missing"));
    }

    [TestMethod]
    public void ConceptSchemeExists_Should_ReturnTrue_WhenSchemeExists()
    {
        var api = new SchemaApi();

        api.AddContextTerm("scheme", "https://example.org/scheme");
        api.CreateConceptScheme("scheme");

        Assert.IsTrue(api.ConceptSchemeExists("scheme"));
    }

    [TestMethod]
    public void ConceptSchemeExists_Should_ReturnFalse_WhenMissing()
    {
        var api = new SchemaApi();

        Assert.IsFalse(api.ConceptSchemeExists("missing"));
    }

    [TestMethod]
    public void DeleteConcept_Should_Remove_FromDocs_And_Graph()
    {
        var api = new SchemaApi();

        api.AddContextTerm(
            "concept",
            "https://example.org/concept");

        api.CreateConcept("concept");

        Assert.IsTrue(api.ConceptExists("concept"));

        api.DeleteConcept("concept");

        Assert.IsFalse(api.ConceptExists("concept"));
        Assert.AreEqual(0, api.GetAllConcepts().Count());

        var triples = api.GetGraph()
            .GetTriplesWithSubject(
                api.GetGraph().CreateUriNode(
                    UriFactory.Create("https://example.org/concept")))
            .ToList();

        Assert.AreEqual(0, triples.Count);
    }

    [TestMethod]
    public void DeleteConceptScheme_Should_Remove_FromDocs_And_Graph()
    {
        var api = new SchemaApi();

        api.AddContextTerm(
            "scheme",
            "https://example.org/scheme");

        api.CreateConceptScheme("scheme");

        Assert.IsTrue(api.ConceptSchemeExists("scheme"));

        api.DeleteConceptScheme("scheme");

        Assert.IsFalse(api.ConceptSchemeExists("scheme"));
        Assert.AreEqual(0, api.GetAllConceptSchemes().Count());

        var triples = api.GetGraph()
            .GetTriplesWithSubject(
                api.GetGraph().CreateUriNode(
                    UriFactory.Create("https://example.org/scheme")))
            .ToList();

        Assert.AreEqual(0, triples.Count);
    }

    [TestMethod]
    public void LoadFromFolder_Should_Normalize_UriIds_To_Terms()
    {
        var api = new SchemaApi();

        api.LoadFromFolder(Input);

        var classes = api.GetAllClasses().ToList();

        Assert.IsTrue(classes.All(x => !x.StartsWith("http")));
    }

    [TestMethod]
    public void Loaded_Items_Should_Be_Resolvable_By_Term()
    {
        var api = new SchemaApi();

        api.LoadFromFolder(Input);

        var first = api.GetAllClasses().FirstOrDefault();

        Assert.IsNotNull(first);

        var item = api.GetSchemaItem(first);

        Assert.IsNotNull(item);
    }

    [TestMethod]
    public void DeleteClass_Should_Remove_FromDocs_And_Graph()
    {
        var api = new SchemaApi();

        api.AddContextTerm(
            "c",
            "https://example.org/c");

        api.CreateClass("c");

        Assert.IsTrue(api.ClassExists("c"));

        api.DeleteClass("c");

        Assert.IsFalse(api.ClassExists("c"));
        Assert.AreEqual(0, api.GetAllClasses().Count());

        var triples = api.GetGraph()
            .GetTriplesWithSubject(
                api.GetGraph().CreateUriNode(
                    UriFactory.Create("https://example.org/c")))
            .ToList();

        Assert.AreEqual(0, triples.Count);
    }

    [TestMethod]
    public void DeleteProperty_Should_Remove_FromDocs_And_Graph()
    {
        var api = new SchemaApi();

        api.AddContextTerm(
            "p",
            "https://example.org/p");

        api.CreateProperty("p");

        Assert.IsTrue(api.PropertyExists("p"));

        api.DeleteProperty("p");

        Assert.IsFalse(api.PropertyExists("p"));
        Assert.AreEqual(0, api.GetAllProperties().Count());

        var triples = api.GetGraph()
            .GetTriplesWithSubject(
                api.GetGraph().CreateUriNode(
                    UriFactory.Create("https://example.org/p")))
            .ToList();

        Assert.AreEqual(0, triples.Count);
    }

    [TestMethod]
    public void SaveFolder_Should_Use_Term_Based_File_Names()
    {
        var api = new SchemaApi();

        api.AddContextTerm(
            "ceterms:TestClass",
            "https://example.org/TestClass");

        api.CreateClass("ceterms:TestClass");

        api.SaveFolder(Output);

        var files = Directory.GetFiles(
            Path.Combine(Output, "Split"),
            "*.jsonld",
            SearchOption.AllDirectories);

        Assert.IsTrue(
            files.Any(f =>
                Path.GetFileName(f)
                == "ceterms_TestClass.jsonld"));
    }

    [TestMethod]
    public void GetDoc_Should_Throw_For_Uri_When_Using_Term_Identity()
    {
        var api = new SchemaApi();

        api.AddContextTerm(
            "c",
            "https://example.org/c");

        api.CreateClass("c");

        Assert.ThrowsException<InvalidOperationException>(() =>
        {
            api.SetField(
                "https://example.org/c",
                "rdfs:label",
                "Bad");
        });
    }

    [TestMethod]
    public void CreateClass_Should_Create_Class()
    {
        var api = new SchemaApi();

        api.AddContextTerm("c", "https://example.org/c");

        api.CreateClass("c");

        Assert.IsTrue(api.ClassExists("c"));

        var item = api.GetSchemaItem("c");

        Assert.IsNotNull(item);
        Assert.AreEqual("rdfs:Class", item["@type"]!.ToString());
    }

    [TestMethod]
    public void DeleteClass_Should_Remove_Class()
    {
        var api = new SchemaApi();

        api.AddContextTerm("c", "https://example.org/c");

        api.CreateClass("c");

        api.DeleteClass("c");

        Assert.IsFalse(api.ClassExists("c"));
    }

    [TestMethod]
    public void CreateProperty_Should_Create_Property()
    {
        var api = new SchemaApi();

        api.AddContextTerm("p", "https://example.org/p");

        api.CreateProperty("p");

        Assert.IsTrue(api.PropertyExists("p"));

        var item = api.GetSchemaItem("p");

        Assert.IsNotNull(item);
        Assert.AreEqual("rdf:Property", item["@type"]!.ToString());
    }

    [TestMethod]
    public void DeleteProperty_Should_Remove_Property()
    {
        var api = new SchemaApi();

        api.AddContextTerm("p", "https://example.org/p");

        api.CreateProperty("p");

        api.DeleteProperty("p");

        Assert.IsFalse(api.PropertyExists("p"));
    }

    [TestMethod]
    public void CreateConcept_Should_Create_Concept()
    {
        var api = new SchemaApi();

        api.AddContextTerm("concept", "https://example.org/concept");

        api.CreateConcept("concept");

        Assert.IsTrue(api.ConceptExists("concept"));

        var item = api.GetSchemaItem("concept");

        Assert.IsNotNull(item);
        Assert.AreEqual("skos:Concept", item["@type"]!.ToString());
    }

    [TestMethod]
    public void DeleteConcept_Should_Remove_Concept()
    {
        var api = new SchemaApi();

        api.AddContextTerm("concept", "https://example.org/concept");

        api.CreateConcept("concept");

        api.DeleteConcept("concept");

        Assert.IsFalse(api.ConceptExists("concept"));
    }

    [TestMethod]
    public void CreateConceptScheme_Should_Create_Scheme()
    {
        var api = new SchemaApi();

        api.AddContextTerm("scheme", "https://example.org/scheme");

        api.CreateConceptScheme("scheme");

        Assert.IsTrue(api.ConceptSchemeExists("scheme"));

        var item = api.GetSchemaItem("scheme");

        Assert.IsNotNull(item);
        Assert.AreEqual(
            "skos:ConceptScheme",
            item["@type"]!.ToString());
    }

    [TestMethod]
    public void DeleteConceptScheme_Should_Remove_Scheme()
    {
        var api = new SchemaApi();

        api.AddContextTerm("scheme", "https://example.org/scheme");

        api.CreateConceptScheme("scheme");

        api.DeleteConceptScheme("scheme");

        Assert.IsFalse(api.ConceptSchemeExists("scheme"));
    }

    [TestMethod]
    public void SetProperty_Should_Update_Property()
    {
        var api = new SchemaApi();

        api.AddContextTerm("c", "https://example.org/c");

        api.CreateClass("c");

        api.SetField(
            "c",
            "vs:term_status",
            "stable");

        var item = api.GetSchemaItem("c");

        Assert.AreEqual(
            "stable",
            item!["vs:term_status"]!.ToString());
    }

    [TestMethod]
    public void RemoveProperty_Should_Remove_Property_Value()
    {
        var api = new SchemaApi();

        api.AddContextTerm("c", "https://example.org/c");

        api.CreateClass("c");

        api.SetField(
            "c",
            "vs:term_status",
            "stable");

        api.RemoveField(
            "c",
            "vs:term_status");

        var item = api.GetSchemaItem("c");

        Assert.IsNull(item!["vs:term_status"]);
    }

    [TestMethod]
    public void AddPropertyValue_Should_Add_Array_Value()
    {
        var api = new SchemaApi();

        api.AddContextTerm("c", "https://example.org/c");
        api.AddContextTerm("p", "https://example.org/p");

        api.CreateClass("c");
        api.CreateProperty("p");

        api.AddFieldValue(
            "p",
            "schema:domainIncludes",
            "c");

        var item = api.GetSchemaItem("p");

        Assert.IsNotNull(item!["schema:domainIncludes"]);
    }

    [TestMethod]
    public void RemovePropertyValue_Should_Remove_Array_Value()
    {
        var api = new SchemaApi();

        api.AddContextTerm("c", "https://example.org/c");
        api.AddContextTerm("p", "https://example.org/p");

        api.CreateClass("c");
        api.CreateProperty("p");

        api.AddFieldValue(
            "p",
            "schema:domainIncludes",
            "c");

        api.RemoveFieldValue(
            "p",
            "schema:domainIncludes",
            "c");

        var item = api.GetSchemaItem("p");

        if (item!["schema:domainIncludes"] is JsonArray arr)
            Assert.AreEqual(0, arr.Count);
    }

    [TestMethod]
    public void SetLanguageProperty_Should_Create_Language_Map()
    {
        var api = new SchemaApi();

        api.AddContextTerm("c", "https://example.org/c");

        api.CreateClass("c");

        api.SetLanguageProperty(
            "c",
            "rdfs:label",
            "en",
            "Test");

        var item = api.GetSchemaItem("c");

        Assert.AreEqual(
            "Test",
            item!["rdfs:label"]!["en"]!.ToString());
    }

    [TestMethod]
    public void UpdateContextTerm_Should_Update_Context()
    {
        var api = new SchemaApi();

        api.AddContextTerm(
            "test",
            "https://example.org/test");

        api.UpdateContextTerm(
            "test",
            JsonValue.Create("https://example.org/updated"));

        var result = api.GetContextTerm("test");

        Assert.AreEqual(
            "https://example.org/updated",
            result!.ToString());
    }

    [TestMethod]
    public void RemoveContextTerm_Should_Remove_Context()
    {
        var api = new SchemaApi();

        api.AddContextTerm(
            "test",
            "https://example.org/test");

        api.RemoveContextTerm("test");

        Assert.IsNull(api.GetContextTerm("test"));
    }

    [TestMethod]
    public void SetContextField_Should_Update_Field()
    {
        var api = new SchemaApi();

        api.AddContextTermType("name", "xsd:string");

        api.SetContextField(
            "name",
            "@container",
            "@language");

        var obj = api.GetContextTerm("name")!.AsObject();

        Assert.AreEqual(
            "@language",
            obj["@container"]!.ToString());
    }

    [TestMethod]
    public void RemoveContextField_Should_Remove_Field()
    {
        var api = new SchemaApi();

        api.AddContextTermTypeAndContainer(
            "name",
            "xsd:string",
            "@language");

        api.RemoveContextField(
            "name",
            "@container");

        var obj = api.GetContextTerm("name")!.AsObject();

        Assert.IsNull(obj["@container"]);
    }

    [TestMethod]
    public void SaveFolder_Then_LoadFolder_Should_Preserve_Data()
    {
        var api = new SchemaApi();

        api.AddContextTerm("c", "https://example.org/c");

        api.CreateClass("c");

        api.SetField(
            "c",
            "vs:term_status",
            "stable");

        api.SaveFolder(Output);

        var api2 = new SchemaApi();

        api2.LoadFromFolder(Output);

        Assert.IsTrue(api2.ClassExists("c"));

        var item = api2.GetSchemaItem("c");

        Assert.AreEqual(
            "stable",
            item!["vs:term_status"]!.ToString());
    }
}