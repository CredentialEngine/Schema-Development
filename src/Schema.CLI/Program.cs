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
        root.Add(BuildAddCommand());
        root.Add(BuildUpdateCommand());
        root.Add(BuildRemoveCommand());
        root.Add(BuildSetCommand());
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

        cmd.SetAction(context =>
        {
            var p = context.GetValue(path)
                    ?? throw new InvalidOperationException("Path cannot be null.");

            if (!Directory.Exists(p))
                throw new DirectoryNotFoundException(p);

            WriteEnvFile(p);

            Console.WriteLine($"Initialized schema at: {p}");
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

    private static Command BuildAddCommand()
    {
        var cmd = new Command("add", "Add schema items");

        cmd.Add(BuildAddClassCommand());
        cmd.Add(BuildAddPropertyCommand());
        cmd.Add(BuildAddConceptCommand());
        cmd.Add(BuildAddConceptSchemeCommand());
        cmd.Add(BuildAddContextCommand());
        cmd.Add(BuildAddValueCommand());

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
        var value = new Option<string?>("--value");

        cmd.Add(term);
        cmd.Add(uri);
        cmd.Add(field);
        cmd.Add(value);

        cmd.SetAction(context =>
        {
            var t = context.GetValue(term)!;
            var u = context.GetValue(uri);

            var f = NormalizeAtValue(context.GetValue(field));
            var v = NormalizeAtValue(context.GetValue(value));

            RunSchemaCommand(api =>
            {
                if (!string.IsNullOrWhiteSpace(u))
                {
                    api.AddContextTerm(t, u);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(f) &&
                    !string.IsNullOrWhiteSpace(v))
                {
                    api.AddContextTermField(t, f, v);
                    return;
                }

                throw new InvalidOperationException(
                    "Use --uri OR both --field and --value.");
            }, "add context");
        });

        return cmd;
    }

    private static Command BuildAddValueCommand()
    {
        var cmd = new Command("value", "Add value to field");

        var subject = RequiredSubjectOption();
        var predicate = RequiredPredicateOption();
        var value = RequiredValueOption();

        cmd.Add(subject);
        cmd.Add(predicate);
        cmd.Add(value);

        cmd.SetAction(context =>
        {
            var s = context.GetValue(subject)!;
            var p = context.GetValue(predicate)!;
            var v = context.GetValue(value)!;

            RunSchemaCommand(api => { api.AddFieldValue(s, p, v); }, "add value");
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
        var value = new Option<string?>("--value");

        cmd.Add(term);
        cmd.Add(uri);
        cmd.Add(field);
        cmd.Add(value);

        cmd.SetAction(context =>
        {
            var t = context.GetValue(term)!;
            var u = context.GetValue(uri);

            var f = NormalizeAtValue(context.GetValue(field));
            var v = NormalizeAtValue(context.GetValue(value));

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
                    "Use --uri OR both --field and --value.");
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
        cmd.Add(BuildRemoveValueCommand());

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

    private static Command BuildRemoveValueCommand()
    {
        var cmd = new Command("value", "Remove value from field");

        var subject = RequiredSubjectOption();
        var predicate = RequiredPredicateOption();
        var value = RequiredValueOption();

        cmd.Add(subject);
        cmd.Add(predicate);
        cmd.Add(value);

        cmd.SetAction(context =>
        {
            var s = context.GetValue(subject)!;
            var p = context.GetValue(predicate)!;
            var v = context.GetValue(value)!;

            RunSchemaCommand(api => { api.RemoveFieldValue(s, p, v); }, "remove value");
        });

        return cmd;
    }

    private static Command BuildSetCommand()
    {
        var cmd = new Command("set", "Set values");

        cmd.Add(BuildSetFieldCommand());
        cmd.Add(BuildSetLanguagePropertyCommand());

        return cmd;
    }

    private static Command BuildSetFieldCommand()
    {
        var cmd = new Command("field", "Set field");

        var subject = RequiredSubjectOption();
        var predicate = RequiredPredicateOption();
        var value = RequiredValueOption();

        cmd.Add(subject);
        cmd.Add(predicate);
        cmd.Add(value);

        cmd.SetAction(context =>
        {
            var s = context.GetValue(subject)!;
            var p = context.GetValue(predicate)!;
            var v = context.GetValue(value)!;

            RunSchemaCommand(api => { api.SetField(s, p, v); }, "set field");
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

        var value = RequiredValueOption();

        cmd.Add(subject);
        cmd.Add(predicate);
        cmd.Add(language);
        cmd.Add(value);

        cmd.SetAction(context =>
        {
            var s = context.GetValue(subject)!;
            var p = context.GetValue(predicate)!;
            var l = context.GetValue(language)!;
            var v = context.GetValue(value)!;

            RunSchemaCommand(api => { api.SetLanguageProperty(s, p, l, v); }, "set language property");
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

            api.LoadFromFolder(Path.Combine(folder, "Split"));

            var (ok, report) = api.ValidateWithShacl(path);

            Console.WriteLine(ok ? $"Schema '{folder}' is Valid" : "Invalid");
            Console.WriteLine(report);
        });

        return cmd;
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

    private static Option<string> RequiredValueOption()
    {
        return new Option<string>("--value")
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
            operation);
    }

    private static string GetOriginalSchemaPath()
    {
        var original = Environment.GetEnvironmentVariable("SCHEMA_ORIGINAL");

        if (string.IsNullOrWhiteSpace(original))
            throw new InvalidOperationException(
                "Schema not initialized. Run: schema init --path <path>");

        return original;
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

    private static string? NormalizeAtValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        return value.StartsWith("@@")
            ? "@" + value[2..]
            : value;
    }

    private static void WriteEnvFile(string schemaPath)
    {
        var envPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            ".env");

        File.WriteAllText(
            envPath,
            $"SCHEMA_ORIGINAL={schemaPath}");
    }

    private static void RunClone(string originalFolder)
    {
        var input = GetLatestSchemaFolder(originalFolder);

        var parent = Directory.GetParent(originalFolder)!.FullName;

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff");

        var output = Path.Combine(parent, $"Schema-{timestamp}");

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
        string operationName)
    {
        var input = GetLatestSchemaFolder(originalFolder);

        var parent = Directory.GetParent(originalFolder)!.FullName;

        var api = new SchemaApi();

        api.LoadFromFolder(input);

        command(api);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff");

        var output = Path.Combine(parent, $"Schema-{timestamp}");

        api.SaveFolder(output);

        WriteVersionFile(
            output,
            timestamp,
            Path.GetFileName(input),
            operationName);

        Console.WriteLine($"New version created: {output}");
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

    private static string GetLatestSchemaFolder(string original)
    {
        var parent = Directory.GetParent(original)!.FullName;

        var candidates = Directory
            .GetDirectories(parent, "Schema-*")
            .OrderByDescending(d => d)
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