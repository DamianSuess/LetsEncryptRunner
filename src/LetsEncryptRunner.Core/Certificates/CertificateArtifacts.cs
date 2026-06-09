namespace LetsEncryptRunner.Core.Certificates;

public sealed record CertificateArtifacts(
    string Directory,
    string CertificatePath,
    string ChainPath,
    string FullChainPath,
    string PrivateKeyPath,
    string PfxPath,
    DateTimeOffset ExpiresUtc);

