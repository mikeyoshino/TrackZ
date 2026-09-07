using System.Diagnostics;
using System.Text.Json;
using Xunit.Sdk;

namespace TrackZ.Api.Tests.Deployment;

public sealed class ProductionDeploymentContractTests
{
    [Fact]
    public async Task Production_compose_renders_the_isolated_TrackZ_service_graph()
    {
        await RequireDockerComposeAsync();
        var repositoryRoot = FindRepositoryRoot();
        var composePath = Path.Combine(repositoryRoot, "deploy", "compose.production.yaml");
        var configurationRoot = CreateConfigurationRoot();

        try
        {
            var result = await RunAsync(
                "docker",
                ["compose", "-f", composePath, "config", "--format", "json"],
                new Dictionary<string, string>
                {
                    ["TRACKZ_CONFIG_ROOT"] = configurationRoot,
                    ["TRACKZ_IMAGE"] = $"ghcr.io/example/trackz@sha256:{new string('a', 64)}"
                });

            Assert.True(result.ExitCode == 0, result.Error);
            using var rendered = JsonDocument.Parse(result.Output);
            var root = rendered.RootElement;
            Assert.Equal("trackz-production", root.GetProperty("name").GetString());

            var services = root.GetProperty("services");
            Assert.Equal(
                ["api", "catalog-deploy", "minio", "minio-bootstrap", "postgres"],
                services.EnumerateObject().Select(service => service.Name).Order().ToArray());

            foreach (var service in services.EnumerateObject())
            {
                Assert.False(service.Value.TryGetProperty("ports", out _),
                    $"{service.Name} must not publish a host port.");
            }

            var networks = root.GetProperty("networks");
            Assert.True(networks.GetProperty("backend").GetProperty("internal").GetBoolean());
            Assert.True(networks.GetProperty("frontend").GetProperty("external").GetBoolean());
            Assert.Equal(
                "toystore-production_frontend",
                networks.GetProperty("frontend").GetProperty("name").GetString());

            var apiNetworks = services.GetProperty("api").GetProperty("networks");
            Assert.True(apiNetworks.TryGetProperty("backend", out _));
            Assert.Contains(
                "trackz-api",
                apiNetworks.GetProperty("frontend").GetProperty("aliases")
                    .EnumerateArray().Select(alias => alias.GetString()));

            foreach (var serviceName in new[] { "postgres", "minio", "minio-bootstrap", "catalog-deploy" })
            {
                var serviceNetworks = services.GetProperty(serviceName).GetProperty("networks");
                Assert.True(serviceNetworks.TryGetProperty("backend", out _));
                Assert.False(serviceNetworks.TryGetProperty("frontend", out _));
            }

            var apiDependencies = services.GetProperty("api").GetProperty("depends_on");
            Assert.Equal(
                "service_completed_successfully",
                apiDependencies.GetProperty("catalog-deploy").GetProperty("condition").GetString());

            var volumes = root.GetProperty("volumes");
            Assert.Equal(
                "trackz-production-postgres-data",
                volumes.GetProperty("postgres-data").GetProperty("name").GetString());
            Assert.Equal(
                "trackz-production-minio-data",
                volumes.GetProperty("minio-data").GetProperty("name").GetString());
        }
        finally
        {
            Directory.Delete(configurationRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("deploy/trackz-bootstrap")]
    [InlineData("deploy/trackz-deploy")]
    public async Task Production_operator_commands_have_valid_Bash_syntax(string relativePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var result = await RunAsync("bash", ["-n", Path.Combine(repositoryRoot, relativePath)]);

        Assert.True(result.ExitCode == 0, result.Error);
    }

    private static async Task RequireDockerComposeAsync()
    {
        var result = await RunAsync("docker", ["compose", "version"]);
        if (result.ExitCode != 0)
        {
            throw SkipException.ForSkip($"Docker Compose is required for this contract test. {result.Error}");
        }
    }

    private static string CreateConfigurationRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-deployment-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "postgres.env"), "POSTGRES_DB=trackz\nPOSTGRES_USER=trackz\nPOSTGRES_PASSWORD=test-only\n");
        File.WriteAllText(Path.Combine(root, "minio.env"), "MINIO_ROOT_USER=trackz-root\nMINIO_ROOT_PASSWORD=test-only-root-secret\n");
        File.WriteAllText(
            Path.Combine(root, "trackz.env"),
            "ConnectionStrings__TrackZ=Host=postgres;Database=trackz;Username=trackz;Password=test-only\n" +
            "Jwt__Issuer=trackz-api\nJwt__Audience=trackz-mobile\nJwt__SigningKey=test-only-signing-key-with-more-than-thirty-two-characters\n" +
            "ObjectStorage__ServiceUrl=http://minio:9000\nObjectStorage__Bucket=trackz-private\n" +
            "ObjectStorage__AccessKey=trackz-api\nObjectStorage__SecretKey=test-only-storage-secret\n" +
            "MediaAccess__PublicOrigin=https://api.trackz.sytoys.shop\nMediaAccess__SigningKey=test-only-media-key-with-more-than-thirty-two-characters\n");
        return root;
    }

    private static async Task<CommandResult> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (environment is not null)
        {
            foreach (var (key, value) in environment) process.StartInfo.Environment[key] = value;
        }

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception)
        {
            return new CommandResult(-1, string.Empty, exception.Message);
        }

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new CommandResult(process.ExitCode, await output, await error);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TrackZ.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the TrackZ repository root.");
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);
}
