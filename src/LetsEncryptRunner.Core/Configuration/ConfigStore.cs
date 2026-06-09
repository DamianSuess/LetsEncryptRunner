using System.Text.Json;

namespace LetsEncryptRunner.Core.Configuration;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<RunnerConfig> LoadAsync(string configPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Config file was not found: {configPath}", configPath);
        }

        await using var stream = File.OpenRead(configPath);
        var config = await JsonSerializer.DeserializeAsync<RunnerConfig>(stream, JsonOptions, cancellationToken);
        return config ?? throw new InvalidOperationException($"Config file is empty or invalid: {configPath}");
    }

    public async Task SaveAsync(string configPath, RunnerConfig config, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(configPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(configPath);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions, cancellationToken);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
    }

    public RunnerConfig CreateDefault()
    {
        return new RunnerConfig();
    }
}

