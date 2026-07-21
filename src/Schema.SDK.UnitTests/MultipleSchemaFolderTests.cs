using System.Text.Json.Nodes;
using Schema.SDK;

namespace Schema.SDK.UnitTests;

[TestClass]
public class MultipleSchemaFolderTests
{
    [TestMethod]
    [DataRow("ctdl", "ctdl", "https://credreg.net/ctdl/schema/context/json")]
    [DataRow("ctdlasn", "ctdlasn", "https://credreg.net/ctdlasn/schema/context/json")]
    [DataRow("qdata", "qdata", "https://credreg.net/qdata/schema/context/json")]
    public void SaveFolder_PreservesSchemaSpecificFileNamesAndContext(
        string schemaName,
        string filePrefix,
        string contextReference)
    {
        var root = Path.Combine(Path.GetTempPath(), "schema-sdk-layout", Guid.NewGuid().ToString("N"));
        var input = Path.Combine(root, schemaName);
        var output = Path.Combine(root, "output", schemaName);

        try
        {
            CreateSchema(input, filePrefix, contextReference);

            var api = new SchemaApi();
            api.LoadFromFolder(input);
            api.CreateNamespace("ex", "https://example.org/");
            api.CreateClass("ex:TestClass");
            api.SaveFolder(output);

            Assert.IsTrue(File.Exists(Path.Combine(output, $"{filePrefix}-context.jsonld")));
            Assert.IsTrue(File.Exists(Path.Combine(output, "Merged", $"{filePrefix}-schema.jsonld")));
            Assert.IsTrue(File.Exists(Path.Combine(output, "Split", "classes", "ex_TestClass.jsonld")));

            var merged = JsonNode.Parse(
                File.ReadAllText(Path.Combine(output, "Merged", $"{filePrefix}-schema.jsonld")))!
                .AsObject();

            Assert.AreEqual(contextReference, merged["@context"]?.ToString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [TestMethod]
    [DataRow("ctdl", "ctdl", "https://credreg.net/ctdl/schema/context/json", "ceterms:Example")]
    [DataRow("ctdlasn", "ctdlasn", "https://credreg.net/ctdlasn/schema/context/json", "ceasn:Example")]
    [DataRow("qdata", "qdata", "https://credreg.net/qdata/schema/context/json", "qdata:Example")]
    public void SplitAndMergeSchema_RoundTripsEverySchemaFolder(
        string schemaName,
        string filePrefix,
        string contextReference,
        string term)
    {
        var root = Path.Combine(Path.GetTempPath(), "schema-sdk-split-merge", Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, schemaName);

        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "Split"));
            Directory.CreateDirectory(Path.Combine(folder, "Merged"));
            File.WriteAllText(Path.Combine(folder, $"{filePrefix}-context.jsonld"), "{\n    \"@context\": {}\n}\n");

            var mergedPath = Path.Combine(folder, "Merged", $"{filePrefix}-schema.jsonld");
            File.WriteAllText(mergedPath, $$"""
                {
                    "@context": "{{contextReference}}",
                    "@graph": [
                        {
                            "@id": "{{term}}",
                            "@type": "rdfs:Class",
                            "rdfs:label": {
                                "en": "Example"
                            }
                        }
                    ]
                }
                """);

            var original = JsonNode.Parse(File.ReadAllText(mergedPath));
            var api = new SchemaApi();

            api.SplitMergedSchema(folder);

            var expectedFile = Path.Combine(
                folder,
                "Split",
                "classes",
                term.Replace(":", "_") + ".jsonld");
            Assert.IsTrue(File.Exists(expectedFile));
            Assert.IsTrue(File.Exists(Path.Combine(folder, "Split", "_meta.json")));

            File.Delete(mergedPath);
            api.MergeSplitSchema(folder);

            var merged = JsonNode.Parse(File.ReadAllText(mergedPath));
            Assert.IsTrue(JsonNode.DeepEquals(original, merged));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }



    [TestMethod]
    public void SplitMergedSchema_AllowsJsonAndJsonLdCopiesOfSameContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "schema-sdk-context-alias", Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "example");

        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "Split"));
            Directory.CreateDirectory(Path.Combine(folder, "Merged"));

            const string context = """
                {
                    "@context": {
                        "rdfs": "http://www.w3.org/2000/01/rdf-schema#",
                        "example": "https://example.org/terms/"
                    }
                }
                """;

            File.WriteAllText(Path.Combine(folder, "example-context.json"), context);
            File.WriteAllText(Path.Combine(folder, "example-context.jsonld"), context);
            File.WriteAllText(Path.Combine(folder, "Merged", "example-schema.jsonld"), """
                {
                    "@context": "https://example.org/context",
                    "@graph": [
                        {
                            "@id": "example:Thing",
                            "@type": "rdfs:Class"
                        }
                    ]
                }
                """);

            var api = new SchemaApi();
            api.SplitMergedSchema(folder);

            Assert.IsTrue(File.Exists(
                Path.Combine(folder, "Split", "classes", "example_Thing.jsonld")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void SchemaApi_DiscoversArbitrarySchemaFileNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "schema-sdk-agnostic", Guid.NewGuid().ToString("N"));
        var input = Path.Combine(root, "not-a-known-schema");
        var output = Path.Combine(root, "output");

        try
        {
            Directory.CreateDirectory(Path.Combine(input, "Split", "classes"));
            Directory.CreateDirectory(Path.Combine(input, "Merged"));

            File.WriteAllText(Path.Combine(input, "vocabulary-context.json"), """
                {
                    "@context": {
                        "rdf": "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
                        "rdfs": "http://www.w3.org/2000/01/rdf-schema#",
                        "example": "https://example.org/terms/"
                    }
                }
                """);

            File.WriteAllText(Path.Combine(input, "Merged", "combined-vocabulary.json"), """
                {
                    "@context": "https://example.org/context",
                    "@graph": []
                }
                """);

            var api = new SchemaApi();
            api.LoadFromFolder(input);
            api.CreateClass("example:Thing");
            api.SaveFolder(output);

            Assert.IsTrue(File.Exists(Path.Combine(output, "vocabulary-context.json")));
            Assert.IsTrue(File.Exists(Path.Combine(output, "Merged", "combined-vocabulary.json")));

            var merged = JsonNode.Parse(
                File.ReadAllText(Path.Combine(output, "Merged", "combined-vocabulary.json")))
                as JsonObject;
            Assert.IsNotNull(merged);
            Assert.AreEqual("https://example.org/context", merged["@context"]?.ToString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void CreateSchema(string folder, string filePrefix, string contextReference)
    {
        Directory.CreateDirectory(Path.Combine(folder, "Split", "classes"));
        Directory.CreateDirectory(Path.Combine(folder, "Merged"));

        File.WriteAllText(Path.Combine(folder, $"{filePrefix}-context.jsonld"), """
            {
                "@context": {
                    "rdf": "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
                    "rdfs": "http://www.w3.org/2000/01/rdf-schema#",
                    "schema": "https://schema.org/"
                }
            }
            """);

        File.WriteAllText(Path.Combine(folder, "Merged", $"{filePrefix}-schema.jsonld"), $$"""
            {
                "@context": "{{contextReference}}",
                "@graph": []
            }
            """);
    }
}
