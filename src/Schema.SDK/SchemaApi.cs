using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Shacl;
using VDS.RDF.Update;
using VDS.RDF.Writing;
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
    private readonly Dictionary<string, string> _preferredLanguageTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonLdPropertyShape> _preferredPropertyShapes = new(StringComparer.Ordinal);
    private readonly IGraph _graph = new Graph();

    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true,
        IndentSize = 4,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NewLine = "\n"
    };

    private TripleStore _store = new();

    private JsonObject _context = BuildContext();
    private string? _contextFileName;
    private string? _mergedFileName;
    private JsonNode? _schemaContext;

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
        _docs.Clear();
        _graph.Clear();
        _store = new TripleStore();
        _preferredLanguageTags.Clear();
        _preferredPropertyShapes.Clear();

        ConfigureSchemaFiles(folder);
        LoadContextFile(folder);

        var parser = new JsonLdParser();
        var resolver = new UrlResolver("cache");

        JsonNode? sharedContext = null;
        var graphArray = new JsonArray();

        var splitFolder = Path.Combine(folder, "Split");

        foreach (var file in Directory
                     .GetFiles(splitFolder, "*", SearchOption.AllDirectories)
                     .Where(IsJsonFile)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            // Skip files that are not schema items.
            var name = Path.GetFileName(file);
            if (name == "_meta.json" ||
                (_contextFileName != null && name.Equals(_contextFileName, StringComparison.OrdinalIgnoreCase)) ||
                (_mergedFileName != null && name.Equals(_mergedFileName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var text = File.ReadAllText(file);
            var original = JsonNode.Parse(text)!.AsObject();

            var id = original["@id"]!.ToString();

            var term = GetTermOrId(id);

            // Store original unchanged and remember the schema's preferred language-tag casing.
            _docs[term] = original;
            CapturePreferredLanguageTags(original);
            CapturePreferredPropertyShapes(term, original);

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

        // Always parse the split documents with the locally loaded context object.
        // Individual documents may reference a remote context URL that does not yet
        // contain newly added namespaces. Using that URL here can cause a CURIE such
        // as ex:FromTurtle to be interpreted as the absolute URI scheme "ex" instead
        // of being expanded to https://example.org/FromTurtle.
        var root = new JsonObject
        {
            ["@context"] = _context.DeepClone(),
            ["@graph"] = graphArray
        };

        using var reader = new StringReader(root.ToJsonString());
        parser.Load(_store, reader);

        foreach (var g in _store.Graphs)
            _graph.Merge(g);
    }


    /// <summary>
    /// Applies a SPARQL 1.1 Update command set to the RDF graph represented by this instance.
    /// The JSON-LD document map is rebuilt from the updated RDF graph so subsequent SDK reads
    /// and saves reflect the update. JSON formatting and compaction may change, but RDF meaning is preserved.
    /// </summary>
    /// <param name="sparqlUpdate">A SPARQL Update command set such as INSERT DATA, DELETE DATA, or DELETE/INSERT WHERE.</param>
    public void ApplySparqlUpdate(string sparqlUpdate)
    {
        if (string.IsNullOrWhiteSpace(sparqlUpdate))
            throw new ArgumentException("SPARQL Update text is required.", nameof(sparqlUpdate));

        var store = CreateStoreFromCurrentGraph();
        var parser = new SparqlUpdateParser();
        var commands = parser.ParseFromString(sparqlUpdate);
        var processor = new LeviathanUpdateProcessor(store);
        processor.ProcessCommandSet(commands);

        ReplaceGraphFromStore(store);
        RebuildDocumentsFromGraph();
    }

    /// <summary>
    /// Writes the current RDF graph as Turtle.
    /// </summary>
    /// <param name="path">Destination Turtle file.</param>
    public void ExportTurtle(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Turtle output path is required.", nameof(path));

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var writer = new CompressingTurtleWriter();
        writer.Save(_graph, path);
    }

    /// <summary>
    /// Replaces the current RDF graph with RDF parsed from a Turtle file.
    /// Load a schema folder first when schema-specific context and output filenames must be retained.
    /// </summary>
    /// <param name="path">Source Turtle file.</param>
    public void ImportTurtle(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Turtle input path is required.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("Turtle input file was not found.", path);

        var imported = new Graph();
        var parser = new TurtleParser();
        parser.Load(imported, path);

        _graph.Clear();
        _graph.Merge(imported);
        RebuildDocumentsFromGraph();
    }

    /// <summary>
    /// Splits the schema-specific merged JSON-LD file into the schema folder's Split directory.
    /// Existing split output is replaced so it exactly reflects the merged file.
    /// </summary>
    /// <param name="folder">Schema folder containing Merged and Split directories.</param>
    public void SplitMergedSchema(string folder)
    {
        ConfigureSchemaFiles(folder);

        var mergedPath = Path.Combine(folder, "Merged", RequireMergedFileName());
        var splitPath = Path.Combine(folder, "Split");

        new JsonLdGraphSplitter().Split(mergedPath, splitPath);
    }

    /// <summary>
    /// Merges the schema folder's Split directory into its schema-specific merged JSON-LD file.
    /// The split metadata is used when present to preserve graph order and root context.
    /// </summary>
    /// <param name="folder">Schema folder containing Merged and Split directories.</param>
    public void MergeSplitSchema(string folder)
    {
        ConfigureSchemaFiles(folder);

        var splitPath = Path.Combine(folder, "Split");
        var mergedPath = Path.Combine(folder, "Merged", RequireMergedFileName());

        new JsonLdGraphSplitter().Merge(
            splitPath,
            mergedPath,
            _schemaContext?.DeepClone());
    }

    /// <summary>
    /// Persist the current in-memory schema to disk. Produces a Split/ and Merged/ layout.
    /// The schema-specific context and merged file names discovered during load are preserved.
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
        var contextPath = Path.Combine(folder, GetContextFileNameForSave());

        var contextDoc = new JsonObject
        {
            ["@context"] = _context.DeepClone()
        };

        File.WriteAllText(contextPath, contextDoc.ToJsonString(_jsonSerializerOptions));

        // Write Split files.
        foreach (var kv in _docs.ToList())
        {
            var obj = kv.Value;

            obj["@context"] = GetSchemaContextForSave();

            // Determine subfolder from @type.
            var subFolder = GetTypeFolder(obj);
            var subFolderPath = Path.Combine(splitFolder, subFolder);

            Directory.CreateDirectory(subFolderPath);

            // Use context term instead of URI.
            var term = kv.Key;
            var fileName = GetSplitFileName(term);
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
            ["@context"] = GetSchemaContextForSave(),
            ["@graph"] = graphArray
        };

        var mergedPath = Path.Combine(mergedFolder, GetMergedFileNameForSave());

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

        AssertObjectTriple(term, RDF_TYPE, RDFS_CLASS);
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

        AssertObjectTriple(term, RDF_TYPE, RDF_PROPERTY);
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
    /// Set the object for a subject-predicate triple and update the RDF graph.
    /// </summary>
    /// <param name="subject">The subject term or id of the schema item to modify.</param>
    /// <param name="predicate">The predicate (context term or CURIE) to set.</param>
    /// <param name="objectValue">The object value to assign.</param>
    public void SetTriple(string subject, string predicate, string objectValue)
    {
        var obj = GetDoc(subject);

        obj[predicate] = objectValue;

        RemoveTriples(subject, predicate);
        AddLiteralTriple(subject, predicate, objectValue);
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

        AssertObjectTriple(term, RDF_TYPE, SKOS_CONCEPT);
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

        AssertObjectTriple(term, RDF_TYPE, SKOS_CONCEPT_SCHEME);
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
    /// Add a subject-predicate-object triple to the schema item and RDF graph.
    /// </summary>
    /// <param name="subject">The subject term or id.</param>
    /// <param name="predicate">The predicate (context term or CURIE) to which the value will be added.</param>
    /// <param name="objectValue">The object term or id to add.</param>
    public void AddTriple(string subject, string predicate, string objectValue)
    {
        AddJsonArrayValue(subject, predicate, objectValue);

        AssertObjectTriple(
            GetUriOrId(subject),
            GetUriOrId(predicate),
            GetUriOrId(objectValue)
        );
    }

    /// <summary>
    /// Remove a subject-predicate-object triple from the schema item and RDF graph.
    /// </summary>
    /// <param name="subject">The subject term or id.</param>
    /// <param name="predicate">The predicate (context term or CURIE) to modify.</param>
    /// <param name="objectValue">The object term or id to remove.</param>
    public void RemoveTriple(string subject, string predicate, string objectValue)
    {
        RemoveJsonArrayValue(subject, predicate, objectValue);

        RetractObjectTriple(
            GetUriOrId(subject),
            GetUriOrId(predicate),
            GetUriOrId(objectValue)
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
            var parts = term.Split(new[] { ':' }, 2);
            var prefix = parts[0];
            var suffix = parts[1];

            if (_context[prefix] != null &&
                TryGetContextIri(_context[prefix], out var namespaceUri))
                return namespaceUri + suffix;
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
        var expanded = type switch
        {
            RDFS_CLASS_TERM => RDFS_CLASS,
            RDF_PROPERTY_TERM => RDF_PROPERTY,
            SKOS_CONCEPT_TERM => SKOS_CONCEPT,
            SKOS_CONCEPT_SCHEME_TERM => SKOS_CONCEPT_SCHEME,
            _ => type
        };

        return _docs.Values.Where(d => HasType(d, type, expanded));
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
        if (HasType(obj, RDFS_CLASS_TERM, RDFS_CLASS)) return 0;
        if (HasType(obj, RDF_PROPERTY_TERM, RDF_PROPERTY)) return 1;
        if (HasType(obj, SKOS_CONCEPT_SCHEME_TERM, SKOS_CONCEPT_SCHEME)) return 2;
        if (HasType(obj, SKOS_CONCEPT_TERM, SKOS_CONCEPT)) return 3;
        return 999;
    }

    private string GetTypeFolder(JsonObject obj)
    {
        if (HasType(obj, RDFS_CLASS_TERM, RDFS_CLASS)) return "classes";
        if (HasType(obj, RDF_PROPERTY_TERM, RDF_PROPERTY)) return "properties";
        if (HasType(obj, SKOS_CONCEPT_TERM, SKOS_CONCEPT)) return "concepts";
        if (HasType(obj, SKOS_CONCEPT_SCHEME_TERM, SKOS_CONCEPT_SCHEME)) return "conceptschemes";
        return "other";
    }

    private string GetTermOrId(string id)
    {
        // Already a plain term defined directly in the context.
        if (_context[id] != null)
            return id;

        // Already a CURIE.
        if (id.Contains(':') && !id.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return id;

        // A context term may map exactly to this URI.
        foreach (var entry in _context)
        {
            if (TryGetContextIri(entry.Value, out var contextIri) &&
                string.Equals(contextIri, id, StringComparison.Ordinal))
                return entry.Key;
        }

        // Otherwise compact the URI through the longest matching prefix namespace.
        var compacted = CompactUri(id);
        if (!string.Equals(compacted, id, StringComparison.Ordinal))
            return compacted;

        throw new InvalidOperationException(
            $"No term or CURIE available for URI: {id}. Add a matching namespace prefix to the schema context before importing RDF or applying SPARQL updates.");
    }


    private TripleStore CreateStoreFromCurrentGraph()
    {
        var store = new TripleStore();
        var graph = new Graph();
        graph.Merge(_graph);
        store.Add(graph);
        return store;
    }

    private void ReplaceGraphFromStore(ITripleStore store)
    {
        _graph.Clear();
        foreach (var graph in store.Graphs)
            _graph.Merge(graph);
    }

    private void RebuildDocumentsFromGraph()
    {
        var store = CreateStoreFromCurrentGraph();
        var writer = new JsonLdWriter();

        using var textWriter = new System.IO.StringWriter();
        writer.Save(store, textWriter, true);

        var expanded = JsonNode.Parse(textWriter.ToString()) as JsonArray
            ?? throw new InvalidOperationException("The RDF graph could not be serialized as expanded JSON-LD.");

        _docs.Clear();

        foreach (var node in expanded.OfType<JsonObject>())
        {
            var idNode = node["@id"];
            if (idNode == null)
                continue;

            var id = idNode.ToString();
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var term = CompactUri(id);
            var compacted = CompactExpandedJsonLd(node, term);
            compacted["@context"] = GetSchemaContextForSave();
            _docs[term] = compacted;
        }
    }

    private JsonObject CompactExpandedJsonLd(JsonObject source, string documentId)
    {
        var result = new JsonObject();

        foreach (var property in source)
        {
            var key = property.Key.StartsWith("@", StringComparison.Ordinal)
                ? property.Key
                : CompactUri(property.Key);

            if (TryCompactLanguageMap(key, property.Value, out var languageMap))
            {
                result[key] = languageMap;
                continue;
            }

            var compactedValue = CompactExpandedValue(property.Key, property.Value);
            result[key] = ApplyPreferredPropertyShape(documentId, key, compactedValue);
        }

        return result;
    }

    private bool TryCompactLanguageMap(
        string compactPropertyName,
        JsonNode? expandedValue,
        out JsonObject languageMap)
    {
        languageMap = new JsonObject();

        if (!IsLanguageContainer(compactPropertyName) || expandedValue is not JsonArray values)
            return false;

        foreach (var valueNode in values)
        {
            if (valueNode is not JsonObject valueObject || valueObject["@value"] == null)
                return false;

            var language = valueObject["@language"]?.ToString() ?? "@none";
            language = GetPreferredLanguageTag(language);
            var compactedValue = valueObject["@value"]!.DeepClone();

            if (languageMap[language] == null)
            {
                languageMap[language] = compactedValue;
                continue;
            }

            if (languageMap[language] is JsonArray existingValues)
            {
                existingValues.Add(compactedValue);
                continue;
            }

            var firstValue = languageMap[language]!.DeepClone();
            languageMap[language] = new JsonArray(firstValue, compactedValue);
        }

        return true;
    }



    private void CapturePreferredPropertyShapes(string documentId, JsonObject document)
    {
        foreach (var property in document)
        {
            if (property.Key == "@context" || property.Value == null)
                continue;

            var value = property.Value;
            var isArray = value is JsonArray;
            var values = value is JsonArray array
                ? array.Where(item => item != null).ToArray()
                : new[] { value };

            var unwrapValueObjects = values.Length > 0 &&
                values.All(item => item is JsonValue);

            _preferredPropertyShapes[GetPropertyShapeKey(documentId, property.Key)] =
                new JsonLdPropertyShape(isArray, unwrapValueObjects);
        }
    }

    private JsonNode? ApplyPreferredPropertyShape(
        string documentId,
        string propertyName,
        JsonNode? value)
    {
        if (value == null)
            return null;

        if (!_preferredPropertyShapes.TryGetValue(
                GetPropertyShapeKey(documentId, propertyName),
                out var shape))
        {
            return value;
        }

        var shapedValue = shape.UnwrapValueObjects
            ? UnwrapValueObjects(value)
            : value;

        if (shapedValue == null)
            return shape.IsArray ? new JsonArray() : null;

        if (shape.IsArray)
        {
            if (shapedValue is JsonArray)
                return shapedValue;

            return new JsonArray(shapedValue.DeepClone());
        }

        if (shapedValue is JsonArray array && array.Count == 1)
            return array[0]?.DeepClone();

        return shapedValue;
    }

    private static JsonNode? UnwrapValueObjects(JsonNode? value)
    {
        if (value is JsonArray array)
        {
            var result = new JsonArray();
            foreach (var item in array)
                result.Add(UnwrapValueObjects(item));
            return result;
        }

        if (value is JsonObject obj && obj.Count == 1 && obj["@value"] != null)
            return obj["@value"]!.DeepClone();

        return value?.DeepClone();
    }

    private static string GetPropertyShapeKey(string documentId, string propertyName)
    {
        return documentId + "\u001F" + propertyName;
    }

    private void CapturePreferredLanguageTags(JsonObject document)
    {
        foreach (var property in document)
        {
            if (!IsLanguageContainer(property.Key) || property.Value is not JsonObject languageMap)
                continue;

            foreach (var languageEntry in languageMap)
            {
                if (string.Equals(languageEntry.Key, "@none", StringComparison.Ordinal))
                    continue;

                var normalized = languageEntry.Key.ToLowerInvariant();
                if (!_preferredLanguageTags.ContainsKey(normalized))
                    _preferredLanguageTags[normalized] = languageEntry.Key;
            }
        }
    }

    private string GetPreferredLanguageTag(string language)
    {
        if (string.Equals(language, "@none", StringComparison.Ordinal))
            return language;

        return _preferredLanguageTags.TryGetValue(language.ToLowerInvariant(), out var preferred)
            ? preferred
            : language;
    }

    private bool IsLanguageContainer(string compactPropertyName)
    {
        return _context[compactPropertyName] is JsonObject definition &&
            string.Equals(
                definition["@container"]?.ToString(),
                "@language",
                StringComparison.Ordinal);
    }

    private JsonNode? CompactExpandedValue(string propertyName, JsonNode? value)
    {
        if (value == null)
            return null;

        if (value is JsonArray array)
        {
            var compacted = new JsonArray();
            foreach (var item in array)
                compacted.Add(CompactExpandedValue(propertyName, item));

            if ((propertyName == "@type" || propertyName == RDF_TYPE) && compacted.Count == 1)
                return compacted[0]?.DeepClone();

            return compacted;
        }

        if (value is JsonObject obj)
        {
            if (obj.Count == 1 && obj["@id"] != null)
                return JsonValue.Create(CompactUri(obj["@id"]!.ToString()));

            var compacted = new JsonObject();
            foreach (var property in obj)
            {
                var key = property.Key.StartsWith("@", StringComparison.Ordinal)
                    ? property.Key
                    : CompactUri(property.Key);
                compacted[key] = CompactExpandedValue(property.Key, property.Value);
            }
            return compacted;
        }

        var text = value.ToString();
        if (propertyName == "@id" || propertyName == "@type" || propertyName == RDF_TYPE)
            return JsonValue.Create(CompactUri(text));

        return value.DeepClone();
    }

    private string CompactUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("_:", StringComparison.Ordinal))
            return value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out _))
            return value;

        string? bestPrefix = null;
        string? bestNamespace = null;

        foreach (var entry in _context)
        {
            if (!TryGetContextIri(entry.Value, out var namespaceUri) ||
                string.IsNullOrWhiteSpace(namespaceUri) ||
                !value.StartsWith(namespaceUri, StringComparison.Ordinal))
                continue;

            if (bestNamespace == null || namespaceUri.Length > bestNamespace.Length)
            {
                bestPrefix = entry.Key;
                bestNamespace = namespaceUri;
            }
        }

        return bestPrefix == null || bestNamespace == null
            ? value
            : bestPrefix + ":" + value.Substring(bestNamespace.Length);
    }

    private static bool TryGetContextIri(JsonNode? value, out string iri)
    {
        iri = string.Empty;

        if (value is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var stringValue) &&
            !string.IsNullOrWhiteSpace(stringValue))
        {
            iri = stringValue;
            return true;
        }

        if (value is JsonObject definition &&
            definition["@id"] is JsonValue idValue &&
            idValue.TryGetValue<string>(out var id) &&
            !string.IsNullOrWhiteSpace(id))
        {
            iri = id;
            return true;
        }

        return false;
    }

    private static bool HasType(JsonObject obj, string compactType, string expandedType)
    {
        var type = obj["@type"];
        if (type is JsonArray array)
            return array.Any(item => item?.ToString() == compactType || item?.ToString() == expandedType);

        var value = type?.ToString();
        return value == compactType || value == expandedType;
    }

    private void ConfigureSchemaFiles(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("Schema folder is required.", nameof(folder));

        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Schema folder was not found: {folder}");

        var splitFolder = Path.Combine(folder, "Split");
        if (!Directory.Exists(splitFolder))
            throw new DirectoryNotFoundException($"Split schema folder was not found: {splitFolder}");

        _contextFileName = FindContextFileName(folder);
        _mergedFileName = FindMergedFileName(folder);
        _schemaContext = ReadSchemaContext(folder);
    }

    private static string FindContextFileName(string folder)
    {
        var candidates = Directory
            .GetFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(IsJsonFile)
            .Where(path => Path.GetFileNameWithoutExtension(path)
                .IndexOf("context", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        if (candidates.Count == 0)
            throw new FileNotFoundException(
                "No JSON or JSON-LD context file was found in the schema folder.",
                folder);

        var groups = candidates
            .GroupBy(
                path => Path.GetFileNameWithoutExtension(path),
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (groups.Count > 1)
            throw new InvalidOperationException(
                $"Multiple distinct context files were found in '{folder}': {string.Join(", ", candidates.Select(Path.GetFileName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))}");

        // Some schema folders contain both <name>.json and <name>.jsonld copies.
        // Treat those as alternate serializations of the same context and prefer JSON-LD.
        var selected = groups[0]
            .OrderByDescending(path => Path.GetExtension(path)
                .Equals(".jsonld", StringComparison.OrdinalIgnoreCase))
            .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .First();

        return Path.GetFileName(selected)
               ?? throw new InvalidOperationException("The context filename could not be determined.");
    }

    private static string FindMergedFileName(string folder)
    {
        var mergedFolder = Path.Combine(folder, "Merged");
        if (!Directory.Exists(mergedFolder))
            throw new DirectoryNotFoundException($"Merged schema folder was not found: {mergedFolder}");

        var candidates = Directory
            .GetFiles(mergedFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(IsJsonFile)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            var metadataPath = Path.Combine(folder, "Split", "_meta.json");
            if (File.Exists(metadataPath))
            {
                var metadata = ParseObjectFile(metadataPath);
                var recordedName = metadata["MergedFileName"]?.ToString()
                                   ?? metadata["mergedFileName"]?.ToString()
                                   ?? throw new InvalidOperationException(
                                    "The merged schema filename could not be determined.");
                if (!string.IsNullOrWhiteSpace(recordedName))
                    return recordedName;
            }

            throw new FileNotFoundException(
                "No merged schema file was found and split metadata does not record its filename.",
                mergedFolder);
        }

        if (candidates.Count > 1)
            throw new InvalidOperationException(
                $"Multiple merged schema files were found in '{mergedFolder}': {string.Join(", ", candidates.Select(Path.GetFileName))}");

        return Path.GetFileName(candidates[0])
               ?? throw new InvalidOperationException("The merged schema filename could not be determined.");
    }

    private JsonNode ReadSchemaContext(string folder)
    {
        var mergedPath = Path.Combine(folder, "Merged", RequireMergedFileName());
        if (File.Exists(mergedPath))
        {
            var mergedRoot = ParseObjectFile(mergedPath);
            var mergedContext = mergedRoot["@context"];
            if (mergedContext != null)
                return mergedContext.DeepClone();
        }

        var metaPath = Path.Combine(folder, "Split", "_meta.json");
        if (File.Exists(metaPath))
        {
            var metadata = ParseObjectFile(metaPath);
            var metadataContext = metadata["Context"] ?? metadata["context"];
            if (metadataContext != null)
                return metadataContext.DeepClone();
        }

        var firstSplitFile = Directory
            .GetFiles(Path.Combine(folder, "Split"), "*", SearchOption.AllDirectories)
            .Where(IsJsonFile)
            .Where(path => !Path.GetFileName(path).Equals("_meta.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (firstSplitFile != null)
        {
            var splitContext = ParseObjectFile(firstSplitFile)["@context"];
            if (splitContext != null)
                return splitContext.DeepClone();
        }

        throw new InvalidOperationException(
            $"The schema context could not be determined from '{folder}'. Add @context to the merged schema file.");
    }

    private static bool IsJsonFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jsonld", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject ParseObjectFile(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        if (node is not JsonObject result)
            throw new InvalidOperationException($"Expected a JSON object in '{path}'.");

        return result;
    }



    private static string GetSplitFileName(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            throw new ArgumentException("A schema term is required.", nameof(term));

        // Preserve the established CURIE filename convention, such as
        // ceterms:Credential -> ceterms_Credential.jsonld.
        var colonIndex = term.IndexOf(':');
        var isCurie = colonIndex > 0
                      && term.IndexOf("//", StringComparison.Ordinal) < 0
                      && term.IndexOf("/", StringComparison.Ordinal) < 0
                      && term.IndexOf('\\') < 0;

        if (isCurie)
            return term.Replace(':', '_') + ".jsonld";

        // Full IRIs and other external identifiers must never be interpreted as
        // paths. Escape the complete identifier into one deterministic filename.
        return Uri.EscapeDataString(term) + ".jsonld";
    }

    private string GetContextFileNameForSave()
    {
        return _contextFileName ?? "context.jsonld";
    }

    private string GetMergedFileNameForSave()
    {
        return _mergedFileName ?? "schema.jsonld";
    }

    private JsonNode GetSchemaContextForSave()
    {
        return (_schemaContext ?? _context).DeepClone();
    }

    private string RequireContextFileName()
    {
        return _contextFileName ?? throw new InvalidOperationException(
            "Schema files have not been configured. Load a schema folder first.");
    }

    private string RequireMergedFileName()
    {
        return _mergedFileName ?? throw new InvalidOperationException(
            "Schema files have not been configured. Load a schema folder first.");
    }

    private JsonNode RequireSchemaContext()
    {
        return _schemaContext ?? throw new InvalidOperationException(
            "Schema context has not been configured. Load a schema folder first.");
    }

    private void LoadContextFile(string folder)
    {
        var contextPath = Path.Combine(folder, RequireContextFileName());

        if (!File.Exists(contextPath))
            return;

        var contextDoc = ParseObjectFile(contextPath);

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

    private void AssertObjectTriple(string subject, string predicate, string objectValue)
    {
        _graph.Assert(
            Node(GetUriOrId(subject)),
            Node(GetUriOrId(predicate)),
            Node(GetUriOrId(objectValue))
        );
    }

    private void AddLiteralTriple(string s, string p, string v)
    {
        _graph.Assert(
            Node(GetUriOrId(s)),
            Node(GetUriOrId(p)),
            _graph.CreateLiteralNode(v));
    }

    private void RetractObjectTriple(string subject, string predicate, string objectValue)
    {
        _graph.Retract(
            new Triple(
                Node(GetUriOrId(subject)),
                Node(GetUriOrId(predicate)),
                Node(GetUriOrId(objectValue))
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
    private sealed class JsonLdPropertyShape
    {
        public JsonLdPropertyShape(bool isArray, bool unwrapValueObjects)
        {
            IsArray = isArray;
            UnwrapValueObjects = unwrapValueObjects;
        }

        public bool IsArray { get; }
        public bool UnwrapValueObjects { get; }
    }

}