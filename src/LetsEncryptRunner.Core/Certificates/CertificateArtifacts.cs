// Copyright Xeno Innovations, Inc. 2026
// See the LICENSE file in the project root for more information.

namespace LetsEncryptRunner.Core.Certificates;

public sealed record CertificateArtifacts(
  string Directory,
  string CertificatePath,
  string ChainPath,
  string FullChainPath,
  string PrivateKeyPath,
  string PfxPath,
  DateTimeOffset ExpiresUtc);
