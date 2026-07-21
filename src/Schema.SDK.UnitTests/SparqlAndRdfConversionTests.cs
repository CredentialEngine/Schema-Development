using System.Text.Json.Nodes;
using Schema.SDK;
using VDS.RDF;

namespace Schema.SDK.UnitTests;

[TestClass]
public class SparqlAndRdfConversionTests
{
    [TestMethod]
    public void TurtleRoundTrip_PreservesRdfGraph()
    {
        var root = CreateSchema();
        var turtle = Path.Combine(root, "schema.ttl");
        var output = Path.Combine(root, "output");

        try
        {
            var originalApi = new SchemaApi();
            originalApi.LoadFromFolder(root);
            var expected = CloneGraph(originalApi.GetGraph());

            originalApi.ExportTurtle(turtle);

            var importedApi = new SchemaApi();
            importedApi.LoadFromFolder(root);
            importedApi.ImportTurtle(turtle);
            importedApi.SaveFolder(output);

            var reloadedApi = new SchemaApi();
            reloadedApi.LoadFromFolder(output);

            Assert.IsTrue(expected.Equals(reloadedApi.GetGraph()),
                "JSON-LD -> Turtle -> JSON-LD must preserve the RDF graph.");

            var savedClass = (JsonNode.Parse(File.ReadAllText(
                Path.Combine(output, "Split", "classes", "example_Thing.jsonld")))
                ?? throw new InvalidOperationException("Expected generated class JSON-LD.")).AsObject();
            var label = savedClass["rdfs:label"] as JsonObject;
            Assert.IsNotNull(label, "Language-container properties must be written as language maps.");
            Assert.AreEqual("Thing", label["en-US"]?.ToString());
            Assert.IsNull(label["@value"]);
            Assert.IsNull(label["@language"]);
            Assert.IsInstanceOfType<JsonValue>(savedClass["vs:term_status"],
                "Single-valued schema properties must remain scalars.");
            Assert.AreEqual("vs:stable", savedClass["vs:term_status"]?.ToString());

            var subPropertyOf = savedClass["rdfs:subPropertyOf"] as JsonArray;
            Assert.IsNotNull(subPropertyOf, "Array-valued schema properties must remain arrays.");
            Assert.AreEqual("dct:identifier", subPropertyOf[0]?.ToString());
            Assert.IsInstanceOfType<JsonValue>(subPropertyOf[0],
                "Compact IRI strings must not be rewritten as @value objects.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ImportTurtle_WithKnownPrefix_WritesCompactIdAndFileName()
    {
        var root = CreateSchema();
        var turtle = Path.Combine(root, "external.ttl");
        var output = Path.Combine(root, "output");

        try
        {
            File.WriteAllText(turtle, "<https://example.org/terms/FromTurtle> <http://www.w3.org/2000/01/rdf-schema#label> \"From Turtle\"@en .");

            var api = new SchemaApi();
            api.LoadFromFolder(root);
            api.ImportTurtle(turtle);
            api.SaveFolder(output);

            var expectedFile = Path.Combine(output, "Split", "other", "example_FromTurtle.jsonld");
            Assert.IsTrue(File.Exists(expectedFile), expectedFile);

            var document = (JsonNode.Parse(File.ReadAllText(expectedFile)) ?? throw new InvalidOperationException("Expected valid JSON object.")).AsObject();
            Assert.AreEqual("example:FromTurtle", document["@id"]?.ToString());

            var reloaded = new SchemaApi();
            reloaded.LoadFromFolder(output);
            Assert.IsTrue(reloaded.GetGraph().Triples.Any(t =>
                t.Subject is IUriNode uriNode &&
                uriNode.Uri.AbsoluteUri == "https://example.org/terms/FromTurtle"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void SaveFolder_UsesOnlyContextDeclaredPrefixes_AndFallsBackToAbsoluteUris()
    {
        var root = CreateSchema();
        var output = Path.Combine(root, "output");

        try
        {
            var api = new SchemaApi();
            api.LoadFromFolder(root);
            api.ApplySparqlUpdate("""
                PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
                PREFIX unknown: <https://example.net/undeclared/>
                INSERT DATA {
                    unknown:Thing a rdfs:Class ;
                        unknown:predicate unknown:Object .
                }
                """);
            api.SaveFolder(output);

            var generated = Directory
                .EnumerateFiles(Path.Combine(output, "Split"), "*.jsonld", SearchOption.AllDirectories)
                .Select(path => (JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidOperationException("Expected valid JSON object.")).AsObject())
                .Single(node => node["@id"]?.ToString() == "https://example.net/undeclared/Thing");

            Assert.AreEqual("rdfs:Class", generated["@type"]?.ToString(),
                "A prefix declared by the active context should be used.");
            Assert.IsNotNull(generated["https://example.net/undeclared/predicate"],
                "An undeclared predicate namespace must remain an absolute URI.");
			var undeclaredPredicate =
	            generated[ "https://example.net/undeclared/predicate" ] as JsonArray;

			Assert.IsNotNull(
				undeclaredPredicate,
				"The undeclared predicate should be serialized as an array." );

			Assert.AreEqual(
				1,
				undeclaredPredicate.Count,
				"The undeclared predicate should contain one object." );

			Assert.AreEqual(
				"https://example.net/undeclared/Object",
				undeclaredPredicate[ 0 ]?.ToString(),
				"An undeclared object namespace must remain an absolute URI." );

			Assert.IsNull(generated["unknown:predicate"],
                "The serializer must not invent prefixes absent from the active context.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ApplySparqlUpdate_UpdatesJsonLdAndGraph()
    {
        var root = CreateSchema();
        var output = Path.Combine(root, "output");

        try
        {
            var api = new SchemaApi();
            api.LoadFromFolder(root);
            api.ApplySparqlUpdate("""
                PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
                INSERT DATA {
                    <https://example.org/terms/Thing> rdfs:comment "Updated through SPARQL"@en .
                }
                """);
            api.SaveFolder(output);

            var reloaded = new SchemaApi();
            reloaded.LoadFromFolder(output);

            var subject = reloaded.GetGraph().CreateUriNode(new Uri("https://example.org/terms/Thing"));
            var predicate = reloaded.GetGraph().CreateUriNode(new Uri("http://www.w3.org/2000/01/rdf-schema#comment"));
            Assert.IsTrue(reloaded.GetGraph().GetTriplesWithSubjectPredicate(subject, predicate).Any());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    [DataRow("ctdl")]
    [DataRow("ctdlasn")]
    [DataRow("qdata")]
    public void RealSchema_TurtleRoundTrip_PreservesGraphAndLanguageMaps(string schemaName)
    {
        var source = FindRepositorySchemaFolder(schemaName);
        if (source == null)
        {
            Assert.Inconclusive($"Repository schema folder was not found for '{schemaName}'.");
            return;
        }

        var temp = Path.Combine(Path.GetTempPath(), "schema-sdk-real-roundtrip", Guid.NewGuid().ToString("N"));
        var turtle = Path.Combine(temp, schemaName + ".ttl");
        var output = Path.Combine(temp, "output");
        Directory.CreateDirectory(temp);

        try
        {
            var original = new SchemaApi();
            original.LoadFromFolder(source);
            var expected = CloneGraph(original.GetGraph());
            original.ExportTurtle(turtle);

            var imported = new SchemaApi();
            imported.LoadFromFolder(source);
            imported.ImportTurtle(turtle);
            imported.SaveFolder(output);

            var reloaded = new SchemaApi();
            reloaded.LoadFromFolder(output);
            Assert.IsTrue(expected.Equals(reloaded.GetGraph()),
                $"{schemaName} JSON-LD -> Turtle -> JSON-LD changed the RDF graph.");

            AssertLanguageMapShapeIsPreserved(source, output);
            AssertScalarArrayAndPrimitiveShapesArePreserved(source, output);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private static void AssertLanguageMapShapeIsPreserved(string source, string output)
    {
        var contextPath = Directory.EnumerateFiles(source, "*context*.json*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .First();
        var contextRoot = (JsonNode.Parse(File.ReadAllText(contextPath)) ?? throw new InvalidOperationException("Expected valid JSON object.")).AsObject();
        var context = (contextRoot["@context"] ?? throw new InvalidOperationException("Expected @context.")).AsObject();
        var languageProperties = context
            .Where(entry => entry.Value is JsonObject definition &&
                string.Equals(definition["@container"]?.ToString(), "@language", StringComparison.Ordinal))
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);

        var sourceMerged = Directory.EnumerateFiles(Path.Combine(source, "Merged"), "*.json*", SearchOption.TopDirectoryOnly).Single();
        var outputMerged = Directory.EnumerateFiles(Path.Combine(output, "Merged"), "*.json*", SearchOption.TopDirectoryOnly).Single();
        var sourceGraph = ((JsonNode.Parse(File.ReadAllText(sourceMerged)) ?? throw new InvalidOperationException("Expected valid JSON."))["@graph"] ?? throw new InvalidOperationException("Expected @graph.")).AsArray();
        var outputGraph = ((JsonNode.Parse(File.ReadAllText(outputMerged)) ?? throw new InvalidOperationException("Expected valid JSON."))["@graph"] ?? throw new InvalidOperationException("Expected @graph.")).AsArray();
        var outputById = outputGraph.OfType<JsonObject>().ToDictionary(node => (node["@id"] ?? throw new InvalidOperationException("Expected @id.")).ToString(), StringComparer.Ordinal);

        foreach (var sourceNode in sourceGraph.OfType<JsonObject>())
        {
            var id = sourceNode["@id"]?.ToString();
            if (id == null || !outputById.TryGetValue(id, out var outputNode))
                continue;

            foreach (var property in languageProperties)
            {
                if (sourceNode[property] == null)
                    continue;

                Assert.IsInstanceOfType<JsonObject>(outputNode[property],
                    $"{id} property {property} was not restored as a language map.");
                Assert.IsTrue(JsonNode.DeepEquals(sourceNode[property], outputNode[property]),
                    $"{id} property {property} changed during the Turtle round trip.");
            }
        }
    }

    private static void AssertScalarArrayAndPrimitiveShapesArePreserved(string source, string output)
    {
        var sourceMerged = Directory.EnumerateFiles(Path.Combine(source, "Merged"), "*.json*", SearchOption.TopDirectoryOnly).Single();
        var outputMerged = Directory.EnumerateFiles(Path.Combine(output, "Merged"), "*.json*", SearchOption.TopDirectoryOnly).Single();
        var sourceGraph = ((JsonNode.Parse(File.ReadAllText(sourceMerged)) ?? throw new InvalidOperationException("Expected valid JSON."))["@graph"] ?? throw new InvalidOperationException("Expected @graph.")).AsArray();
        var outputGraph = ((JsonNode.Parse(File.ReadAllText(outputMerged)) ?? throw new InvalidOperationException("Expected valid JSON."))["@graph"] ?? throw new InvalidOperationException("Expected @graph.")).AsArray();
        var outputById = outputGraph.OfType<JsonObject>().ToDictionary(node => (node["@id"] ?? throw new InvalidOperationException("Expected @id.")).ToString(), StringComparer.Ordinal);

        foreach (var sourceNode in sourceGraph.OfType<JsonObject>())
        {
            var id = sourceNode["@id"]?.ToString();
            if (id == null || !outputById.TryGetValue(id, out var outputNode))
                continue;

            foreach (var property in sourceNode)
            {
                if (property.Key == "@context" || property.Value == null)
                    continue;

                var outputValue = outputNode[property.Key];
                Assert.IsNotNull(outputValue, $"{id} property {property.Key} was lost during the Turtle round trip.");

                if (property.Value is JsonArray sourceArray)
                {
                    Assert.IsInstanceOfType<JsonArray>(outputValue,
                        $"{id} property {property.Key} changed from an array to a scalar.");

                    if (sourceArray.Where(item => item != null).All(item => item is JsonValue))
                    {
                        var outputArray = (outputValue ?? throw new InvalidOperationException("Expected output value.")).AsArray();
                        Assert.IsTrue(outputArray.Where(item => item != null).All(item => item is JsonValue),
                            $"{id} property {property.Key} changed primitive values into JSON-LD value objects.");
                    }
                }
                else if (property.Value is JsonValue)
                {
                    Assert.IsInstanceOfType<JsonValue>(outputValue,
                        $"{id} property {property.Key} changed from a scalar into an array or object.");
                }
            }
        }
    }

    private static string? FindRepositorySchemaFolder(string schemaName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "Schema", schemaName);
            if (Directory.Exists(Path.Combine(candidate, "Merged")))
                return candidate;

            candidate = Path.Combine(current.FullName, "src", "Schema", schemaName);
            if (Directory.Exists(Path.Combine(candidate, "Merged")))
                return candidate;

            current = current.Parent;
        }

        return null;
    }

    private static IGraph CloneGraph(IGraph source)
    {
        var clone = new Graph();
        clone.Merge(source);
        return clone;
    }

    private static string CreateSchema()
    {
        var root = Path.Combine(Path.GetTempPath(), "schema-sdk-rdf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Split", "classes"));
        Directory.CreateDirectory(Path.Combine(root, "Merged"));

        File.WriteAllText(Path.Combine(root, "example-context.jsonld"), """
            {
                "@context": {
                    "rdf": "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
                    "rdfs": "http://www.w3.org/2000/01/rdf-schema#",
                    "rdfs:label": { "@container": "@language" },
                    "dct": "http://purl.org/dc/terms/",
                    "vs": "https://www.w3.org/2003/06/sw-vocab-status/ns#",
                    "vs:term_status": { "@type": "@id" },
                    "example": "https://example.org/terms/"
                }
            }
            """);

        File.WriteAllText(Path.Combine(root, "Merged", "example-schema.jsonld"), """
            {
                "@context": {
                    "rdf": "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
                    "rdfs": "http://www.w3.org/2000/01/rdf-schema#",
                    "rdfs:label": { "@container": "@language" },
                    "dct": "http://purl.org/dc/terms/",
                    "vs": "https://www.w3.org/2003/06/sw-vocab-status/ns#",
                    "vs:term_status": { "@type": "@id" },
                    "example": "https://example.org/terms/"
                },
                "@graph": [
                    {
                        "@id": "example:Thing",
                        "@type": "rdfs:Class",
                        "rdfs:label": {
                            "en-US": "Thing"
                        },
                        "rdfs:subPropertyOf": [
                            "dct:identifier"
                        ],
                        "vs:term_status": "vs:stable"
                    }
                ]
            }
            """);

        File.WriteAllText(Path.Combine(root, "Split", "classes", "example_Thing.jsonld"), """
            {
                "@context": {
                    "rdf": "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
                    "rdfs": "http://www.w3.org/2000/01/rdf-schema#",
                    "rdfs:label": { "@container": "@language" },
                    "dct": "http://purl.org/dc/terms/",
                    "vs": "https://www.w3.org/2003/06/sw-vocab-status/ns#",
                    "vs:term_status": { "@type": "@id" },
                    "example": "https://example.org/terms/"
                },
                "@id": "example:Thing",
                "@type": "rdfs:Class",
                "rdfs:label": {
                    "en-US": "Thing"
                },
                "rdfs:subPropertyOf": [
                    "dct:identifier"
                ],
                "vs:term_status": "vs:stable"
            }
            """);

        return root;
    }
}
