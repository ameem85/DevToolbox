using System.Security.Cryptography.X509Certificates;

namespace DevToolbox.Core.Abstractions.Services;

public sealed record RootCertificateExportResult(
    bool Success,
    string SubjectName,
    string Thumbprint,
    string CerFilePath,
    string PemFilePath,
    DateTimeOffset NotAfterUtc,
    string StatusMessage);

/// <summary>
/// Manages the local MITM Root Certificate Authority (CA) and dynamically mints per-host
/// leaf X.509 certificates for HTTPS <c>CONNECT</c> TLS termination in <see cref="INetworkProxyService"/>.
/// </summary>
public interface IProxyCertificateService
{
    X509Certificate2 GetOrCreateRootCertificate();

    X509Certificate2 GetOrCreateServerCertificate(string hostName);

    Task<RootCertificateExportResult> ExportRootCertificateAsync(
        string? outputDirectory = null,
        CancellationToken cancellationToken = default);
}
