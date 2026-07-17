using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Shacl;
using Path = System.IO.Path;

namespace Schema.SDK;

/// <summary>
/// Provides a lossless editor for a JSON-LD based schema.
/// This class manages an in-memory representation of schema documents, a JSON-LD context
/// and an RDF graph (using dotNetRDF) that stays in sync with the JSON documents.
/// Use this API to load existing schema files, modify terms, classes, properties and concepts,
/// and persist the schema back to disk while keeping RDF triples consistent.
/// </summary>
public class SchemaApi
{
    private const string SKOS_CONCEPT = "http://www.w3.org/2004/02/skos/core#Concept";
    private const string SKOS_CONCEPT_SCHEME = "http://www.w3.org/2004/02/skos/core#ConceptScheme";

    private const string RDF_TYPE = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string RDFS_CLASS = "http://www.w3.org/2000/01/rdf-schema#Class";
    private const string RDF_PROPERTY = "http://www.w3.org/1999/02/22-rdf-syntax-ns#Property";

    // JSON-LD type term constants to avoid repeated string literals
    private const string RDFS_CLASS_TERM = "rdfs:Class";
    private const string RDF_PROPERTY_TERM = "rdf:Property";
    private const string SKOS_CONCEPT_TERM = "skos:Concept";
    private const string SKOS_CONCEPT_SCHEME_TERM = "skos:ConceptScheme";

    private readonly Dictionary<string, JsonObject> _docs = new();
    private readonly IGraph _graph = new Graph();

    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true,
        IndentSize = 4,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NewLine = "\n"
    };

    private readonly TripleStore _store = new();

    private JsonObject _context = BuildContext();

    /// <summary>
    /// Gets the internal RDF graph representation maintained by the API.
    /// </summary>
    /// <returns>The dotNetRDF <see cref="IGraph" /> instance containing triples for the current documents.</returns>
    public IGraph GetGraph()
    {
        return _graph;
    }

    /// <summary>
    /// Retrieves the JSON-LD context mapping for a given term.
    /// </summary>
    /// <param name="term">The context term or CURIE to look up.</param>
    /// <returns>The <see cref="JsonNode" /> that represents the context entry, or null if missing.</returns>
    public JsonNode? GetContextTerm(string term)
    {
        return _context[term];
    }

    /// <summary>
    /// Adds a context term whose value is an object with a single field.
    /// </summary>
    /// <param name="term">The context term to add.</param>
    /// <param name="field">The field name (e.g. "@id" or "@type").</param>
    /// <param name="value">The value for the field.</param>
    public void AddContextTermField(string term, string field, string value)
    {
        if (_context[term] != null)
            throw new InvalidOperationException($"Context term already exists: {term}");

        _context[term] = new JsonObject
        {
            [field] = value
        };
    }

    /// <summary>
    /// Adds a context term with an explicit @type mapping.
    /// </summary>
    /// <param name="term">The context term to add.</param>
    /// <param name="type">The type mapping (e.g. "@id" target or datatype).</param>
    public void AddContextTermType(string term, string type)
    {
        AddContextObject(term, "@type", type);
    }

    /// <summary>
    /// Adds a context term with a @container mapping (e.g. "@language").
    /// </summary>
    /// <param name="term">The context term to add.</param>
    /// <param name="container">The container mapping.</param>
    public void AddContextTermContainer(string term, string container)
    {
        AddContextObject(term, "@container", container);
    }

    /// <summary>
    /// Adds a context term object containing both @type and @container entries.
    /// </summary>
    /// <param name="term">The context term to add.</param>
    /// <param name="type">The type mapping.</param>
    /// <param name="container">The container mapping.</param>
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

    /// <summary>
    /// Removes a context term completely from the current context.
    /// </summary>
    /// <param name="term">The context term to remove.</param>
    public void RemoveContext(string term)
    {
        _context.Remove(term);
    }

    /// <summary>
    /// Sets or replaces the @type value for an existing context term that is an object.
    /// </summary>
    /// <param name="term">The context term to modify.</param>
    /// <param name="type">The new @type value.</param>
    public void SetContextTermType(string term, string type)
    {
        RequireContextObject(term);
        _context[term]!.AsObject()["@type"] = type;
    }

    /// <summary>
    /// Sets or replaces the @container value for an existing context term that is an object.
    /// </summary>
    /// <param name="term">The context term to modify.</param>
    /// <param name="container">The new @container value.</param>
    public void SetContextTermContainer(string term, string container)
    {
        RequireContextObject(term);
        _context[term]!.AsObject()["@container"] = container;
    }

    /// <summary>
    /// Updates an existing context term with a new JsonNode value.
    /// </summary>
    /// <param name="term">The context term to update.</param>
    /// <param name="value">The new JsonNode value to assign to the term.</param>
    public void UpdateContextTerm(string term, JsonNode value)
    {
        if (_context[term] == null)
            throw new InvalidOperationException($"Term does not exist: {term}");

        _context[term] = value;
    }

    /// <summary>
    /// Removes a context term if it exists.
    /// </summary>
    /// <param name="term">The context term to remove.</param>
    public void RemoveContextTerm(string term)
    {
        if (_context[term] == null)
            return;

        _context.Remove(term);
    }

    /// <summary>
    /// Removes a property from a context term object.
    /// </summary>
    /// <param name="term">The context term to modify.</param>
    /// <param name="property">The property name to remove (e.g. "@type").</param>
    public void RemoveContextTermProperty(string term, string property)
    {
        RequireContextObject(term);

        var obj = _context[term]!.AsObject();
        obj.Remove(property);
    }

    /// <summary>
    /// Sets a field on a context term object.
    /// </summary>
    /// <param name="term">The context term to modify.</param>
    /// <param name="field">The field name (e.g. "@container").</param>
    /// <param name="value">The value to set.</param>
    public void SetContextField(string term, string field, string value)
    {
        RequireContextObject(term);

        var obj = _context[term]!.AsObject();
        obj[field] = value;
    }

    /// <summary>
    /// Removes a field from a context term object.
    /// </summary>
    /// <param name="term">The context term to modify.</param>
    /// <param name="field">The field name to remove.</param>
    public void RemoveContextField(string term, string field)
    {
        RequireContextObject(term);

        var obj = _context[term]!.AsObject();
        obj.Remove(field);
    }

    /// <summary>
    /// Load schema documents from a split folder (files organized under subfolders).
    /// This will populate the in-memory document map and build the RDF graph representation.
    /// </summary>
    /// <param name="folder">Path to the folder containing split JSON-LD files.</param>
    public void LoadFromFolder(string folder)
    {
        LoadContextFile(folder);

        var parser = new JsonLdParser();
        var resolver = new UrlResolver("cache");

        JsonNode? sharedContext = null;
        var graphArray = new JsonArray();

        var splitFolder = Path.Combine(folder, "Split");

        foreach (var file in Directory.GetFiles(splitFolder, "*.jsonld", SearchOption.AllDirectories))
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

    /// <summary>
    /// Persist the current in-memory schema to disk. Produces a Split/ and Merged/ layout.
    /// Also writes a ctdl-context.jsonld at the root of the folder.
    /// </summary>
    /// <param name="folder">Output directory to write the split and merged representations.</param>
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

    /// <summary>
    /// Create a new class schema item using the provided context term.
    /// </summary>
    /// <param name="term">Context term for the new class (must exist in the current context).</param>
    public void CreateClass(string term)
    {
        RequireNamespaceExists(term);

        var obj = new JsonObject
        {
            ["@context"] = _context.DeepClone(),
            ["@id"] = term,
            ["@type"] = RDFS_CLASS_TERM
        };

        _docs[term] = obj;

        AddTriple(term, RDF_TYPE, RDFS_CLASS);
    }

    /// <summary>
    /// Delete a class schema item by term.
    /// </summary>
    /// <param name="term">The class term or id to delete.</param>
    public void DeleteClass(string term)
    {
        DeleteSchemaItem(term, RDFS_CLASS_TERM);
    }

    /// <summary>
    /// Delete a property schema item by term.
    /// </summary>
    /// <param name="term">The property term or id to delete.</param>
    public void DeleteProperty(string term)
    {
        DeleteSchemaItem(term, RDF_PROPERTY_TERM);
    }

    /// <summary>
    /// Delete a SKOS concept schema item by term.
    /// </summary>
    /// <param name="term">The concept term or id to delete.</param>
    public void DeleteConcept(string term)
    {
        DeleteSchemaItem(term, SKOS_CONCEPT_TERM);
    }

    /// <summary>
    /// Delete a SKOS concept scheme schema item by term.
    /// </summary>
    /// <param name="term">The concept scheme term or id to delete.</param>
    public void DeleteConceptScheme(string term)
    {
        DeleteSchemaItem(term, SKOS_CONCEPT_SCHEME_TERM);
    }

    /// <summary>
    /// Create a new property schema item using the provided context term.
    /// </summary>
    /// <param name="term">Context term for the new property (must exist in the current context).</param>
    public void CreateProperty(string term)
    {
        RequireNamespaceExists(term);

        EnsurePropertyContextTerm(term);

        var obj = new JsonObject
        {
            ["@context"] = _context.DeepClone(),
            ["@id"] = term,
            ["@type"] = RDF_PROPERTY_TERM
        };

        _docs[term] = obj;

        AddTriple(term, RDF_TYPE, RDF_PROPERTY);
    }

    private void EnsurePropertyContextTerm(string term)
    {
        if (_context[term] != null)
            return;

        _context[term] = new JsonObject
        {
            ["@type"] = "@id"
        };
    }

    /// <summary>
    /// Set a simple literal field on a schema item and update the RDF graph.
    /// </summary>
    /// <param name="subject">The subject term or id of the schema item to modify.</param>
    /// <param name="predicate">The predicate (context term or CURIE) to set.</param>
    /// <param name="value">The literal value to assign.</param>
    public void SetField(string subject, string predicate, string value)
    {
        var obj = GetDoc(subject);

        obj[predicate] = value;

        RemoveTriples(subject, predicate);
        AddLiteralTriple(subject, predicate, value);
    }

    /// <summary>
    /// Remove a field from a schema item and retract corresponding triples.
    /// </summary>
    /// <param name="subject">The subject term or id of the schema item to modify.</param>
    /// <param name="predicate">The predicate (context term or CURIE) to remove.</param>
    public void RemoveField(string subject, string predicate)
    {
        var obj = GetDoc(subject);

        obj.Remove(predicate);

        RemoveTriples(subject, predicate);
    }

    /// <summary>
    /// Create a new SKOS concept schema item using the provided context term.
    /// </summary>
    /// <param name="term">Context term for the new concept (must exist in the current context).</param>
    public void CreateConcept(string term)
    {
        RequireNamespaceExists(term);

        var obj = new JsonObject
        {
            ["@context"] = _context.DeepClone(),
            ["@id"] = term,
            ["@type"] = SKOS_CONCEPT_TERM
        };

        _docs[term] = obj;

        AddTriple(term, RDF_TYPE, SKOS_CONCEPT);
    }

    /// <summary>
    /// Create a new SKOS concept scheme schema item using the provided context term.
    /// </summary>
    /// <param name="term">Context term for the new concept scheme (must exist in the current context).</param>
    public void CreateConceptScheme(string term)
    {
        RequireNamespaceExists(term);

        var obj = new JsonObject
        {
            ["@context"] = _context.DeepClone(),
            ["@id"] = term,
            ["@type"] = SKOS_CONCEPT_SCHEME_TERM
        };

        _docs[term] = obj;

        AddTriple(term, RDF_TYPE, SKOS_CONCEPT_SCHEME);
    }

    /// <summary>
    /// Set a language-tagged string value for a schema item's property.
    /// This will create or update a language map object for the predicate.
    /// </summary>
    /// <param name="subject">The subject term or id of the schema item to modify.</param>
    /// <param name="predicate">The predicate (context term or CURIE) to set.</param>
    /// <param name="language">The language tag (e.g. "en").</param>
    /// <param name="value">The localized string value.</param>
    public void SetLanguageProperty(string subject, string predicate, string language, string value)
    {
        var obj = GetDoc(subject);

        if (obj[predicate] == null || obj[predicate] is not JsonObject)
            obj[predicate] = new JsonObject();

        obj[predicate]!.AsObject()[language] = value;

        RemoveTriples(subject, predicate);
        AddLiteralTriple(subject, predicate, value);
    }

    /// <summary>
    /// Add a value to a property array for a schema item and assert an object triple in the RDF graph.
    /// </summary>
    /// <param name="subject">The subject term or id.</param>
    /// <param name="predicate">The predicate (context term or CURIE) to which the value will be added.</param>
    /// <param name="value">The value (term or id) to add.</param>
    public void AddFieldValue(string subject, string predicate, string value)
    {
        AddJsonArrayValue(subject, predicate, value);

        AddTriple(
            GetUriOrId(subject),
            GetUriOrId(predicate),
            GetUriOrId(value)
        );
    }

    /// <summary>
    /// Remove a value from a property's array and retract the corresponding object triple.
    /// </summary>
    /// <param name="subject">The subject term or id.</param>
    /// <param name="predicate">The predicate (context term or CURIE) to modify.</param>
    /// <param name="value">The value (term or id) to remove.</param>
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

    /// <summary>
    /// Get all class terms currently stored in the document map.
    /// </summary>
    /// <returns>An enumeration of class terms (context terms) ordered alphabetically.</returns>
    public IEnumerable<string> GetAllProperties()
    {
        return GetByType(RDF_PROPERTY_TERM)
            .Select(d => d["@id"]!.ToString())
            .OrderBy(x => x);
    }

    /// <summary>
    /// Get all property terms currently stored in the document map.
    /// </summary>
    /// <returns>An enumeration of property terms ordered alphabetically.</returns>
    public IEnumerable<string> GetAllConcepts()
    {
        return GetByType(SKOS_CONCEPT_TERM)
            .Select(d => d["@id"]!.ToString())
            .OrderBy(x => x);
    }

    /// <summary>
    /// Get all SKOS concept terms currently stored in the document map.
    /// </summary>
    /// <returns>An enumeration of concept terms ordered alphabetically.</returns>
    public IEnumerable<string> GetAllConceptSchemes()
    {
        return GetByType(SKOS_CONCEPT_SCHEME_TERM)
            .Select(d => d["@id"]!.ToString())
            .OrderBy(x => x);
    }

    public JsonObject? GetSchemaItem(string term)
    {
        return _docs.TryGetValue(term, out var o) ? o : default;
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
            return [domain.ToString()];

        if (domain is JsonArray arr)
            return arr.Select(x => x!.ToString());

        return [];
    }

    /// <summary>
    /// Get the classes (domain) defined on a property.
    /// </summary>
    /// <param name="propertyIdOrTerm">Property term or id to inspect.</param>
    /// <returns>Enumeration of class terms that are in the property's domain.</returns>
    public IEnumerable<string> GetRangeOfProperty(string propertyIdOrTerm)
    {
        var property = GetDoc(propertyIdOrTerm);

        var range = property["schema:rangeIncludes"];

        if (range == null)
            return [];

        if (range is JsonValue)
            return [range.ToString()];

        if (range is JsonArray arr)
            return arr.Select(x => x!.ToString());

        return [];
    }

    /// <summary>
    /// Get the range(s) declared for a given property.
    /// </summary>
    /// <param name="classIdOrTerm">Property term or id to inspect.</param>
    /// <returns>Enumeration of range terms for the property.</returns>
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

    /// <summary>
    /// Get properties that have the provided class in their rangeIncludes.
    /// </summary>
    /// <param name="classIdOrTerm">Class term or id to search for.</param>
    /// <returns>Enumeration of property terms whose range includes the class.</returns>
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

    /// <summary>
    /// Get direct subclasses of the provided class term.
    /// </summary>
    /// <param name="schemeIdOrTerm">Term or id to search for.</param>
    /// <returns>Enumeration of subclass terms.</returns>
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

    /// <summary>
    /// Get concepts that are members of the given concept scheme.
    /// </summary>
    /// <param name="idOrTerm">Concept scheme term or id to search for.</param>
    /// <returns>Enumeration of concept terms in the scheme.</returns>
    public bool ClassExists(string idOrTerm)
    {
        var item = GetSchemaItem(idOrTerm);

        return item != null &&
               item["@type"]?.ToString() == RDFS_CLASS_TERM;
    }

    /// <summary>
    /// Determine whether a class with the given term or id exists.
    /// </summary>
    /// <param name="idOrTerm">Class term or id to check.</param>
    /// <returns>True when the class exists; otherwise false.</returns>
    public bool PropertyExists(string idOrTerm)
    {
        var item = GetSchemaItem(idOrTerm);

        return item != null &&
               item["@type"]?.ToString() == RDF_PROPERTY_TERM;
    }

    /// <summary>
    /// Determine whether a property with the given term or id exists.
    /// </summary>
    /// <param name="idOrTerm">Property term or id to check.</param>
    /// <returns>True when the property exists; otherwise false.</returns>
    public bool ConceptExists(string idOrTerm)
    {
        var item = GetSchemaItem(idOrTerm);

        return item != null &&
               item["@type"]?.ToString() == SKOS_CONCEPT_TERM;
    }

    /// <summary>
    /// Determine whether a SKOS concept with the given term or id exists.
    /// </summary>
    /// <param name="idOrTerm">Concept term or id to check.</param>
    /// <returns>True when the concept exists; otherwise false.</returns>
    public bool ConceptSchemeExists(string idOrTerm)
    {
        var item = GetSchemaItem(idOrTerm);

        return item != null &&
               item["@type"]?.ToString() == SKOS_CONCEPT_SCHEME_TERM;
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

    public string GetUriOrId(string term)
    {
        // Full URI
        if (term.StartsWith("http"))
            return term;

        // CURIE lookup FIRST
        if (term.Contains(':'))
        {
            var prefix = term.Split([ ':' ], 2)[0];
            var suffix = term.Split([ ':' ], 2)[1];

            if (_context[prefix] != null)
                return _context[prefix]! + suffix;
        }

        // Direct term lookup SECOND
        if (_context[term] is JsonValue value)
            return value.ToString();

        if (_context[term] is JsonObject obj && obj["@id"] != null)
            return obj["@id"]!.ToString();

        return term;
    }

    public void CreateNamespace(
        string prefix,
        string uri)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new InvalidOperationException(
                "Namespace prefix is required.");

        if (prefix.Contains(':'))
            throw new InvalidOperationException(
                "Namespace prefix must not contain ':'.");

        if (!Uri.TryCreate(uri, UriKind.Absolute, out _))
            throw new InvalidOperationException(
                $"Invalid namespace URI: {uri}");

        if (_context[prefix] != null)
            throw new InvalidOperationException(
                $"Namespace already exists: {prefix}");

        _context[prefix] = uri;
    }

    public bool NamespaceExists(string prefix)
    {
        return _context[prefix] is JsonValue;
    }

    private void RequireNamespaceExists(string term)
    {
        var parts = term.Split([':'], 2);

        if (parts.Length != 2)
            throw new InvalidOperationException("Term must be a CURIE with a prefix: " + term);

        var prefix = parts[0];

        if (_context[prefix] == null)
            throw new InvalidOperationException(
                $"Namespace '{prefix}' does not exist in context.");
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

    private void LoadContextFile(string folder)
    {
        var contextPath = Path.Combine(folder, "ctdl-context.jsonld");

        if (!File.Exists(contextPath))
            return;

        var contextDoc = JsonNode.Parse(File.ReadAllText(contextPath))!.AsObject();

        if (contextDoc["@context"] is JsonObject context)
            _context = context.DeepClone().AsObject();
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
            throw new InvalidOperationException(
                $"'{term}' is not of type '{expectedType}'.");

        EnsureNoReferences(key);

        var removed = _docs.Remove(key);

        if (!removed)
            throw new InvalidOperationException(
                $"Failed to remove schema item: {key}");

        RemoveAllTriples(key);
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
            //["ceterms"] = "https://credreg.net/ctdl/terms/",
            //["schema"] = "https://schema.org/",
            //["rdf"] = "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
            //["rdfs"] = "http://www.w3.org/2000/01/rdf-schema#"
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
        return doc.Where(p => !p.Key.StartsWith("@"));
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