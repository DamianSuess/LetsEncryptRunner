// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

using System.Net.Http.Headers;
using System.Text.Json;
using LetsEncryptRunner.Core.Certificates;
using LetsEncryptRunner.Core.Configuration;

namespace LetsEncryptRunner.Core.Deployment;

public sealed class CertificateDeployer
{
  public async Task DeployAsync(
    WebsiteConfig website,
    CertificateArtifacts artifacts,
    Action<string>? log = null,
    CancellationToken cancellationToken = default)
  {
    if (string.Equals(website.Deployment.Type, DeploymentTypes.None, StringComparison.OrdinalIgnoreCase))
    {
      log?.Invoke($"Deployment skipped for '{website.Name}' because deployment type is None.");
      return;
    }

    if (string.Equals(website.Deployment.Type, DeploymentTypes.CPanel, StringComparison.OrdinalIgnoreCase))
    {
      await DeployToCPanelAsync(website, artifacts, log, cancellationToken);
      return;
    }

    throw new NotSupportedException($"Unsupported deployment type: {website.Deployment.Type}");
  }

  private static async Task DeployToCPanelAsync(
    WebsiteConfig website,
    CertificateArtifacts artifacts,
    Action<string>? log,
    CancellationToken cancellationToken)
  {
    var cpanel = website.Deployment.CPanel;
    if (string.IsNullOrWhiteSpace(cpanel.BaseUrl))
      throw new InvalidOperationException("cPanel base URL is required.");

    if (string.IsNullOrWhiteSpace(cpanel.Username))
      throw new InvalidOperationException("cPanel username is required.");

    var token = Environment.GetEnvironmentVariable(cpanel.ApiTokenEnvironmentVariable);
    if (string.IsNullOrWhiteSpace(token))
      throw new InvalidOperationException($"Environment variable '{cpanel.ApiTokenEnvironmentVariable}' does not contain a cPanel API token.");

    var handler = new HttpClientHandler();
    if (cpanel.AllowInvalidTlsCertificate)
      handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

    using var httpClient = new HttpClient(handler)
    {
      BaseAddress = new Uri(EnsureTrailingSlash(cpanel.BaseUrl)),
      Timeout = TimeSpan.FromSeconds(60)
    };

    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("cpanel", $"{cpanel.Username}:{token}");

    var installDomain = string.IsNullOrWhiteSpace(cpanel.DomainNameOverride)
      ? website.DomainNames[0]
      : cpanel.DomainNameOverride;

    var form = new Dictionary<string, string>
    {
      ["domain"] = installDomain,
      ["cert"] = await File.ReadAllTextAsync(artifacts.CertificatePath, cancellationToken),
      ["key"] = await File.ReadAllTextAsync(artifacts.PrivateKeyPath, cancellationToken),
      ["cabundle"] = await File.ReadAllTextAsync(artifacts.ChainPath, cancellationToken)
    };

    log?.Invoke($"Uploading certificate to cPanel SSL/install_ssl for {installDomain}.");
    using var response = await httpClient.PostAsync("execute/SSL/install_ssl", new FormUrlEncodedContent(form), cancellationToken);
    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

    if (!response.IsSuccessStatusCode)
    {
      throw new InvalidOperationException($"cPanel SSL upload failed: {(int)response.StatusCode} {response.ReasonPhrase}. {responseBody}");
    }

    using var document = JsonDocument.Parse(responseBody);
    if (document.RootElement.TryGetProperty("status", out var statusElement) && statusElement.GetInt32() != 1)
    {
      var error = document.RootElement.TryGetProperty("errors", out var errorsElement)
        ? errorsElement.ToString()
        : responseBody;

      throw new InvalidOperationException($"cPanel SSL upload returned failure: {error}");
    }

    website.State.LastSuccessfulUploadUtc = DateTimeOffset.UtcNow;
    website.State.LastError = null;
  }

  private static string EnsureTrailingSlash(string value)
  {
    return value.EndsWith('/') ? value : value + "/";
  }
}
