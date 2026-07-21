using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Schema.SDK;
using VDS.RDF;

namespace CLI.IntegrationTests;

[TestClass]
public class SchemaCliDiffTests
{
    public TestContext TestContext { get; set; } = null!;

    private readonly string _actualOutput;
    private readonly string _cliDll;
    private readonly string _expectedOutput;
    private readonly string _testRoot;
    private string _seedSchema;

    private string _workDir;

    public SchemaCliDiffTests()
    {
        _testRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        _expectedOutput = Path.Combine(_testRoot, "ExpectedOutput");
        _actualOutput = Path.Combine(
            _testRoot,
            "ActualOutput",
            DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff") + Guid.NewGuid().ToString("N"));

        var configuration =
            Environment.GetEnvironmentVariable("BUILD_CONFIGURATION")
            ?? "Debug";
        _cliDll = Path.GetFullPath(
            Path.Combine(_testRoot, "..", "Schema.CLI", "bin", "x64", configuration, "net10.0", "Schema.CLI.dll"));

        _workDir = string.Empty;
        _seedSchema = string.Empty;
    }

    [TestInitialize]
    public void Setup()
    {
        Directory.CreateDirectory(_actualOutput);

        _workDir = Path.Combine(
            Path.GetTempPath(),
            "schema-cli-diff-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_workDir);

        _seedSchema = Path.Combine(_workDir, "Schema");

        CreateSeedSchema(_seedSchema);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, true);
    }

    [TestMethod]
    public void CtdlCommands_ProduceExpectedSchemaOutput()
    {
        var scriptPath = Path.Combine(
            _testRoot,
            "TestScripts",
            "ctdl-test-script.txt");

        var consoleOutput = RunScriptAndCaptureOutputs(scriptPath);

        File.WriteAllText(
            Path.Combine(_actualOutput, "console-output.txt"),
            NormalizeConsoleOutput(consoleOutput, _workDir));

        AssertDirectoriesEqual(_expectedOutput, _actualOutput);
    }

    [TestMethod]
    public void CheckpointLimit_TrimsOldSnapshots()
    {
        var scriptPath = Path.Combine(
            _testRoot,
            "TestScripts",
            "checkpoint-trim.txt");

        RunScriptAndCaptureOutputs(scriptPath);

        var checkpoints = Directory
            .GetDirectories(_seedSchema, "ctdl-*")
            .OrderBy(x => x)
            .ToList();
        Assert.HasCount(2, checkpoints);
    }

    [TestMethod]
    public void Apply_ReplacesOriginalWithLatestCheckpoint()
    {
        var scriptPath = Path.Combine(
            _testRoot,
            "TestScripts",
            "checkpoint-apply.txt");

        RunScriptAndCaptureOutputs(scriptPath);

        // Latest checkpoint should no longer exist because it was moved
        var checkpoints = Directory
            .GetDirectories(_seedSchema, "ctdl-*")
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.HasCount(1, checkpoints);

        // Original schema should now contain the applied changes
        var classFile = Path.Combine(
            _seedSchema,
            "ctdl",
            "Split",
            "classes",
            "ex_TestClass.jsonld");

        Assert.IsTrue(File.Exists(classFile));

        var json = File.ReadAllText(classFile);

        StringAssert.Contains(json, "\"@id\": \"ex:TestClass\"");
    }

    [TestMethod]
    [DataRow("ctdlasn", "ceasn", "https://purl.org/ctdlasn/terms/", "ceasn_TestClass.jsonld")]
    [DataRow("qdata", "qdata", "https://credreg.net/qdata/terms/", "qdata_TestClass.jsonld")]
    public void AdditionalSchemaScripts_ManipulateSelectedSchema(
        string schemaName,
        string prefix,
        string namespaceUri,
        string expectedClassFile)
    {
        var scriptPath = Path.Combine(
            _testRoot,
            "TestScripts",
            $"{schemaName}-test-script.txt");

        RunScriptAndCaptureOutputs(scriptPath);

        var latest = GetLatestSchemaFolder(schemaName);
        var classFile = Path.Combine(latest, "Split", "classes", expectedClassFile);

        Assert.IsTrue(File.Exists(classFile));
        Assert.IsTrue(File.Exists(Path.Combine(latest, $"{schemaName}-context.jsonld")));
        Assert.IsTrue(File.Exists(Path.Combine(latest, "Merged", $"{schemaName}-schema.jsonld")));

        var context = File.ReadAllText(Path.Combine(latest, $"{schemaName}-context.jsonld"));
        StringAssert.Contains(context, namespaceUri);
        StringAssert.Contains(File.ReadAllText(classFile), $"\"@id\": \"{prefix}:TestClass\"");
    }

    [TestMethod]
    [DataRow("ctdl", "ctdl", "https://credreg.net/ctdl/schema/context/json", "ceterms:CliExample")]
    [DataRow("ctdlasn", "ctdlasn", "https://credreg.net/ctdlasn/schema/context/json", "ceasn:CliExample")]
    [DataRow("qdata", "qdata", "https://credreg.net/qdata/schema/context/json", "qdata:CliExample")]
    public void SplitAndMergeCommands_WorkForSelectedSchema(
        string schemaName,
        string filePrefix,
        string contextReference,
        string term)
    {
        var init = RunCli("init", "--path", _seedSchema, "--schema", schemaName);
        Assert.AreEqual(0, init.ExitCode, init.StdErr);

        var schemaFolder = Path.Combine(_seedSchema, schemaName);
        var mergedPath = Path.Combine(schemaFolder, "Merged", $"{filePrefix}-schema.jsonld");
        File.WriteAllText(mergedPath, $$"""
            {
                "@context": "{{contextReference}}",
                "@graph": [
                    {
                        "@id": "{{term}}",
                        "@type": "rdfs:Class"
                    }
                ]
            }
            """);

        var original = JsonNode.Parse(File.ReadAllText(mergedPath));

        var split = RunCli("split");
        Assert.AreEqual(0, split.ExitCode, split.StdErr);
        Assert.IsTrue(File.Exists(Path.Combine(
            schemaFolder,
            "Split",
            "classes",
            term.Replace(":", "_") + ".jsonld")));

        File.Delete(mergedPath);

        var merge = RunCli("merge");
        Assert.AreEqual(0, merge.ExitCode, merge.StdErr);

        var merged = JsonNode.Parse(File.ReadAllText(mergedPath));
        Assert.IsTrue(JsonNode.DeepEquals(original, merged));
    }

    [TestMethod]
    public void RdfAndSparqlCommands_RoundTripAndUpdateSelectedSchema()
    {
        var init = RunCli("init", "--path", _seedSchema, "--schema", "ctdl");
        Assert.AreEqual(0, init.ExitCode, init.StdErr);

        var addNamespace = RunCli("add", "namespace", "--prefix", "ex", "--uri", "https://example.org/");
        Assert.AreEqual(0, addNamespace.ExitCode, addNamespace.StdErr);

        var turtlePath = Path.Combine(_workDir, "ctdl.ttl");
        var export = RunCli("rdf", "export-turtle", "--output", turtlePath);
        Assert.AreEqual(0, export.ExitCode, export.StdErr);
        Assert.IsTrue(File.Exists(turtlePath));

        File.AppendAllText(turtlePath, "\n<https://example.org/FromTurtle> <http://www.w3.org/2000/01/rdf-schema#label> \"From Turtle\"@en .\n");
        var import = RunCli("rdf", "import-turtle", "--input", turtlePath);
        Assert.AreEqual(0, import.ExitCode, import.StdErr);

        var updatePath = Path.Combine(_workDir, "update.rq");
        File.WriteAllText(updatePath, "INSERT DATA { <https://example.org/FromSparql> <http://www.w3.org/2000/01/rdf-schema#label> \"From SPARQL\"@en . }");
        var update = RunCli("sparql", "--file", updatePath);
        Assert.AreEqual(0, update.ExitCode, update.StdErr);

        var latest = GetLatestSchemaFolder("ctdl");
        var api = new SchemaApi();
        api.LoadFromFolder(latest);
        Assert.IsTrue(api.GetGraph().Triples.Any(t =>
            t.Subject is IUriNode uriNode &&
            uriNode.Uri.AbsoluteUri == "https://example.org/FromTurtle"));
        Assert.IsTrue(api.GetGraph().Triples.Any(t =>
            t.Subject is IUriNode uriNode &&
            uriNode.Uri.AbsoluteUri == "https://example.org/FromSparql"));
        Assert.IsTrue(File.Exists(Path.Combine(latest, "Split", "other", "ex_FromTurtle.jsonld")));
        Assert.IsTrue(File.Exists(Path.Combine(latest, "Split", "other", "ex_FromSparql.jsonld")));
    }

    private CliResult RunCli(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = _workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add(_cliDll);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        process.WaitForExit();

        return new CliResult(process.ExitCode, stdout, stderr);
    }

    private static string[] SplitCommandLine(string commandLine)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            args.Add(current.ToString());

        return args.ToArray();
    }

    private string RunScriptAndCaptureOutputs(string scriptPath)
    {
        var log = new StringBuilder();
        var stepNumber = 0;

        foreach (var rawLine in File.ReadAllLines(scriptPath))
        {
            var line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;

            line = line
                .Replace("{SeedSchema}", _seedSchema)
                .Replace("{TestScripts}", Path.Combine(_testRoot, "TestScripts"));

            log.AppendLine($"> schema {NormalizeConsoleOutput(line, _workDir)}");

            var args = SplitCommandLine(line);
            var result = RunCli(args);

            log.AppendLine(
            NormalizeDynamicValues(result.StdOut, _workDir)
                .ReplaceLineEndings("\n")
                .TrimEnd());

            log.AppendLine(
                NormalizeDynamicValues(result.StdErr, _workDir)
                    .ReplaceLineEndings("\n")
                    .TrimEnd());

            log.AppendLine($"ExitCode: {result.ExitCode}");
            log.AppendLine();

            // Persist the transcript after every command so a failing command still
            // leaves complete diagnostics in ActualOutput/console-output.txt.
            File.WriteAllText(
                Path.Combine(_actualOutput, "console-output.txt"),
                NormalizeConsoleOutput(log.ToString(), _workDir));

            Assert.AreEqual(
                0,
                result.ExitCode,
                $"CLI failed on script line:\n{rawLine}\n\nConsole output:\n{log}");

            var snapshotSource = GetSnapshotSourceFolder(args);

            if (snapshotSource != null)
            {
                stepNumber++;

                var snapshotFolder = Path.Combine(
                    _actualOutput,
                    $"Schema_{stepNumber:000}");

                CopyDirectory(snapshotSource, snapshotFolder);
            }
        }

        return log.ToString();
    }

    private string? GetSnapshotSourceFolder(string[] args)
    {
        if (args.Length == 0)
            return null;

        // init does not create a checkpoint output folder.
        // Snapshot the initialized seed schema.
        if (args[0].Equals("init", StringComparison.OrdinalIgnoreCase))
        {
            var schemaIndex = Array.FindIndex(
                args,
                arg => arg.Equals("--schema", StringComparison.OrdinalIgnoreCase));
            var schemaName = schemaIndex >= 0 && schemaIndex + 1 < args.Length
                ? args[schemaIndex + 1]
                : "ctdl";

            return Path.Combine(_seedSchema, schemaName);
        }

        // clone/add/update/remove/set commands create a schema-specific checkpoint folder.
        return GetLatestSchemaFolder(GetInitializedSchemaName());
    }

    private string GetInitializedSchemaName()
    {
        var envPath = Path.Combine(_workDir, ".env");
        var originalLine = File.ReadLines(envPath)
            .First(line => line.StartsWith("SCHEMA_ORIGINAL=", StringComparison.Ordinal));
        var originalPath = originalLine.Substring("SCHEMA_ORIGINAL=".Length);
        return Path.GetFileName(Path.TrimEndingDirectorySeparator(originalPath));
    }

    private static string NormalizeConsoleOutput(string text, string workDir)
    {
        return NormalizeDynamicValues(text, workDir)
            .ReplaceLineEndings("\n");
    }

    private static string NormalizeDynamicValues(string text, string workDir)
    {
        text = text.Replace("\\", "/");

        text = text.Replace(
            workDir.Replace("\\", "/"),
            "{WorkDir}");

        text = Regex.Replace(
            text,
            @"(ctdl|ctdlasn|qdata)-\d{8}_\d{6}_\d{7}",
            "$1-{Timestamp}");

        return text;
    }

    private string GetLatestSchemaFolder()
    {
        return GetLatestSchemaFolder("ctdl");
    }

    private string GetLatestSchemaFolder(string schemaName)
    {
        var candidates = Directory
            .GetDirectories(_seedSchema, $"{schemaName}-*")
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.IsNotEmpty(candidates, $"No generated {schemaName}-* folder found.");

        return candidates[0];
    }

    private static void CreateSeedSchema(string schemaRoot)
    {
        CreateSeedSchemaFolder(
            Path.Combine(schemaRoot, "ctdl"),
            "ctdl",
            "https://credreg.net/ctdl/schema/context/json");
        CreateSeedSchemaFolder(
            Path.Combine(schemaRoot, "ctdlasn"),
            "ctdlasn",
            "https://credreg.net/ctdlasn/schema/context/json");
        CreateSeedSchemaFolder(
            Path.Combine(schemaRoot, "qdata"),
            "qdata",
            "https://credreg.net/qdata/schema/context/json");
    }

    private static void CreateSeedSchemaFolder(
        string schemaFolder,
        string filePrefix,
        string contextReference)
    {
        Directory.CreateDirectory(Path.Combine(schemaFolder, "Split", "classes"));
        Directory.CreateDirectory(Path.Combine(schemaFolder, "Split", "properties"));
        Directory.CreateDirectory(Path.Combine(schemaFolder, "Merged"));

        File.WriteAllText(Path.Combine(schemaFolder, $"{filePrefix}-context.jsonld"), """
            {
              "@context": {
              }
            }
            """);

        File.WriteAllText(Path.Combine(schemaFolder, "Merged", $"{filePrefix}-schema.jsonld"), $$"""
            {
                "@context": "{{contextReference}}",
                "@graph": []
            }
            """);
    }

    private static void AssertDirectoriesEqual(string expectedDir, string actualDir)
    {
        Assert.IsTrue(
            Directory.Exists(expectedDir),
            $"Expected output folder does not exist yet.\nCopy ActualOutput into:\n{expectedDir}");

        var expectedFiles = GetComparableFiles(expectedDir);
        var actualFiles = GetComparableFiles(actualDir);

        var missingFiles = expectedFiles.Except(actualFiles).OrderBy(x => x).ToList();
        var addedFiles = actualFiles.Except(expectedFiles).OrderBy(x => x).ToList();

        var changedFiles = expectedFiles
            .Intersect(actualFiles)
            .Where(relativePath =>
            {
                var expected = File.ReadAllText(Path.Combine(expectedDir, relativePath)).ReplaceLineEndings("\n");
                var actual = File.ReadAllText(Path.Combine(actualDir, relativePath)).ReplaceLineEndings("\n");

                Assert.AreEqual(expected, actual);
                return expected != actual;
            })
            .OrderBy(x => x)
            .ToList();

        if (missingFiles.Count == 0 && addedFiles.Count == 0 && changedFiles.Count == 0)
            return;

        var message = new StringBuilder();

        message.AppendLine("Snapshot output differs.");
        message.AppendLine();
        message.AppendLine($"Expected: {expectedDir}");
        message.AppendLine($"Actual:   {actualDir}");
        message.AppendLine();

        AppendFileList(message, "Missing files", missingFiles);
        AppendFileList(message, "Added files", addedFiles);
        AppendFileList(message, "Changed files", changedFiles);

        Assert.Fail(message.ToString());
    }

    private static List<string> GetComparableFiles(string root)
    {
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .Where(path => !IsIgnoredSnapshotFile(path))
            .OrderBy(path => path)
            .ToList();
    }

    private static bool IsIgnoredSnapshotFile(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);

        return fileName.Equals("_version.json", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendFileList(StringBuilder message, string title, List<string> files)
    {
        message.AppendLine($"{title}: {files.Count}");

        foreach (var file in files)
            message.AppendLine($"  - {file}");

        message.AppendLine();
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr);
}