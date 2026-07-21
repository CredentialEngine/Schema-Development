using System.CommandLine;
using System.Text.Encodings.Web;
using System.Text.Json;
using Schema.SDK;

namespace Schema.CLI;

/// <summary>
/// Entry point for the Schema CLI tool. Builds and dispatches command-line
/// verbs for manipulating the JSON-LD schema collection.
/// </summary>
internal class Program
{
    protected Program()
    {
    }

    public static async Task<int> Main(string[] args)
    {
        LoadEnv();

        var root = BuildRootCommand();

        var config = new ParserConfiguration
        {
            ResponseFileTokenReplacer = null
        };

        var parseResult = root.Parse(args, config);

        foreach (var error in parseResult.Errors) await Console.Error.WriteLineAsync(error.Message);

        return await parseResult.InvokeAsync();
    }

    private static RootCommand BuildRootCommand()
    {
        var root = new RootCommand("Schema CLI");

        root.Add(BuildInitCommand());
        root.Add(BuildCloneCommand());
        root.Add(BuildApplyCommand());
        root.Add(BuildAddCommand());
        root.Add(BuildUpdateCommand());
        root.Add(BuildRemoveCommand());
        root.Add(BuildSetCommand());
        root.Add(BuildSplitCommand());
        root.Add(BuildMergeCommand());
        root.Add(BuildSparqlCommand());
        root.Add(BuildRdfCommand());
        root.Add(BuildValidateCommand());

        return root;
    }

    private static Command BuildInitCommand()
    {
        var cmd = new Command("init", "Initialize schema");

        var path = new Option<string>("--path")
        {
            Required = true
        };
        cmd.Add(path);

        var schema = new Option<string?>("--schema")
        {
            Description = "Schema folder under the root path: ctdl, ctdlasn, or qdata."
        };
        schema.Validators.Add(result =>
        {
            var schemaValue = result.GetValue(schema);
            if (!string.IsNullOrWhiteSpace(schemaValue) &&
                !SupportedSchemaNames.Contains(schemaValue, StringComparer.OrdinalIgnoreCase))
            {
                result.AddError("--schema must be ctdl, ctdlasn, or qdata.");
            }
        });
        cmd.Add(schema);

        var checkpoints = new Option<int?>("--checkpoints")
        {
            Description = "Number of schema checkpoints to retain. 0 replaces immediately."
        };
        checkpoints.Validators.Add(result =>
        {
            var checkpointValue = result.GetValue(checkpoints);
            if (checkpointValue.HasValue && checkpointValue.Value < 1)
            {
                result.AddError("--checkpoints must be at least 1.");
            }
        });
        cmd.Add(checkpoints);

        cmd.SetAction(context =>
        {
            var p = context.GetValue(path)
                    ?? throw new InvalidOperationException("Path cannot be null.");

            if (!Directory.Exists(p))
                throw new DirectoryNotFoundException(p);

            var schemaName = context.GetValue(schema);
            var schemaPath = ResolveSchemaPath(p, schemaName);
            var cp = context.GetValue(checkpoints);

            WriteEnvFile(schemaPath, cp);

            Console.WriteLine($"Initialized schema at: {schemaPath}");
            Console.WriteLine(".env file created.");
        });

        return cmd;
    }

    private static Command BuildCloneCommand()
    {
        var cmd = new Command("clone", "Clone latest schema version");

        cmd.SetAction(_ => { RunClone(GetOriginalSchemaPath()); });

        return cmd;
    }

    private static Command BuildApplyCommand()
    {
        var cmd = new Command(
            "apply",
            "Replace original schema with latest checkpoint");

        cmd.SetAction(_ =>
        {
            var original = GetOriginalSchemaPath();

            ApplyLatestCheckpoint(original);
        });

        return cmd;
    }

    private static Command BuildAddCommand()
    {
        var cmd = new Command("add", "Add schema items");

        cmd.Add(BuildAddNamespaceCommand());
        cmd.Add(BuildAddClassCommand());
        cmd.Add(BuildAddPropertyCommand());
        cmd.Add(BuildAddConceptCommand());
        cmd.Add(BuildAddConceptSchemeCommand());
        cmd.Add(BuildAddContextCommand());
        cmd.Add(BuildAddTripleCommand());

        return cmd;
    }

    private static Command BuildAddNamespaceCommand()
    {
        var cmd = new Command("namespace", "Add namespace");

        var prefix = new Option<string>("--prefix")
        {
            Required = true
        };

        var uri = new Option<string>("--uri")
        {
            Required = true
        };

        cmd.Add(prefix);
        cmd.Add(uri);

        cmd.SetAction((Action<ParseResult>)(context =>
        {
            var p = context.GetValue(prefix)!;
            var u = context.GetValue(uri)!;

            RunSchemaCommand((Action<SchemaApi>)(api =>
            {
                api.CreateNamespace(p, u);
            }), "add namespace");
        }));

        return cmd;
    }

    private static Command BuildAddClassCommand()
    {
        var cmd = new Command("class", "Add class");

        var term = RequiredTermOption();
        var uri = OptionalUriOption();

        cmd.Add(term);
        cmd.Add(uri);

        cmd.SetAction(context =>
        {
            var t = context.GetValue(term)!;
            var u = context.GetValue(uri);

            RunSchemaCommand(api =>
            {
                ValidateOptionalUri(api, t, u);
                api.CreateClass(t);
            }, "add class");
        });

        return cmd;
    }

    private static Command BuildAddPropertyCommand()
    {
        var cmd = new Command("property", "Add property");

        var term = RequiredTermOption();
        var uri = OptionalUriOption();

        cmd.Add(term);
        cmd.Add(uri);

        cmd.SetAction(context =>
        {
            var t = context.GetValue(term)!;
            var u = context.GetValue(uri);

            RunSchemaCommand(api =>
            {
                ValidateOptionalUri(api, t, u);
                api.CreateProperty(t);
            }, "add property");
        });

        return cmd;
    }

    private static Command BuildAddConceptCommand()
    {
        var cmd = new Command("concept", "Add concept");

        var term = RequiredTermOption();
        var uri = OptionalUriOption();

        cmd.Add(term);
        cmd.Add(uri);

        cmd.SetAction(context =>
        {
            var t = context.GetValue(term)!;
            var u = context.GetValue(uri);

            RunSchemaCommand(api =>
            {
                ValidateOptionalUri(api, t, u);
                api.CreateConcept(t);
            }, "add concept");
        });

        return cmd;
    }

    private static Command BuildAddConceptSchemeCommand()
    {
        var cmd = new Command("conceptscheme", "Add concept scheme");

        var term = RequiredTermOption();
        var uri = OptionalUriOption();

        cmd.Add(term);
        cmd.Add(uri);

        cmd.SetAction(context =>
        {
            var t = context.GetValue(term)!;
            var u = context.GetValue(uri);

            RunSchemaCommand(api =>
            {
                ValidateOptionalUri(api, t, u);
                api.CreateConceptScheme(t);
            }, "add conceptscheme");
        });

        return cmd;
    }

    private static Command BuildAddContextCommand()
    {
        var cmd = new Command("context", "Add context");

        var term = RequiredTermOption();
        var uri = OptionalUriOption();

        var field = new Option<string?>("--field");
        var objectOption = new Option<string?>("--object");

        cmd.Add(term);
        cmd.Add(uri);
        cmd.Add(field);
        cmd.Add(objectOption);

        cmd.SetAction((Action<ParseResult>)(context =>
        {
            var t = context.GetValue(term)!;
            var u = context.GetValue(uri);

            var f = NormalizeAtObject(context.GetValue(field));
            var v = NormalizeAtObject(context.GetValue(objectOption));

            RunSchemaCommand((Action<SchemaApi>)(api =>
            {
                if (!string.IsNullOrWhiteSpace(u))
                {
                    api.CreateNamespace(t, u);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(f) &&
                    !string.IsNullOrWhiteSpace(v))
                {
                    api.AddContextTermField(t, f, v);
                    return;
                }

                throw new InvalidOperationException(
                    "Use --uri OR both --field and --object.");
            }), "add context");
        }));

        return cmd;
    }

    private static Command BuildAddTripleCommand()
    {
        var cmd = new Command("triple", "Add an RDF triple");

        var subject = RequiredSubjectOption();
        var predicate = RequiredPredicateOption();
        var objectOption = RequiredObjectOption();

        cmd.Add(subject);
        cmd.Add(predicate);
        cmd.Add(objectOption);

        cmd.SetAction(context =>
        {
            var s = context.GetValue(subject)!;
            var p = context.GetValue(predicate)!;
            var v = context.GetValue(objectOption)!;

            RunSchemaCommand(api => { api.AddTriple(s, p, v); }, "add triple");
        });

        return cmd;
    }

    private static Command BuildUpdateCommand()
    {
        var cmd = new Command("update", "Update schema items");

        cmd.Add(BuildUpdateContextCommand());

        return cmd;
    }

    private static Command BuildUpdateContextCommand()
    {
        var cmd = new Command("context", "Update context");

        var term = RequiredTermOption();

        var uri = OptionalUriOption();
        var field = new Option<string?>("--field");
        var objectOption = new Option<string?>("--object");

        cmd.Add(term);
        cmd.Add(uri);
        cmd.Add(field);
        cmd.Add(objectOption);

        cmd.SetAction(context =>
        {
            var t = context.GetValue(term)!;
            var u = context.GetValue(uri);

            var f = NormalizeAtObject(context.GetValue(field));
            var v = NormalizeAtObject(context.GetValue(objectOption));

            RunSchemaCommand(api =>
            {
                if (!string.IsNullOrWhiteSpace(u))
                {
                    api.UpdateContextTerm(t, u);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(f) &&
                    !string.IsNullOrWhiteSpace(v))
                {
                    api.SetContextField(t, f, v);
                    return;
                }

                throw new InvalidOperationException(
                    "Use --uri OR both --field and --object.");
            }, "update context");
        });

        return cmd;
    }

    private static Command BuildRemoveCommand()
    {
        var cmd = new Command("remove", "Remove schema items");

        cmd.Add(BuildRemoveClassCommand());
        cmd.Add(BuildRemoveFieldCommand());
        cmd.Add(BuildRemoveConceptCommand());
        cmd.Add(BuildRemoveConceptSchemeCommand());
        cmd.Add(BuildRemoveContextCommand());
        cmd.Add(BuildRemoveTripleCommand());

        return cmd;
    }

    private static Command BuildRemoveClassCommand()
    {
        var cmd = new Command("class", "Remove class");

        var term = RequiredTermOption();

        cmd.Add(term);

        cmd.SetAction(context =>
        {
            var t = context.GetValue(term)!;

            RunSchemaCommand(api => { api.DeleteClass(t); }, "remove class");
        });

        return cmd;
    }

    private static Command BuildRemoveFieldCommand()
    {
        var cmd = new Command("field", "Remove field");

        var subject = RequiredSubjectOption();
        var predicate = RequiredPredicateOption();

        cmd.Add(subject);
        cmd.Add(predicate);

        cmd.SetAction(context =>
        {
            var s = context.GetValue(subject)!;
            var p = context.GetValue(predicate)!;

            RunSchemaCommand(api => { api.RemoveField(s, p); }, "remove field");
        });

        return cmd;
    }

    private static Command BuildRemoveConceptCommand()
    {
        var cmd = new Command("concept", "Remove concept");

        var term = RequiredTermOption();

        cmd.Add(term);

        cmd.SetAction(context =>
        {
            var t = context.GetValue(term)!;

            RunSchemaCommand(api => { api.DeleteConcept(t); }, "remove concept");
        });

        return cmd;
    }

    private static Command BuildRemoveConceptSchemeCommand()
    {
        var cmd = new Command("conceptscheme", "Remove concept scheme");

        var term = RequiredTermOption();

        cmd.Add(term);

        cmd.SetAction(context =>
        {
            var t = context.GetValue(term)!;

            RunSchemaCommand(api => { api.DeleteConceptScheme(t); }, "remove conceptscheme");
        });

        return cmd;
    }

    private static Command BuildRemoveContextCommand()
    {
        var cmd = new Command("context", "Remove context");

        var term = RequiredTermOption();

        var field = new Option<string?>("--field");

        cmd.Add(term);
        cmd.Add(field);

        cmd.SetAction(context =>
        {
            var t = context.GetValue(term)!;
            var f = context.GetValue(field);

            RunSchemaCommand(api =>
            {
                if (string.IsNullOrWhiteSpace(f))
                    api.RemoveContext(t);
                else
                    api.RemoveContextField(t, f);
            }, "remove context");
        });

        return cmd;
    }

    private static Command BuildRemoveTripleCommand()
    {
        var cmd = new Command("triple", "Remove an RDF triple");

        var subject = RequiredSubjectOption();
        var predicate = RequiredPredicateOption();
        var objectOption = RequiredObjectOption();

        cmd.Add(subject);
        cmd.Add(predicate);
        cmd.Add(objectOption);

        cmd.SetAction(context =>
        {
            var s = context.GetValue(subject)!;
            var p = context.GetValue(predicate)!;
            var v = context.GetValue(objectOption)!;

            RunSchemaCommand(api => { api.RemoveTriple(s, p, v); }, "remove triple");
        });

        return cmd;
    }

    private static Command BuildSetCommand()
    {
        var cmd = new Command("set", "Set schema triples and properties");

        cmd.Add(BuildSetTripleCommand());
        cmd.Add(BuildSetLanguagePropertyCommand());

        return cmd;
    }

    private static Command BuildSetTripleCommand()
    {
        var cmd = new Command("triple", "Set an RDF triple");

        var subject = RequiredSubjectOption();
        var predicate = RequiredPredicateOption();
        var objectOption = RequiredObjectOption();

        cmd.Add(subject);
        cmd.Add(predicate);
        cmd.Add(objectOption);

        cmd.SetAction(context =>
        {
            var s = context.GetValue(subject)!;
            var p = context.GetValue(predicate)!;
            var v = context.GetValue(objectOption)!;

            RunSchemaCommand(api => { api.SetTriple(s, p, v); }, "set triple");
        });

        return cmd;
    }

    private static Command BuildSetLanguagePropertyCommand()
    {
        var cmd = new Command("language-property", "Set language property");

        var subject = RequiredSubjectOption();
        var predicate = RequiredPredicateOption();

        var language = new Option<string>("--language")
        {
            Required = true
        };

        var objectOption = RequiredObjectOption();

        cmd.Add(subject);
        cmd.Add(predicate);
        cmd.Add(language);
        cmd.Add(objectOption);

        cmd.SetAction(context =>
        {
            var s = context.GetValue(subject)!;
            var p = context.GetValue(predicate)!;
            var l = context.GetValue(language)!;
            var v = context.GetValue(objectOption)!;

            RunSchemaCommand(api => { api.SetLanguageProperty(s, p, l, v); }, "set language property");
        });

        return cmd;
    }

    private static Command BuildSplitCommand()
    {
        var cmd = new Command(
            "split",
            "Split the selected schema's merged JSON-LD file into Split folders");

        cmd.SetAction(_ =>
        {
            var folder = GetOriginalSchemaPath();
            var api = new SchemaApi();
            api.SplitMergedSchema(folder);
            Console.WriteLine($"Split merged schema into: {Path.Combine(folder, "Split")}");
        });

        return cmd;
    }

    private static Command BuildMergeCommand()
    {
        var cmd = new Command(
            "merge",
            "Merge the selected schema's Split folders into one JSON-LD file");

        cmd.SetAction(_ =>
        {
            var folder = GetOriginalSchemaPath();
            var api = new SchemaApi();
            api.MergeSplitSchema(folder);
            Console.WriteLine($"Merged split schema under: {Path.Combine(folder, "Merged")}");
        });

        return cmd;
    }

    private static Command BuildSparqlCommand()
    {
        var cmd = new Command("sparql", "Apply a SPARQL 1.1 Update to the selected schema");
        var file = new Option<string?>("--file")
        {
            Description = "Path to a file containing SPARQL Update commands."
        };
        var update = new Option<string?>("--update")
        {
            Description = "Inline SPARQL Update text."
        };

        cmd.Add(file);
        cmd.Add(update);
        cmd.SetAction(context =>
        {
            var filePath = context.GetValue(file);
            var inlineUpdate = context.GetValue(update);

            if (string.IsNullOrWhiteSpace(filePath) == string.IsNullOrWhiteSpace(inlineUpdate))
                throw new InvalidOperationException("Specify exactly one of --file or --update.");

            var sparql = !string.IsNullOrWhiteSpace(filePath)
                ? File.ReadAllText(filePath)
                : inlineUpdate!;

            RunSchemaCommand(api => api.ApplySparqlUpdate(sparql), "apply SPARQL update");
        });

        return cmd;
    }

    private static Command BuildRdfCommand()
    {
        var cmd = new Command("rdf", "Convert the selected schema between JSON-LD and Turtle");
        cmd.Add(BuildExportTurtleCommand());
        cmd.Add(BuildImportTurtleCommand());
        return cmd;
    }

    private static Command BuildExportTurtleCommand()
    {
        var cmd = new Command("export-turtle", "Export the selected JSON-LD schema as Turtle");
        var output = new Option<string>("--output")
        {
            Required = true
        };
        cmd.Add(output);

        cmd.SetAction(context =>
        {
            var outputPath = context.GetValue(output)!;
            var folder = GetLatestSchemaFolder(GetOriginalSchemaPath());
            var api = new SchemaApi();
            api.LoadFromFolder(folder);
            api.ExportTurtle(outputPath);
            Console.WriteLine($"Turtle written to: {Path.GetFullPath(outputPath)}");
        });

        return cmd;
    }

    private static Command BuildImportTurtleCommand()
    {
        var cmd = new Command("import-turtle", "Replace the selected schema graph with RDF from Turtle");
        var input = new Option<string>("--input")
        {
            Required = true
        };
        cmd.Add(input);

        cmd.SetAction(context =>
        {
            var inputPath = context.GetValue(input)!;
            RunSchemaCommand(api => api.ImportTurtle(inputPath), "import Turtle");
        });

        return cmd;
    }

    private static Command BuildValidateCommand()
    {
        var cmd = new Command("validate", "Validate schema");

        var shapes = new Option<string>("--shapes")
        {
            Required = true
        };

        cmd.Add(shapes);

        cmd.SetAction(context =>
        {
            var path = context.GetValue(shapes)!;

            var folder = GetLatestSchemaFolder(GetOriginalSchemaPath());

            var api = new SchemaApi();

            api.LoadFromFolder(folder);

            var (ok, report) = api.ValidateWithShacl(path);

            Console.WriteLine(ok ? $"Schema '{folder}' is Valid" : "Invalid");
            Console.WriteLine(report);
        });

        return cmd;
    }

    private static readonly string[] SupportedSchemaNames = ["ctdl", "ctdlasn", "qdata"];

    private static string ResolveSchemaPath(string path, string? schemaName)
    {
        var fullPath = Path.GetFullPath(path);

        if (string.IsNullOrWhiteSpace(schemaName))
        {
            if (Directory.Exists(Path.Combine(fullPath, "Split")))
                return fullPath;

            var defaultSchemaPath = Path.Combine(fullPath, "ctdl");
            if (Directory.Exists(defaultSchemaPath))
                return defaultSchemaPath;

            return fullPath;
        }

        var normalizedSchemaName = schemaName.ToLowerInvariant();
        var selectedPath = Path.Combine(fullPath, normalizedSchemaName);
        if (!Directory.Exists(selectedPath))
            throw new DirectoryNotFoundException(selectedPath);

        EnsureSchemaStructure(selectedPath, normalizedSchemaName);
        return selectedPath;
    }

    private static void EnsureSchemaStructure(string schemaPath, string schemaName)
    {
        var (contextFileName, mergedFileName, contextReference) = schemaName switch
        {
            "ctdlasn" => ("ctdlasn-context.jsonld", "ctdlasn-schema.jsonld", "https://credreg.net/ctdlasn/schema/context/json"),
            "qdata" => ("qdata-context.jsonld", "qdata-schema.jsonld", "https://credreg.net/qdata/schema/context/json"),
            _ => ("ctdl-context.jsonld", "ctdl-schema.jsonld", "https://credreg.net/ctdl/schema/context/json")
        };

        Directory.CreateDirectory(Path.Combine(schemaPath, "Split", "classes"));
        Directory.CreateDirectory(Path.Combine(schemaPath, "Split", "properties"));
        Directory.CreateDirectory(Path.Combine(schemaPath, "Split", "concepts"));
        Directory.CreateDirectory(Path.Combine(schemaPath, "Split", "conceptschemes"));
        Directory.CreateDirectory(Path.Combine(schemaPath, "Merged"));

        var contextPath = Path.Combine(schemaPath, contextFileName);
        if (!File.Exists(contextPath))
            File.WriteAllText(contextPath, "{\n    \"@context\": {}\n}\n");

        var mergedPath = Path.Combine(schemaPath, "Merged", mergedFileName);
        if (!File.Exists(mergedPath))
        {
            File.WriteAllText(
                mergedPath,
                $"{{\n    \"@context\": \"{contextReference}\",\n    \"@graph\": []\n}}\n");
        }
    }

    private static Option<string> RequiredTermOption()
    {
        return new Option<string>("--term")
        {
            Required = true
        };
    }

    private static Option<string?> OptionalUriOption()
    {
        return new Option<string?>("--uri");
    }

    private static Option<string> RequiredSubjectOption()
    {
        return new Option<string>("--subject")
        {
            Required = true
        };
    }

    private static Option<string> RequiredPredicateOption()
    {
        return new Option<string>("--predicate")
        {
            Required = true
        };
    }

    private static Option<string> RequiredObjectOption()
    {
        return new Option<string>("--object")
        {
            Required = true
        };
    }

    private static void RunSchemaCommand(
        Action<SchemaApi> command,
        string operation)
    {
        RunCommand(
            GetOriginalSchemaPath(),
            command,
            operation,
            GetCheckpointLimit());
    }

    private static string GetOriginalSchemaPath()
    {
        var original = Environment.GetEnvironmentVariable("SCHEMA_ORIGINAL");

        if (string.IsNullOrWhiteSpace(original))
            throw new InvalidOperationException(
                "Schema not initialized. Run: schema init --path <path>");

        return original;
    }

    private static void ApplyLatestCheckpoint(string originalFolder)
    {
        var latest = GetLatestSchemaFolder(originalFolder);

        if (Path.GetFullPath(latest)
            .Equals(
                Path.GetFullPath(originalFolder),
                StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Original schema is already current.");
            return;
        }

        if (Directory.Exists(originalFolder))
            Directory.Delete(originalFolder, true);

        Directory.Move(latest, originalFolder);

        Console.WriteLine(
            $"Applied checkpoint '{Path.GetFileName(latest)}' to original schema.");
    }

    private static void ValidateOptionalUri(
        SchemaApi api,
        string term,
        string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return;

        var expected = api.GetUriOrId(term);

        if (expected != uri)
            throw new InvalidOperationException(
                $"URI does not match context mapping for term '{term}'.");
    }

    private static string? NormalizeAtObject(string? objectValue)
    {
        if (string.IsNullOrWhiteSpace(objectValue))
            return objectValue;

        return objectValue.StartsWith("@@")
            ? "@" + objectValue[2..]
            : objectValue;
    }

    private static void WriteEnvFile(string schemaPath, int? checkpoints)
    {
        var envPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            ".env");

        var lines = new List<string>
        {
            $"SCHEMA_ORIGINAL={schemaPath}"
        };

        if (checkpoints.HasValue)
            lines.Add($"SCHEMA_CHECKPOINTS={checkpoints.Value}");

        File.WriteAllLines(envPath, lines);
    }

    private static int? GetCheckpointLimit()
    {
        var checkpointValue = Environment.GetEnvironmentVariable("SCHEMA_CHECKPOINTS");

        if (int.TryParse(checkpointValue, out var result))
            return result;

        return null;
    }

    private static void RunClone(string originalFolder)
    {
        var input = GetLatestSchemaFolder(originalFolder);

        var parent = Directory.GetParent(originalFolder)!.FullName;

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff");

        var checkpointPrefix = GetCheckpointPrefix(originalFolder);
        var output = Path.Combine(parent, $"{checkpointPrefix}-{timestamp}");

        CopyDirectory(input, output);

        WriteVersionFile(
            output,
            timestamp,
            Path.GetFileName(input),
            "clone");

        Console.WriteLine($"Cloned schema to: {output}");
    }

    private static void RunCommand(
        string originalFolder,
        Action<SchemaApi> command,
        string operationName,
        int? checkpointLimit)
    {
        var input = GetLatestSchemaFolder(originalFolder);

        var parent = Directory.GetParent(originalFolder)!.FullName;

        var api = new SchemaApi();

        api.LoadFromFolder(input);

        command(api);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff");

        var checkpointPrefix = GetCheckpointPrefix(originalFolder);
        var output = Path.Combine(parent, $"{checkpointPrefix}-{timestamp}");

        api.SaveFolder(output);

        TrimCheckpoints( parent, checkpointLimit);

        WriteVersionFile(
            output,
            timestamp,
            Path.GetFileName(input),
            operationName);

        Console.WriteLine($"New version created: {output}");
    }

    private static void TrimCheckpoints(
        string parent,
        int? checkpointLimit)
    {
        if (!checkpointLimit.HasValue)
            return;

        var checkpointPrefix = GetCheckpointPrefix(GetOriginalSchemaPath());
        var checkpoints = Directory
            .GetDirectories(parent, $"{checkpointPrefix}-*")
            .OrderByDescending(Directory.GetCreationTimeUtc)
            .ToList();

        foreach (var old in checkpoints.Skip(checkpointLimit.Value))
            Directory.Delete(old, true);
    }

    private static void WriteVersionFile(
        string output,
        string timestamp,
        string basedOn,
        string operation)
    {
        File.WriteAllText(
            Path.Combine(output, "_version.json"),
            JsonSerializer.Serialize(
                new
                {
                    timestamp,
                    basedOn,
                    operation
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    IndentSize = 4,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    NewLine = "\n"
                }));
    }

    private static void CopyDirectory(
        string sourceDir,
        string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(
                destDir,
                Path.GetFileName(file));

            File.Copy(file, destFile, true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(
                destDir,
                Path.GetFileName(dir));

            CopyDirectory(dir, destSubDir);
        }
    }

    private static string GetCheckpointPrefix(string original)
    {
        return Path.GetFileName(Path.TrimEndingDirectorySeparator(original));
    }

    private static string GetLatestSchemaFolder(string original)
    {
        var parent = Directory.GetParent(original)!.FullName;

        var checkpointPrefix = GetCheckpointPrefix(original);
        var candidates = Directory
            .GetDirectories(parent, $"{checkpointPrefix}-*")
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates.FirstOrDefault() ?? original;
    }

    private static void LoadEnv()
    {
        var envPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            ".env");

        if (!File.Exists(envPath))
            return;

        foreach (var line in File.ReadAllLines(envPath))
        {
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("#"))
                continue;

            var parts = line.Split('=', 2);

            if (parts.Length != 2)
                continue;

            Environment.SetEnvironmentVariable(
                parts[0],
                parts[1]);
        }
    }
}