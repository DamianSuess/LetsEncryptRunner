using System.Diagnostics;
using System.Runtime.Versioning;

namespace LetsEncryptRunner.Core;

public sealed class WindowsScheduledTaskService
{
    [SupportedOSPlatform("windows")]
    public async Task InstallAsync(
        string taskName,
        string executablePath,
        string arguments,
        int intervalDays,
        CancellationToken cancellationToken = default)
    {
        if (intervalDays < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalDays), "Interval days must be greater than zero.");
        }

        var taskRunCommand = $"\"{executablePath}\" {arguments}";

        await RunSchTasksAsync(
            [
                "/Create",
                "/TN", taskName,
                "/TR", taskRunCommand,
                "/SC", "DAILY",
                "/MO", intervalDays.ToString(),
                "/F"
            ],
            cancellationToken);

        await RunSchTasksAsync(
            [
                "/Create",
                "/TN", taskName + ".AtLogon",
                "/TR", taskRunCommand,
                "/SC", "ONLOGON",
                "/F"
            ],
            cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    public async Task UninstallAsync(string taskName, CancellationToken cancellationToken = default)
    {
        await DeleteTaskIfExistsAsync(taskName, cancellationToken);
        await DeleteTaskIfExistsAsync(taskName + ".AtLogon", cancellationToken);
    }

    private static async Task DeleteTaskIfExistsAsync(string taskName, CancellationToken cancellationToken)
    {
        await RunSchTasksAsync(["/Delete", "/TN", taskName, "/F"], cancellationToken, throwOnFailure: false);
    }

    private static async Task RunSchTasksAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnFailure = true)
    {
        using var process = new Process();
        process.StartInfo.FileName = "schtasks.exe";
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (throwOnFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"schtasks.exe failed with exit code {process.ExitCode}. {output} {error}".Trim());
        }
    }
}

