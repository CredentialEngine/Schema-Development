using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CLI.IntegrationTests;

[TestClass]
public class SchemaCliDiffTests
{
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
        _actualOutput = Path.Combine(_testRoot, "ActualOutput");

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
        if (Directory.Exists(_actualOutput))
            Directory.Delete(_actualOutput, true);

        Directory.CreateDirectory(_actualOutput);

        _workDir = Path.Combine(
            Path.GetTempPath(),
            "schema-cli-diff-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_workDir);

        _seedSchema = Path.Combine(_workDir, "Schema");

        CreateSeedSchema(_seedSchema);
    }

    [TestMethod]
    public void CliCommands_ProduceExpectedSchemaOutput()
    {
        var scriptPath = Path.Combine("TestScripts", "test-script.txt");

        var consoleOutput = RunScriptAndCaptureOutputs(scriptPath);

        File.WriteAllText(
            Path.Combine(_actualOutput, "console-output.txt"),
            NormalizeConsoleOutput(consoleOutput, _workDir));

        AssertDirectoriesEqual(_expectedOutput, _actualOutput);
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

            line = line.Replace("{SeedSchema}", _seedSchema);

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

        // init does not create a Schema-* output folder.
        // Snapshot the initialized seed schema.
        if (args[0].Equals("init", StringComparison.OrdinalIgnoreCase))
            return _seedSchema;

        // clone/add/update/remove/set commands create a new Schema-* folder.
        return GetLatestSchemaFolder();
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
            @"Schema-\d{8}_\d{6}_\d{7}",
            "Schema-{Timestamp}");

        return text;
    }

    private string GetLatestSchemaFolder()
    {
        var candidates = Directory.GetDirectories(_workDir, "Schema-*")
            .OrderByDescending(x => x)
            .ToList();

        Assert.IsNotEmpty(candidates, "No generated Schema-* folder found.");

        return candidates[0];
    }

    private static void CreateSeedSchema(string schemaRoot)
    {
        Directory.CreateDirectory(Path.Combine(schemaRoot, "Split", "classes"));
        Directory.CreateDirectory(Path.Combine(schemaRoot, "Split", "properties"));
        Directory.CreateDirectory(Path.Combine(schemaRoot, "Merged"));

        File.WriteAllText(Path.Combine(schemaRoot, "ctdl-context.jsonld"), """
                                                                           {
                                                                             "@context": {
                                                                               "ceterms": "https://purl.org/ctdl/terms/",
                                                                               "rdf": "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
                                                                               "rdfs": "http://www.w3.org/2000/01/rdf-schema#",
                                                                               "schema": "https://schema.org/"
                                                                             }
                                                                           }
                                                                           """);

        File.WriteAllText(Path.Combine(schemaRoot, "Merged", "ctdl-schema.jsonld"), """
            {
                "@context": "https://credreg.net/ctdl/schema/context/json",
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