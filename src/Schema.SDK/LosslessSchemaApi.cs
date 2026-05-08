using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Final;
using Schema.SDK;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Shacl;
using Path = System.IO.Path;

namespace CTDL.SchemaAPI;

public class LosslessSchemaApi
{
    private const string SKOS_CONCEPT = "http://www.w3.org/2004/02/skos/core#Concept";
    private const string SKOS_CONCEPT_SCHEME = "http://www.w3.org/2004/02/skos/core#ConceptScheme";

    private const string RDF_TYPE = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string RDFS_CLASS = "http://www.w3.org/2000/01/rdf-schema#Class";
    private const string RDF_PROPERTY = "http://www.w3.org/1999/02/22-rdf-syntax-ns#Property";

    // Term constants to avoid repeated string literals
    private const string RDFS_CLASS_TERM = "rdfs:Class";
    private const string RDF_PROPERTY_TERM = "rdf:Property";
    private const string SKOS_CONCEPT_TERM = "skos:Concept";
    private const string SKOS_CONCEPT_SCHEME_TERM = "skos:ConceptScheme";

    private readonly Dictionary<string, JsonObject> _docs = new();
    private readonly IGraph _graph = new Graph();
    private readonly TripleStore _store = new();
    private readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        IndentSize = 4,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private JsonObject _context = BuildContext();

    public IGraph GetGraph()
    {
        return _graph;
    }

    public JsonNode? GetContextTerm(string term)
    {
        return _context[term];
    }

    public void AddContextTerm(string term, string uri)
    {
        AddContextValue(term, uri);
    }

    public void AddContextTermField(string term, string field, string value)
    {
        if (_context[term] != null)
            throw new InvalidOperationException($"Context term already exists: {term}");

        _context[term] = new JsonObject
        {
            [field] = value
        };
    }

    public void AddContextTermType(string term, string type)
    {
        AddContextObject(term, "@type", type);
    }

    public void AddContextTermContainer(string term, string container)
    {
        AddContextObject(term, "@container", container);
    }

    public void AddContextTermTypeAndContainer(string term, string type, string container)
    {
        if (_context[term] != null)
            throw new InvalidOperationException($"Context term already exists: {term}");

        _context[term] = new JsonObject
        {
            ["@type"] = type,
            ["@container"] = container
        };
    }

    public void RemoveContext(string term)
    {
        _context.Remove(term);
    }

    public void SetContextTermType(string term, string type)
    {
        RequireContextObject(term);
        _context[term]!.AsObject()["@type"] = type;
    }

    public void SetContextTermContainer(string term, string container)
    {
        RequireContextObject(term);
        _context[term]!.AsObject()["@container"] = container;
    }

    private void AddContextValue(string term, string value)
    {
        if (_context[term] != null)
            throw new InvalidOperationException($"Context term already exists: {term}");

        _context[term] = value;
    }

    private void AddContextObject(string term, string key, string value)
    {
        if (_context[term] != null)
            throw new InvalidOperationException($"Context term already exists: {term}");

        _context[term] = new JsonObject
        {
            [key] = value
        };
    }

    private void RequireContextObject(string term)
    {
        if (_context[term] == null)
            throw new InvalidOperationException($"Context term does not exist: {term}");

        if (_context[term] is not JsonObject)
            throw new InvalidOperationException($"Context term is not an object: {term}");
    }

    public void UpdateContextTerm(string term, JsonNode value)
    {
        if (_context[term] == null)
            throw new InvalidOperationException($"Term does not exist: {term}");

        _context[term] = value;
    }

    public void RemoveContextTerm(string term)
    {
        if (_context[term] == null)
            return;

        _context.Remove(term);
    }

    public void RemoveContextTermProperty(string term, string property)
    {
        RequireContextObject(term);

        var obj = _context[term]!.AsObject();
        obj.Remove(property);
    }

    public void SetContextField(string term, string field, string value)
    {
        RequireContextObject(term);

        var obj = _context[term]!.AsObject();
        obj[field] = value;
    }

    public void RemoveContextField(string term, string field)
    {
        RequireContextObject(term);

        var obj = _context[term]!.AsObject();
        obj.Remove(field);
    }

    public void LoadFromFolder(string folder)
    {
        LoadContextFile(folder);

        var parser = new JsonLdParser();
        var resolver = new UrlResolver("cache");

        JsonNode? sharedContext = null;
        var graphArray = new JsonArray();

        foreach (var file in Directory.GetFiles(folder, "*.jsonld", SearchOption.AllDirectories))
        {
            // Skip files that are not schema items.
            var name = Path.GetFileName(file);
            if (name == "_meta.json" ||
                name == "ctdl-context.jsonld" ||
                name == "ctdl-schema.jsonld")
                continue;

            var text = File.ReadAllText(file);
            var original = JsonNode.Parse(text)!.AsObject();

            var id = original["@id"]!.ToString();

            var term = GetTermOrId(id);

            // Store orignal unchanged (for round-trip)
            _docs[term] = original;

            // Capture context once
            if (sharedContext == null && original["@context"] != null)
                sharedContext = original["@context"]!.DeepClone();

            // Work on a clone for RDF processing
            var working = original.DeepClone().AsObject();

            // Remove context ONLY for parsing
            working.Remove("@context");

            // Rewrite (optional)
            var rewritten = JsonLdRewriter.Rewrite(working, resolver);

            graphArray.Add(rewritten);
        }

        var root = new JsonObject
        {
            ["@context"] = sharedContext,
            ["@graph"] = graphArray
        };

        using var reader = new StringReader(root.ToJsonString());
        parser.Load(_store, reader);

        foreach (var g in _store.Graphs)
            _graph.Merge(g);
    }

    private void LoadContextFile(string folder)
    {
        var rootFolder = Directory.GetParent(folder)?.FullName ?? folder;
        var contextPath = Path.Combine(rootFolder, "ctdl-context.jsonld");

        if (!File.Exists(contextPath))
            return;

        var contextDoc = JsonNode.Parse(File.ReadAllText(contextPath))!.AsObject();

        if (contextDoc["@context"] is JsonObject context)
            _context = context.DeepClone().AsObject();
    }

    public void SaveFolder(string folder)
    {
        Directory.CreateDirectory(folder);

        var splitFolder = Path.Combine(folder, "Split");
        var mergedFolder = Path.Combine(folder, "Merged");

        Directory.CreateDirectory(splitFolder);
        Directory.CreateDirectory(mergedFolder);

        // Write Context.
        var contextPath = Path.Combine(folder, "ctdl-context.jsonld");

        var contextDoc = new JsonObject
        {
            ["@context"] = _context.DeepClone()
        };

        File.WriteAllText(contextPath, contextDoc.ToJsonString(_jsonSerializerOptions));

        // Write Split files.
        foreach (var kv in _docs.ToList())
        {
            var obj = kv.Value;

            obj["@context"] = "https://credreg.net/ctdl/schema/context/json";

            // Determine subfolder from @type.
            var subFolder = GetTypeFolder(obj);
            var subFolderPath = Path.Combine(splitFolder, subFolder);

            Directory.CreateDirectory(subFolderPath);

            // Use context term instead of URI.
            var term = kv.Key;
            var fileName = term.Replace(":", "_") + ".jsonld";
            var path = Path.Combine(subFolderPath, fileName);

            var jsonText = obj.ToJsonString(_jsonSerializerOptions);

            File.WriteAllText(path, jsonText);

            _docs[kv.Key] = JsonNode.Parse(jsonText)!.AsObject();
        }

        // Write Merged file.
        var graphArray = new JsonArray();

        foreach (var doc in _docs.Values
             .OrderBy(GetTypeSortOrder)
             .ThenBy(d => d["@id"]?.ToString()))
        {
            var clone = doc.DeepClone().AsObject();

            // @context belongs only at the root of the merged document
            clone.Remove("@context");

            graphArray.Add(clone);
        }

        var merged = new JsonObject
        {
            ["@context"] = "https://credreg.net/ctdl/schema/context/json",
            ["@graph"] = graphArray
        };

        var mergedPath = Path.Combine(mergedFolder, "ctdl-schema.jsonld");

        File.WriteAllText(mergedPath, merged.ToJsonString(_jsonSerializerOptions));
    }

    private static int GetTypeSortOrder(JsonObject obj)
    {
        return obj["@type"]?.ToString() switch
        {
            RDFS_CLASS_TERM => 0,
            RDF_PROPERTY_TERM => 1,
            SKOS_CONCEPT_SCHEME_TERM => 2,
            SKOS_CONCEPT_TERM => 3,
            _ => 999
        };
    }

    private string GetTypeFolder(JsonObject obj)
    {
        var type = obj["@type"]?.ToString();

        return type switch
        {
            RDFS_CLASS_TERM => "classes",
            RDF_PROPERTY_TERM => "properties",
            SKOS_CONCEPT_TERM => "concepts",
            SKOS_CONCEPT_SCHEME_TERM => "conceptschemes",
            _ => "other"
        };
    }

    public void CreateClass(string term)
    {
        RequireContextTerm(term);

        var obj = new JsonObject
        {
            ["@context"] = _context.DeepClone(),
            ["@id"] = term,
            ["@type"] = RDFS_CLASS_TERM
        };

        _docs[term] = obj;

        AddTriple(term, RDF_TYPE, RDFS_CLASS);
    }

    public void DeleteClass(string term)
    {
        DeleteSchemaItem(term, RDFS_CLASS_TERM);
    }

    public void DeleteProperty(string term)
    {
        DeleteSchemaItem(term, RDF_PROPERTY_TERM);
    }

    public void DeleteConcept(string term)
    {
        DeleteSchemaItem(term, SKOS_CONCEPT_TERM);
    }

    public void DeleteConceptScheme(string term)
    {
        DeleteSchemaItem(term, SKOS_CONCEPT_SCHEME_TERM);
    }

    public void CreateProperty(string term)
    {
        RequireContextTerm(term);

        var obj = new JsonObject
        {
            ["@context"] = _context.DeepClone(),
            ["@id"] = term,
            ["@type"] = RDF_PROPERTY_TERM
        };

        _docs[term] = obj;

        AddTriple(term, RDF_TYPE, RDF_PROPERTY);
    }

    public void SetField(string subject, string predicate, string value)
    {
        var obj = GetDoc(subject);

        obj[predicate] = value;

        RemoveTriples(subject, predicate);
        AddLiteralTriple(subject, predicate, value);
    }

    public void RemoveField(string subject, string predicate)
    {
        var obj = GetDoc(subject);

        obj.Remove(predicate);

        RemoveTriples(subject, predicate);
    }

    public void CreateConcept(string term)
    {
        RequireContextTerm(term);

        var obj = new JsonObject
        {
            ["@context"] = _context.DeepClone(),
            ["@id"] = term,
            ["@type"] = SKOS_CONCEPT_TERM
        };

        _docs[term] = obj;

        AddTriple(term, RDF_TYPE, SKOS_CONCEPT);
    }

    public void CreateConceptScheme(string term)
    {
        RequireContextTerm(term);

        var obj = new JsonObject
        {
            ["@context"] = _context.DeepClone(),
            ["@id"] = term,
            ["@type"] = SKOS_CONCEPT_SCHEME_TERM
        };

        _docs[term] = obj;

        AddTriple(term, RDF_TYPE, SKOS_CONCEPT_SCHEME);
    }

    public void SetLanguageProperty(string subject, string predicate, string language, string value)
    {
        var obj = GetDoc(subject);

        if (obj[predicate] == null || obj[predicate] is not JsonObject)
            obj[predicate] = new JsonObject();

        obj[predicate]!.AsObject()[language] = value;

        RemoveTriples(subject, predicate);
        AddLiteralTriple(subject, predicate, value);
    }

    public void AddFieldValue(string subject, string predicate, string value)
    {
        AddJsonArrayValue(subject, predicate, value);

        AddTriple(
            GetUriOrId(subject),
            GetUriOrId(predicate),
            GetUriOrId(value)
        );
    }

    public void RemoveFieldValue(string subject, string predicate, string value)
    {
        RemoveJsonArrayValue(subject, predicate, value);

        RemoveTriple(
            GetUriOrId(subject),
            GetUriOrId(predicate),
            GetUriOrId(value)
        );
    }

    public IEnumerable<string> GetAllClasses()
    {
        return GetByType(RDFS_CLASS_TERM)
            .Select(d => d["@id"]!.ToString())
            .OrderBy(x => x);
    }

    public IEnumerable<string> GetAllProperties()
    {
        return GetByType(RDF_PROPERTY_TERM)
            .Select(d => d["@id"]!.ToString())
            .OrderBy(x => x);
    }

    public IEnumerable<string> GetAllConcepts()
    {
        return GetByType(SKOS_CONCEPT_TERM)
            .Select(d => d["@id"]!.ToString())
            .OrderBy(x => x);
    }

    public IEnumerable<string> GetAllConceptSchemes()
    {
        return GetByType(SKOS_CONCEPT_SCHEME_TERM)
            .Select(d => d["@id"]!.ToString())
            .OrderBy(x => x);
    }

    public JsonObject? GetSchemaItem(string term)
    {
        if (_docs.TryGetValue(term, out var item))
            return item;

        return null;
    }

    public IEnumerable<string> GetPropertiesOfClass(string classIdOrTerm)
    {
        var classTerm = classIdOrTerm;

        return GetByType(RDF_PROPERTY_TERM)
            .Where(d =>
            {
                var domain = d["schema:domainIncludes"];

                if (domain == null)
                    return false;

                // Single value
                if (domain is JsonValue) return domain.ToString() == classTerm;

                // Array
                if (domain is JsonArray arr) return arr.Any(x => x!.ToString() == classTerm);

                return false;
            })
            .Select(d => d["@id"]!.ToString())
            .OrderBy(x => x);
    }

    public IEnumerable<string> GetClassesUsingProperty(string propertyIdOrTerm)
    {
        var property = GetDoc(propertyIdOrTerm);

        var domain = property["schema:domainIncludes"];

        if (domain == null)
            return Enumerable.Empty<string>();

        if (domain is JsonValue)
            return new[] { domain.ToString() };

        if (domain is JsonArray arr)
            return arr.Select(x => x!.ToString());

        return Enumerable.Empty<string>();
    }

    public IEnumerable<string> GetRangeOfProperty(string propertyIdOrTerm)
    {
        var property = GetDoc(propertyIdOrTerm);

        var range = property["schema:rangeIncludes"];

        if (range == null)
            return Enumerable.Empty<string>();

        if (range is JsonValue)
            return new[] { range.ToString() };

        if (range is JsonArray arr)
            return arr.Select(x => x!.ToString());

        return Enumerable.Empty<string>();
    }

    public IEnumerable<string> GetPropertiesWithRange(string classIdOrTerm)
    {
        var classTerm = classIdOrTerm;

        return _docs.Values
            .Where(d => d["@type"]?.ToString() == RDF_PROPERTY_TERM)
            .Where(d =>
            {
                var range = d["schema:rangeIncludes"];

                if (range == null)
                    return false;

                // Single value
                if (range is JsonValue) return range.ToString() == classTerm;

                // Array
                if (range is JsonArray arr)
                    return arr.Any(x =>
                        x!.ToString() == classTerm);

                return false;
            })
            .Select(d => d["@id"]!.ToString())
            .OrderBy(x => x);
    }

    public IEnumerable<string> GetSubClasses(string classIdOrTerm)
    {
        var classTerm = classIdOrTerm;

        return _docs.Values
            .Where(d => d["@type"]?.ToString() == RDFS_CLASS_TERM)
            .Where(d =>
            {
                var sub = d["rdfs:subClassOf"];

                if (sub == null)
                    return false;

                if (sub is JsonValue)
                    return sub.ToString() == classTerm;

                if (sub is JsonArray arr)
                    return arr.Any(x => x!.ToString() == classTerm);

                return false;
            })
            .Select(d => d["@id"]!.ToString())
            .OrderBy(x => x);
    }

    public IEnumerable<string> GetConceptsInScheme(string schemeIdOrTerm)
    {
        var schemeTerm = schemeIdOrTerm;

        return _docs.Values
            .Where(d => d["@type"]?.ToString() == SKOS_CONCEPT_TERM)
            .Where(d =>
            {
                var scheme = d["skos:inScheme"];

                if (scheme == null)
                    return false;

                if (scheme is JsonValue)
                    return scheme.ToString() == schemeTerm;

                if (scheme is JsonArray arr)
                    return arr.Any(x => x!.ToString() == schemeTerm);

                return false;
            })
            .Select(d => d["@id"]!.ToString())
            .OrderBy(x => x);
    }

    public bool ClassExists(string idOrTerm)
    {
        var item = GetSchemaItem(idOrTerm);

        return item != null &&
               item["@type"]?.ToString() == RDFS_CLASS_TERM;
    }

    public bool PropertyExists(string idOrTerm)
    {
        var item = GetSchemaItem(idOrTerm);

        return item != null &&
               item["@type"]?.ToString() == RDF_PROPERTY_TERM;
    }

    public bool ConceptExists(string idOrTerm)
    {
        var item = GetSchemaItem(idOrTerm);

        return item != null &&
               item["@type"]?.ToString() == SKOS_CONCEPT_TERM;
    }

    public bool ConceptSchemeExists(string idOrTerm)
    {
        var item = GetSchemaItem(idOrTerm);

        return item != null &&
               item["@type"]?.ToString() == SKOS_CONCEPT_SCHEME_TERM;
    }

    private IEnumerable<JsonObject> GetByType(string type)
    {
        return _docs.Values
            .Where(d => d["@type"]?.ToString() == type);
    }

    private JsonObject GetDoc(string term)
    {
        if (_docs.TryGetValue(term, out var doc))
            return doc;

        throw new InvalidOperationException(
            $"Schema item not found: {term}");
    }

    public string GetUriOrId(string term)
    {
        // Full URI
        if (term.StartsWith("http"))
            return term;

        // CURIE lookup FIRST
        if (term.Contains(':'))
        {
            var prefix = term.Split(':', 2)[0];
            var suffix = term.Split(':', 2)[1];

            if (_context[prefix] != null)
                return _context[prefix]!.ToString() + suffix;
        }

        // Direct term lookup SECOND
        if (_context[term] is JsonValue value)
            return value.ToString();

        if (_context[term] is JsonObject obj && obj["@id"] != null)
            return obj["@id"]!.ToString();

        return term;
    }

    private string GetTermOrId(string id)
    {
        // Already a plain term
        if (_context[id] != null)
            return id;

        // CURIE
        if (id.Contains(':') && !id.StartsWith("http"))
            return id;

        // URI lookup
        foreach (var kv in _context)
            if (kv.Value?.ToString() == id)
                return kv.Key;

        throw new InvalidOperationException(
            $"No term or CURIE available for URI: {id}");
    }

    private void RequireContextTerm(string term)
    {
        if (_context[term] == null)
            throw new InvalidOperationException(
                $"Context term must be added before creating schema item: {term}");

        var existingUri = _context[term]!.ToString()
                          ?? throw new InvalidOperationException(
                              $"Context term '{term}' does not have a URI.");

        if (!Uri.TryCreate(existingUri, UriKind.Absolute, out _))
            throw new InvalidOperationException(
                $"Context term '{term}' maps to '{existingUri}'.");
    }

    private void AddJsonArrayValue(string id, string key, string value)
    {
        var obj = GetDoc(id);

        if (obj[key] == null)
            obj[key] = new JsonArray { value };
        else if (obj[key] is JsonArray arr)
            arr.Add(value);
        else
            obj[key] = new JsonArray { obj[key]!, value };
    }

    private void RemoveJsonArrayValue(string id, string key, string value)
    {
        var obj = GetDoc(id);

        if (obj[key] is JsonArray arr)
        {
            var match = arr.FirstOrDefault(x => x!.ToString() == value);
            if (match != null) arr.Remove(match);
        }
    }

    private void DeleteSchemaItem(string term, string expectedType)
    {
        var key = ResolveExistingKey(term);

        var doc = GetDoc(key);

        var actualType = doc["@type"]?.ToString();

        if (actualType != expectedType)
        {
            throw new InvalidOperationException(
                $"'{term}' is not of type '{expectedType}'.");
        }

        EnsureNoReferences(key);

        var removed = _docs.Remove(key);

        if (!removed)
        {
            throw new InvalidOperationException(
                $"Failed to remove schema item: {key}");
        }

        RemoveAllTriples(key);
    }

    public (bool conforms, string reportText) ValidateWithShacl(string shapesPath)
    {
        var shapesGraph = new Graph();
        FileLoader.Load(shapesGraph, shapesPath);

        var shapes = new ShapesGraph(shapesGraph);

        var report = shapes.Validate(_graph);

        if (report.Conforms)
            return (true, "Conforms");

        var results = report.Results.Select(r =>
            $"FocusNode: {r.FocusNode}\n" +
            $"Path: {r.ResultPath}\n" +
            $"Message: {string.Join(", ", r.Message)}"
        );

        return (false, string.Join("\n---\n", results));
    }

    private void AddTriple(string s, string p, string o)
    {
        _graph.Assert(
            Node(GetUriOrId(s)),
            Node(GetUriOrId(p)),
            Node(GetUriOrId(o))
        );
    }

    private void AddLiteralTriple(string s, string p, string v)
    {
        _graph.Assert(
            Node(GetUriOrId(s)),
            Node(GetUriOrId(p)),
            _graph.CreateLiteralNode(v));
    }

    private void RemoveTriple(string s, string p, string o)
    {
        _graph.Retract(
            new Triple(
                Node(GetUriOrId(s)),
                Node(GetUriOrId(p)),
                Node(GetUriOrId(o))
            )
        );
    }

    private void RemoveTriples(string s, string p)
    {
        var subj = Node(GetUriOrId(s));
        var pred = Node(GetUriOrId(p));

        var triples = _graph
            .GetTriplesWithSubjectPredicate(subj, pred)
            .ToList();

        _graph.Retract(triples);
    }

    private void RemoveAllTriples(string term)
    {
        var node = Node(GetUriOrId(term));

        var subjectTriples = _graph
            .GetTriplesWithSubject(node)
            .ToList();

        var objectTriples = _graph
            .GetTriplesWithObject(node)
            .ToList();

        _graph.Retract(subjectTriples);
        _graph.Retract(objectTriples);
    }

    private IUriNode Node(string uri)
    {
        return _graph.CreateUriNode(UriFactory.Create(uri));
    }

    private static JsonObject BuildContext()
    {
        return new JsonObject
        {
            ["ceterms"] = "https://credreg.net/ctdl/terms/",
            ["schema"] = "https://schema.org/",
            ["rdf"] = "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
            ["rdfs"] = "http://www.w3.org/2000/01/rdf-schema#"
        };
    }

    private void EnsureNoReferences(string term)
    {
        var references = FindReferences(term).ToList();

        if (references.Count == 0)
            return;

        var details = string.Join(
            Environment.NewLine,
            references.Select(r =>
                $"  - {r.Subject} -> {r.Predicate} -> {term}"));

        throw new InvalidOperationException(
            $"Cannot delete '{term}' because it is still referenced:{Environment.NewLine}{details}");
    }

    private IEnumerable<(string Subject, string Predicate)> FindReferences(string term)
    {
        foreach (var doc in _docs.Values)
        {
            var subject = GetDocumentSubject(doc);

            if (subject == null)
                continue;

            foreach (var property in GetReferenceProperties(doc))
                if (PropertyContainsTerm(property.Value, term))
                    yield return (subject, property.Key);
        }
    }

    private static string? GetDocumentSubject(JsonObject doc)
    {
        var subject = doc["@id"]?.ToString();

        return string.IsNullOrWhiteSpace(subject)
            ? null
            : subject;
    }

    private static IEnumerable<KeyValuePair<string, JsonNode?>> GetReferenceProperties(JsonObject doc)
    {
        return doc.Where(p => !p.Key.StartsWith('@'));
    }

    private static bool PropertyContainsTerm(JsonNode? value, string term)
    {
        if (value == null)
            return false;

        return value switch
        {
            JsonValue jsonValue => JsonValueMatches(jsonValue, term),
            JsonArray jsonArray => JsonArrayMatches(jsonArray, term),
            JsonObject jsonObject => JsonObjectMatches(jsonObject, term),
            _ => false
        };
    }

    private static bool JsonValueMatches(JsonValue value, string term)
    {
        return value.ToString() == term;
    }

    private static bool JsonArrayMatches(JsonArray array, string term)
    {
        return array.Any(item => item?.ToString() == term);
    }

    private static bool JsonObjectMatches(JsonObject obj, string term)
    {
        return obj.Any(kv => kv.Value?.ToString() == term);
    }

    private string ResolveExistingKey(string term)
    {
        if (_docs.ContainsKey(term))
            return term;

        foreach (var kv in _docs)
        {
            var id = kv.Value["@id"]?.ToString();

            if (string.Equals(id, term, StringComparison.Ordinal))
                return kv.Key;
        }

        throw new InvalidOperationException(
            $"Schema item not found: {term}");
    }
}