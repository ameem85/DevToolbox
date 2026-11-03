using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.Core.Services;

/// <summary>
/// Generates and caches a local self-signed Root CA certificate and dynamically mints
/// SAN-enabled per-host TLS leaf certificates for HTTPS proxy interception.
/// </summary>
public sealed class ProxyCertificateService : IProxyCertificateService, IDisposable
{
    private readonly Lock _rootLock = new();
    private readonly ConcurrentDictionary<string, X509Certificate2> _leafCertCache = new(StringComparer.OrdinalIgnoreCase);
    private X509Certificate2? _rootCaCertificate;

    public X509Certificate2 GetOrCreateRootCertificate()
    {
        lock (_rootLock)
        {
            if (_rootCaCertificate is not null)
            {
                return _rootCaCertificate;
            }

            var storageDir = GetCertificateStorageDirectory();
            Directory.CreateDirectory(storageDir);
            var pfxPath = Path.Combine(storageDir, "DevToolboxRootCA.pfx");

            if (File.Exists(pfxPath))
            {
                try
                {
                    _rootCaCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                        pfxPath,
                        null,
                        X509KeyStorageFlags.Exportable);

                    if (_rootCaCertificate.NotAfter > DateTime.UtcNow.AddDays(7))
                    {
                        return _rootCaCertificate;
                    }
                }
                catch
                {
                    // Regenerate if corrupted
                }
            }

            using var rsa = RSA.Create(2048);
            var distinguishedName = new X500DistinguishedName(
                "CN=DevToolbox MITM Root CA, O=DevToolbox Companion, OU=QA & Network Debugging");

            var request = new CertificateRequest(
                distinguishedName,
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(
                    certificateAuthority: true,
                    hasPathLengthConstraint: false,
                    pathLengthConstraint: 0,
                    critical: true));

            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature,
                    critical: true));

            request.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

            var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
            var notAfter = DateTimeOffset.UtcNow.AddYears(5);

            using var ephemeralRoot = request.CreateSelfSigned(notBefore, notAfter);
            var pfxBytes = ephemeralRoot.Export(X509ContentType.Pfx);
            File.WriteAllBytes(pfxPath, pfxBytes);

            _rootCaCertificate = X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                null,
                X509KeyStorageFlags.Exportable);

            return _rootCaCertificate;
        }
    }

    public X509Certificate2 GetOrCreateServerCertificate(string hostName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);

        var normalizedHost = hostName.Trim();
        return _leafCertCache.GetOrAdd(normalizedHost, host =>
        {
            var rootCa = GetOrCreateRootCertificate();
            using var leafRsa = RSA.Create(2048);

            var request = new CertificateRequest(
                $"CN={host}, O=DevToolbox MITM Proxy",
                leafRsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));

            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    critical: true));

            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    [new Oid("1.3.6.1.5.5.7.3.1")], // serverAuth
                    critical: false));

            var sanBuilder = new SubjectAlternativeNameBuilder();
            if (IPAddress.TryParse(host, out var ipAddress))
            {
                sanBuilder.AddIpAddress(ipAddress);
            }
            else
            {
                sanBuilder.AddDnsName(host);
            }

            request.CertificateExtensions.Add(sanBuilder.Build());

            Span<byte> serialNumber = stackalloc byte[16];
            RandomNumberGenerator.Fill(serialNumber);
            serialNumber[0] &= 0x7F; // Ensure positive serial number

            var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
            var notAfter = DateTimeOffset.UtcNow.AddYears(1);

            using var signedCert = request.Create(rootCa, notBefore, notAfter, serialNumber);
            using var certWithKey = signedCert.CopyWithPrivateKey(leafRsa);
            var pfxBytes = certWithKey.Export(X509ContentType.Pfx);

            return X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                null,
                X509KeyStorageFlags.Exportable);
        });
    }

    public async Task<RootCertificateExportResult> ExportRootCertificateAsync(
        string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var rootCa = GetOrCreateRootCertificate();
        var targetDir = string.IsNullOrWhiteSpace(outputDirectory)
            ? GetCertificateStorageDirectory()
            : outputDirectory;

        Directory.CreateDirectory(targetDir);

        var cerBytes = rootCa.Export(X509ContentType.Cert);
        var cerPath = Path.Combine(targetDir, "DevToolboxRootCA.cer");
        await File.WriteAllBytesAsync(cerPath, cerBytes, cancellationToken).ConfigureAwait(false);

        var pemBuilder = new StringBuilder();
        pemBuilder.AppendLine("-----BEGIN CERTIFICATE-----");
        pemBuilder.AppendLine(Convert.ToBase64String(cerBytes, Base64FormattingOptions.InsertLineBreaks));
        pemBuilder.AppendLine("-----END CERTIFICATE-----");

        var pemPath = Path.Combine(targetDir, "DevToolboxRootCA.pem");
        await File.WriteAllTextAsync(pemPath, pemBuilder.ToString(), Encoding.ASCII, cancellationToken)
            .ConfigureAwait(false);

        return new RootCertificateExportResult(
            true,
            rootCa.Subject,
            rootCa.Thumbprint,
            cerPath,
            pemPath,
            rootCa.NotAfter.ToUniversalTime(),
            $"Exported Root CA ({rootCa.Thumbprint[..12]}...) to {cerPath}");
    }

    public void Dispose()
    {
        foreach (var leaf in _leafCertCache.Values)
        {
            leaf.Dispose();
        }

        _leafCertCache.Clear();
        _rootCaCertificate?.Dispose();
    }

    private static string GetCertificateStorageDirectory()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = Path.GetTempPath();
        }

        return Path.Combine(baseDir, "DevToolbox", "Certificates");
    }
}
