using System.Net;
using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using LetsEncryptRunner.Core.Configuration;
using ChallengeStatus = Certes.Acme.Resource.ChallengeStatus;

namespace LetsEncryptRunner.Core.Certificates;

public sealed class AcmeCertificateService
{
    private readonly HttpClient _httpClient;

    public AcmeCertificateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    public async Task<CertificateArtifacts> IssueAsync(
        RunnerConfig config,
        WebsiteConfig website,
        string configPath,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ValidateWebsite(website);

        var accountKeyPath = PathHelpers.ResolveFromConfig(configPath, config.AccountKeyPath);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(accountKeyPath) ?? System.IO.Directory.GetCurrentDirectory());

        var accountKey = File.Exists(accountKeyPath)
            ? KeyFactory.FromPem(await File.ReadAllTextAsync(accountKeyPath, cancellationToken))
            : null;

        var directoryUri = config.UseLetsEncryptStaging
            ? WellKnownServers.LetsEncryptStagingV2
            : WellKnownServers.LetsEncryptV2;

        var acme = accountKey is null
            ? new AcmeContext(directoryUri)
            : new AcmeContext(directoryUri, accountKey);

        if (accountKey is null)
        {
            log?.Invoke("Creating Let's Encrypt ACME account.");
            await acme.NewAccount(website.EmailAddress, true);
            await File.WriteAllTextAsync(accountKeyPath, acme.AccountKey.ToPem(), cancellationToken);
        }
        else
        {
            log?.Invoke("Using saved Let's Encrypt ACME account key.");
            await acme.Account();
        }

        log?.Invoke($"Ordering certificate for {string.Join(", ", website.DomainNames)}.");
        var order = await acme.NewOrder(website.DomainNames);
        var authorizations = await order.Authorizations();
        var challengeFiles = new List<string>();

        try
        {
            foreach (var authorization in authorizations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var httpChallenge = await authorization.Http()
                    ?? throw new InvalidOperationException("The ACME server did not return an HTTP-01 challenge.");

                var challengePath = WriteChallengeFile(configPath, website, httpChallenge, challengeFiles);
                log?.Invoke($"Wrote HTTP-01 challenge file: {challengePath}");

                if (website.HttpChallenge.ValidateChallengeFileBeforeSubmit)
                {
                    await ValidateChallengeReachabilityAsync(website, httpChallenge, cancellationToken);
                }

                var challenge = await httpChallenge.Validate();
                log?.Invoke($"Submitted HTTP-01 challenge token {httpChallenge.Token}; status: {challenge.Status}.");

                if (challenge.Status == ChallengeStatus.Invalid)
                {
                    throw new InvalidOperationException($"HTTP-01 challenge failed for token {httpChallenge.Token}.");
                }
            }

            var certificateKey = KeyFactory.NewKey(KeyAlgorithm.RS256, 2048);
            var csr = new CsrInfo
            {
                CommonName = website.DomainNames[0]
            };

            log?.Invoke("Finalizing certificate order.");
            var chain = await order.Generate(csr, certificateKey, retryCount: 10);

            return await SaveArtifactsAsync(config, website, configPath, chain, certificateKey, cancellationToken);
        }
        finally
        {
            if (website.HttpChallenge.CleanUpChallengeFiles)
            {
                foreach (var file in challengeFiles.Where(File.Exists))
                {
                    File.Delete(file);
                }
            }
        }
    }

    private static void ValidateWebsite(WebsiteConfig website)
    {
        if (!website.Enabled)
        {
            throw new InvalidOperationException($"Website '{website.Name}' is disabled.");
        }

        if (website.DomainNames.Count == 0 || website.DomainNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("At least one domain name is required.");
        }

        if (website.DomainNames.Any(domain => domain.TrimStart().StartsWith('*')))
        {
            throw new InvalidOperationException("HTTP-01 validation cannot issue wildcard certificates. Use DNS-01 for wildcard domains.");
        }

        if (string.IsNullOrWhiteSpace(website.EmailAddress))
        {
            throw new InvalidOperationException("Email address is required.");
        }

        if (string.IsNullOrWhiteSpace(website.HttpChallenge.WebRootPath))
        {
            throw new InvalidOperationException("HTTP challenge web root path is required.");
        }
    }

    private static string WriteChallengeFile(
        string configPath,
        WebsiteConfig website,
        IChallengeContext httpChallenge,
        List<string> challengeFiles)
    {
        var webRootPath = PathHelpers.ResolveFromConfig(configPath, website.HttpChallenge.WebRootPath);
        var challengeDirectory = Path.Combine(webRootPath, ".well-known", "acme-challenge");
        System.IO.Directory.CreateDirectory(challengeDirectory);

        var challengePath = Path.Combine(challengeDirectory, httpChallenge.Token);
        File.WriteAllText(challengePath, httpChallenge.KeyAuthz);
        challengeFiles.Add(challengePath);

        return challengePath;
    }

    private async Task ValidateChallengeReachabilityAsync(
        WebsiteConfig website,
        IChallengeContext httpChallenge,
        CancellationToken cancellationToken)
    {
        foreach (var domain in website.DomainNames)
        {
            var uri = new Uri($"http://{domain}/.well-known/acme-challenge/{httpChallenge.Token}");
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode != HttpStatusCode.OK || !string.Equals(body.Trim(), httpChallenge.KeyAuthz, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"HTTP-01 challenge file is not reachable at {uri}. Status: {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
        }
    }

    private static async Task<CertificateArtifacts> SaveArtifactsAsync(
        RunnerConfig config,
        WebsiteConfig website,
        string configPath,
        CertificateChain chain,
        IKey certificateKey,
        CancellationToken cancellationToken)
    {
        var storePath = PathHelpers.ResolveFromConfig(configPath, config.CertificateStorePath);
        var siteName = string.IsNullOrWhiteSpace(website.Name) ? website.DomainNames[0] : website.Name;
        var certificateDirectory = Path.Combine(
            storePath,
            PathHelpers.SafeFileName(siteName),
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"));

        System.IO.Directory.CreateDirectory(certificateDirectory);

        var certificatePem = chain.Certificate.ToPem();
        var issuerPem = string.Join(Environment.NewLine, chain.Issuers.Select(issuer => issuer.ToPem()));
        var fullChainPem = certificatePem + Environment.NewLine + issuerPem;
        var privateKeyPem = certificateKey.ToPem();

        var certificatePath = Path.Combine(certificateDirectory, "cert.pem");
        var chainPath = Path.Combine(certificateDirectory, "chain.pem");
        var fullChainPath = Path.Combine(certificateDirectory, "fullchain.pem");
        var privateKeyPath = Path.Combine(certificateDirectory, "privkey.pem");
        var pfxPath = Path.Combine(certificateDirectory, "certificate.pfx");

        await File.WriteAllTextAsync(certificatePath, certificatePem, cancellationToken);
        await File.WriteAllTextAsync(chainPath, issuerPem, cancellationToken);
        await File.WriteAllTextAsync(fullChainPath, fullChainPem, cancellationToken);
        await File.WriteAllTextAsync(privateKeyPath, privateKeyPem, cancellationToken);

        var pfxPassword = Environment.GetEnvironmentVariable(config.PfxPasswordEnvironmentVariable) ?? string.Empty;
        var pfxBuilder = chain.ToPfx(certificateKey);
        pfxBuilder.FullChain = true;
        var pfx = pfxBuilder.Build(siteName, pfxPassword);
        await File.WriteAllBytesAsync(pfxPath, pfx, cancellationToken);

        using var certificate = new X509Certificate2(chain.Certificate.ToDer());
        var expiresUtc = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);

        website.State.LastIssuedUtc = DateTimeOffset.UtcNow;
        website.State.ExpiresUtc = expiresUtc;
        website.State.LastCertificateDirectory = certificateDirectory;
        website.State.LastError = null;

        return new CertificateArtifacts(
            certificateDirectory,
            certificatePath,
            chainPath,
            fullChainPath,
            privateKeyPath,
            pfxPath,
            expiresUtc);
    }
}
