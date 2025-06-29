#!/usr/share/dotnet/dotnet run

#:package SharpCompress@0.40.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using SharpCompress.Readers;

//return Program.Main();

class GithubRelease
{
    public GithubReleaseAsset[] assets { get; set; } = [];
}

class GithubReleaseAsset
{
    public string name { get; set; } = string.Empty;
    public string browser_download_url { get; set; } = string.Empty;
}

partial class Program
{
    static int Main()
    {
        var password = GenerateAlphanumericPassword(20);

        StopContainers();

        _ = RunCommand("docker", "ps");

        var containerImageMysql = "mysql";
        var containerImagePostgres = "postgres";
        var containerImageSqlserver = "mcr.microsoft.com/mssql/server";
        var containerImageOpensearch = "opensearchproject/opensearch";

        var curdir = Directory.GetCurrentDirectory();

        var bindmountMysql = $"{curdir}/mysql:/tests";
        var bindmountPostgres = $"{curdir}/postgres:/tests";
        var bindmountSqlserver = $"{curdir}/sqlserver:/tests";

        var containerMysql = GetContainerId(containerImageMysql);
        if (containerMysql != string.Empty)
        {
            Log($"Reusing existing mysql container: {containerMysql}");
        }
        else
        {
            Log($"Starting {containerImageMysql}:");
            containerMysql = StartContainer(containerImageMysql, 3306, bindmountMysql, new() { ["MYSQL_ROOT_PASSWORD"] = password });
        }

        var containerPostgres = GetContainerId(containerImagePostgres);
        if (containerPostgres != string.Empty)
        {
            Log($"Reusing existing postgres container: {containerPostgres}");
        }
        else
        {
            Log($"Starting {containerImagePostgres}:");
            containerPostgres = StartContainer(containerImagePostgres, 5432, bindmountPostgres, new() { ["POSTGRES_PASSWORD"] = password });
        }

        var containerSqlserver = GetContainerId(containerImageSqlserver);
        if (containerSqlserver != string.Empty)
        {
            Log($"Reusing existing sqlserver container: {containerSqlserver}");
        }
        else
        {
            Log($"Starting {containerImageSqlserver}:");
            containerSqlserver = StartContainer(containerImageSqlserver, 1433, bindmountSqlserver, new() { ["ACCEPT_EULA"] = "Y", ["SA_PASSWORD"] = password });
        }

        var containerOpensearch = GetContainerId(containerImageOpensearch);
        if (containerOpensearch != string.Empty)
        {
            Log($"Reusing existing opensearch container: {containerOpensearch}");
        }
        else
        {
            Log($"Starting {containerImageOpensearch}:");
            containerOpensearch = StartContainer(containerImageOpensearch, 9200, string.Empty, new() { ["discovery.type"] = "single-node", ["OPENSEARCH_INITIAL_ADMIN_PASSWORD"] = password });
        }

        _ = DownloadSqlCmd("sqlserver");

        CompileIsatty("mysql");

        Log("Waiting for containers to start up...");
        Thread.Sleep(30000);

        Log("Running containers:");
        _ = RunCommand("docker", "ps");

        Log($"Running mysql script in {containerMysql}:");
        _ = RunCommand("docker", $"exec {containerMysql} /tests/setupmysql.sh");

        Log($"Running postgres script in {containerPostgres}:");
        _ = RunCommand("docker", $"exec {containerPostgres} /tests/setuppostgres.sh");

        Log($"Running sqlserver script in {containerSqlserver}:");
        _ = RunCommand("docker", $"exec {containerSqlserver} /tests/setupsqlserver.sh");

        Log("Waiting for opensearch startup...");
        var seconds = 0;
        var certfilename = "esnode.pem";
        do
        {
            if (File.Exists(certfilename))
            {
                File.Delete(certfilename);
            }
            _ = RunCommand("docker", $"cp {containerOpensearch}:/usr/share/opensearch/config/{certfilename} .");
            Log(seconds.ToString());
            seconds++;
            if (seconds == 300)
            {
                Log("Couldn't retrieve opensearch ca cert.");
                return 1;
            }
            Thread.Sleep(1000);
        }
        while (!File.Exists(certfilename) || new FileInfo(certfilename).Length == 0);
        Log($"Got opensearch cert: {new FileInfo(certfilename).Length} bytes file.");

        var success = true;

        _ = RunCommand("dotnet", "--version");

        Log("Importing mysql:");
        var resultfilename = "result.json";
        if (File.Exists(resultfilename))
        {
            File.Delete(resultfilename);
        }
        var exitcode = RunCommand("dotnet", "run --project ../src configMysql.json",
            new() { ["SQLTOOS_CACERTFILE"] = certfilename, ["SQLTOOS_PASSWORD"] = password, ["SQLTOOS_CONNSTR"] = $"Server=localhost;Database=testdb;User Id=root;Password={password}" });
        if (exitcode != 0)
        {
            Log("Error: mysql run.");
            success = false;
        }
        else
        {
            if (!ShowJsonDiff("result.json", "result_mysql.json", "expected_mysql.json"))
            {
                Log("Error: mysql diff.");
                success = false;
            }
        }

        Log("Importing postgres:");
        if (File.Exists(resultfilename))
        {
            File.Delete(resultfilename);
        }
        exitcode = RunCommand("dotnet", "run --project ../src configPostgres.json",
            new() { ["SQLTOOS_CACERTFILE"] = certfilename, ["SQLTOOS_PASSWORD"] = password, ["SQLTOOS_CONNSTR"] = $"Server=localhost;Database=testdb;User Id=postgres;Password={password}" });
        if (exitcode != 0)
        {
            Log("Error: postgres run.");
            success = false;
        }
        else
        {
            if (!ShowJsonDiff("result.json", "result_postgres.json", "expected_postgres.json"))
            {
                Log("Error: postgres diff.");
                success = false;
            }
        }

        Log("Importing sqlserver:");
        if (File.Exists(resultfilename))
        {
            File.Delete(resultfilename);
        }
        exitcode = RunCommand("dotnet", "run --project ../src configSqlserver.json",
            new() { ["SQLTOOS_CACERTFILE"] = certfilename, ["SQLTOOS_PASSWORD"] = password, ["SQLTOOS_CONNSTR"] = $"Server=localhost;TrustServerCertificate=true;Database=testdb;User Id=sa;Password={password}" });
        if (exitcode != 0)
        {
            Log("Error: sqlserver run.");
            success = false;
        }
        else
        {
            if (!ShowJsonDiff("result.json", "result_sqlserver.json", "expected_sqlserver.json"))
            {
                Log("Error: sqlserver diff.");
                success = false;
            }
        }

        return !success ? 1 : 0;
    }

    static string GenerateAlphanumericPassword(int numberOfChars)
    {
        List<char> validChars = [];

        validChars.AddRange(Enumerable.Range('a', 'z' - 'a' + 1).Select(i => (char)i));
        validChars.AddRange(Enumerable.Range('A', 'Z' - 'A' + 1).Select(i => (char)i));
        validChars.AddRange(Enumerable.Range('0', '9' - '0' + 1).Select(i => (char)i));

        var password = new char[numberOfChars];
        do
        {
            for (var i = 0; i < numberOfChars; i++)
            {
                password[i] = validChars[RandomNumberGenerator.GetInt32(validChars.Count)];
            }
        }
        while (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit));

        return new(password);
    }

    static void StopContainers()
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = "ps",
            RedirectStandardOutput = true
        }) ?? throw new Exception("Error running: 'docker' 'ps'");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Error stopping docker containers: '{output}'");
        }

        var rows = output.Trim().Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries);
        foreach (var row in rows)
        {
            var index = row.IndexOf(' ');
            if (index >= 0)
            {
                var containerId = row[..index];
                if (containerId == "CONTAINER")
                {
                    continue;
                }
                Log($"Stopping container: {containerId}");
                _ = RunCommand("docker", $"stop {containerId}");
            }
        }
    }

    static string GetContainerId(string containerImage)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = "ps",
            RedirectStandardOutput = true
        }) ?? throw new Exception("Error running: 'docker' 'ps'");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Error getting container id: '{output}'");
        }

        var rows = output.Trim().Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries);
        foreach (var row in rows)
        {
            var index = row.IndexOf(' ');
            if (index >= 0)
            {
                var containerId = row[..index];
                if (containerId == "CONTAINER")
                {
                    continue;
                }
                if (row.Contains(containerImage))
                {
                    Log($"Found container: '{containerId}' for image: '{containerImage}'");
                    return containerId;
                }
            }
        }

        return string.Empty;
    }

    static string StartContainer(string image, int port, string bindmount, Dictionary<string, string> environmentVariables)
    {
        var envs = string.Join(" ", environmentVariables.Keys.Select(k => $"-e {k}={environmentVariables[k]}"));

        var args = bindmount == string.Empty ?
            $"run -d -p {port}:{port} {envs} {image}" :
            $"run -d -p {port}:{port} -v {bindmount} {envs} {image}";

        Log($"Starting container: 'docker' '{args}'");

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = args,
            RedirectStandardOutput = true
        }) ?? throw new Exception($"Error starting container: 'docker' '{args}'");

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        return process.ExitCode != 0 ? throw new Exception($"Error starting container: '{output}'") : output;
    }

    static int RunCommand(string exefile, string args, Dictionary<string, string> environmentVariables)
    {
        var envs = string.Join(" ", environmentVariables.Keys.Select(k => $"-e {k}={environmentVariables[k]}"));

        Log($"Running: '{exefile}' '{args}'");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = exefile,
            Arguments = args
        };
        foreach (var key in environmentVariables.Keys)
        {
            processStartInfo.EnvironmentVariables.Add(key, environmentVariables[key]);
        }

        var process = Process.Start(processStartInfo) ?? throw new Exception($"Error starting process: '{exefile}' '{args}'");
        process.WaitForExit();

        return process.ExitCode;
    }

    static int RunCommand(string exefile, string args)
    {
        Log($"Running: '{exefile}' '{args}'");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = exefile,
            Arguments = args
        };

        var process = Process.Start(processStartInfo) ?? throw new Exception($"Error starting process: '{exefile}' '{args}'");
        process.WaitForExit();

        return process.ExitCode;
    }

    static bool DownloadSqlCmd(string outputfolder)
    {
        if (!Directory.Exists(outputfolder))
        {
            throw new DirectoryNotFoundException($"Output folder doesn't exist: '{outputfolder}'");
        }

        var architecture = RuntimeInformation.ProcessArchitecture;
        var arch = architecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            _ => throw new NotSupportedException($"Unsupported architecture: '{architecture}'")
        };
        Log($"architecture: '{arch}'");

        if (!DownloadAsset("microsoft", "go-sqlcmd", $"^sqlcmd-linux-{arch}\\.tar\\.bz2$", "sqlcmd", outputfolder))
        {
            return false;
        }

        var sqlcmdpath = $"{outputfolder}/sqlcmd";
        if (RunCommand("chmod", $"+x {sqlcmdpath}") != 0)
        {
            Log($"Couldn't set executable permission for sqlcmd: '{sqlcmdpath}'");
            return false;
        }

        return true;
    }

    static bool DownloadAsset(string org, string repo, string regex, string filename, string outputfolder)
    {
        Uri baseAdress = new("https://api.github.com/");
        ProductInfoHeaderValue UserAgent = new("useragent", "1.0");

        using HttpClient client = new() { BaseAddress = baseAdress };
        client.DefaultRequestHeaders.UserAgent.Add(UserAgent);

        var asseturl = GetAssetUrl(client, org, repo, regex);
        if (asseturl == string.Empty)
        {
            Log("Error: Unable to get asset url.");
            return false;
        }

        var assetfile = Path.Combine(outputfolder, filename);
        Log($"Downloading: '{asseturl}' -> '{assetfile}'");

        return DownloadFile(client, asseturl, filename, outputfolder);
    }

    static string GetAssetUrl(HttpClient client, string org, string repo, string regex)
    {
        var releasesurl = $"repos/{org}/{repo}/releases/latest";
        Log($"releasesurl: '{client.BaseAddress}{releasesurl}'");

        client.DefaultRequestHeaders.Accept.Add(new("application/json"));

        using HttpRequestMessage request = new(HttpMethod.Get, releasesurl);
        using var response = client.Send(request);

        using var stream = response.Content.ReadAsStream();
        using StreamReader reader = new(stream);
        var json = reader.ReadToEnd();

        if (!response.IsSuccessStatusCode)
        {
            Log($"Error: {response.StatusCode} ({response.ReasonPhrase}): {json}");
            return string.Empty;
        }

        GithubRelease? release;
        try
        {
            release = JsonSerializer.Deserialize<GithubRelease>(json);
        }
        catch (JsonException ex)
        {
            Log($"Couldn't deserialize json '{releasesurl}', {ex.Message}: '{json}'");
            return string.Empty;
        }
        if (release == null || release.assets.Length == 0)
        {
            Log($"Couldn't deserialize json '{releasesurl}': '{json}'");
            return string.Empty;
        }

        Log($"regex: '{regex}'");

        var asset = release.assets.FirstOrDefault(a => Regex.IsMatch(a.name, regex));
        if (asset == null || asset.browser_download_url == string.Empty)
        {
            Log($"Couldn't find matching asset using regex '{regex}' in release '{releasesurl}': '{json}'");
            return string.Empty;
        }

        return asset.browser_download_url;
    }

    static bool DownloadFile(HttpClient client, string url, string filename, string outputfolder)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        using var response = client.Send(request);

        if (!response.IsSuccessStatusCode)
        {
            Log($"Error: {response.StatusCode} ({response.ReasonPhrase})");
            return false;
        }

        using var stream = response.Content.ReadAsStream();
        using var reader = ReaderFactory.Open(stream);

        while (reader.MoveToNextEntry())
        {
            var entry = reader.Entry;

            if (entry.IsDirectory)
            {
                continue;
            }

            if (entry.Key == filename)
            {
                var outputPath = Path.Combine(outputfolder, entry.Key);
                _ = Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                using var outFile = File.Create(outputPath);
                reader.WriteEntryTo(outFile);
                Log($"Downloaded: '{url}' -> '{outputPath}'");
            }
        }

        return true;
    }

    static void CompileIsatty(string outputfolder)
    {
        var curdir = Directory.GetCurrentDirectory();
        var dockerfile = @"FROM ubuntu
WORKDIR /out
RUN apt-get update && \
    apt-get -y install gcc
RUN echo ""Compiling isatty work around for mysql"" && \
    echo ""int isatty(int fd) { return 1; }"" | gcc -O2 -fpic -shared -ldl -o /out/isatty.so -xc - ";

        File.WriteAllText("Dockerfile", dockerfile);

        var imagetag = "mysqlworkaround";

        _ = RunCommand("docker", $"rmi -f {imagetag}");
        _ = RunCommand("docker", $"system prune -f");
        var args = $"build -t {imagetag} .";
        var exitcode = RunCommand("docker", args, new() { ["DOCKER_BUILDKIT"] = "0" });
        if (exitcode != 0)
        {
            Log($"Dockerfile:{Environment.NewLine}{dockerfile}");
            throw new Exception($"Error building image: 'docker' '{args}'");
        }

        File.Delete("Dockerfile");

        if (Directory.Exists("artifacts"))
        {
            Directory.Delete("artifacts", recursive: true);
        }
        _ = Directory.CreateDirectory("artifacts");
        _ = RunCommand("docker", $"run --entrypoint cp -v {curdir}/artifacts:/artifacts {imagetag} /out/isatty.so /artifacts");
        _ = RunCommand("docker", $"rmi -f {imagetag}");
        File.Move("artifacts/isatty.so", $"{outputfolder}/isatty.so", overwrite: true);
        Directory.Delete("artifacts", recursive: false);
    }

    static bool ShowJsonDiff(string result, string newresult, string expected)
    {
        if (!File.Exists(result))
        {
            Log($"Result file not found: '{result}'");
            return false;
        }
        var length1 = new FileInfo(result).Length;
        if (length1 == 0)
        {
            Log($"Result is zero length: '{result}'");
            return false;
        }
        if (!File.Exists(expected))
        {
            Log($"Expected file not found: '{expected}'");
            return false;
        }
        var length2 = new FileInfo(expected).Length;
        if (length2 == 0)
        {
            Log($"Expected is zero length: '{expected}'");
            return false;
        }

        var json = File.ReadAllText(result);
        using var doc = JsonDocument.Parse(json);
        using MemoryStream ms = new();
        using Utf8JsonWriter writer = new(ms, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Name != "took")
            {
                property.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
        writer.Flush();

        ms.WriteByte((byte)'\n');
        File.WriteAllText(newresult, Encoding.UTF8.GetString(ms.ToArray()));

        var exitcode = RunCommand("diff", $"{newresult} {expected}");
        return exitcode == 0;
    }

    static void Log(string message)
    {
        var oldcolor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Green;
        try
        {
            Console.WriteLine(message);
        }
        finally
        {
            Console.ForegroundColor = oldcolor;
        }
    }
}
