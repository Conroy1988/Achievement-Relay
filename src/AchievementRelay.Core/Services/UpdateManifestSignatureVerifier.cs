using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AchievementRelay.Core.Services;

public static class UpdateManifestSignatureVerifier
{
    private const string SignatureAlgorithm = "rsa-sha256-pkcs1";
    private const string CodeSigningEnhancedKeyUsage = "1.3.6.1.5.5.7.3.3";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static ManifestSignatureVerificationResult Verify(
        byte[] manifestBytes,
        byte[] signatureEnvelopeBytes,
        IReadOnlySet<string> pinnedPublisherCertificates)
    {
        if (pinnedPublisherCertificates.Count == 0)
        {
            return ManifestSignatureVerificationResult.Failure(
                "This build does not contain a trusted update-publisher identity.");
        }

        ManifestSignatureEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ManifestSignatureEnvelope>(
                signatureEnvelopeBytes,
                JsonOptions) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return ManifestSignatureVerificationResult.Failure(
                "The update manifest signature record is invalid.");
        }

        if (envelope.SchemaVersion != 1 ||
            !string.Equals(envelope.Algorithm, SignatureAlgorithm, StringComparison.Ordinal))
        {
            return ManifestSignatureVerificationResult.Failure(
                "The update manifest uses an unsupported signature format.");
        }

        try
        {
            var certificateBytes = Convert.FromBase64String(envelope.Certificate);
            var signatureBytes = Convert.FromBase64String(envelope.Signature);
            if (certificateBytes.Length is <= 0 or > 32 * 1024 ||
                signatureBytes.Length is <= 0 or > 8 * 1024)
            {
                return ManifestSignatureVerificationResult.Failure(
                    "The update manifest signature record is outside the supported size.");
            }

            using var certificate = X509CertificateLoader.LoadCertificate(certificateBytes);
            var certificateSha256 = Convert.ToHexString(SHA256.HashData(certificate.RawData))
                .ToLowerInvariant();
            if (!string.Equals(
                    certificateSha256,
                    envelope.CertificateSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !pinnedPublisherCertificates.Contains(certificateSha256))
            {
                return ManifestSignatureVerificationResult.Failure(
                    "The update manifest was signed by an unexpected publisher certificate.");
            }

            var codeSigningAllowed = certificate.Extensions
                .OfType<X509EnhancedKeyUsageExtension>()
                .SelectMany(extension => extension.EnhancedKeyUsages.Cast<Oid>())
                .Any(oid => string.Equals(oid.Value, CodeSigningEnhancedKeyUsage, StringComparison.Ordinal));
            if (!codeSigningAllowed)
            {
                return ManifestSignatureVerificationResult.Failure(
                    "The update publisher certificate is not valid for code signing.");
            }

            using var rsa = certificate.GetRSAPublicKey();
            if (rsa is null || rsa.KeySize < 2048 ||
                !rsa.VerifyData(
                    manifestBytes,
                    signatureBytes,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1))
            {
                return ManifestSignatureVerificationResult.Failure(
                    "The update manifest signature did not verify.");
            }

            return ManifestSignatureVerificationResult.Success(
                certificateSha256,
                new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero),
                new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero));
        }
        catch (Exception exception) when (exception is
            FormatException or
            CryptographicException or
            ArgumentException)
        {
            return ManifestSignatureVerificationResult.Failure(
                "The update manifest signature record could not be verified.");
        }
    }

    private sealed record ManifestSignatureEnvelope
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("algorithm")]
        public string Algorithm { get; init; } = string.Empty;

        [JsonPropertyName("certificateSha256")]
        public string CertificateSha256 { get; init; } = string.Empty;

        [JsonPropertyName("certificate")]
        public string Certificate { get; init; } = string.Empty;

        [JsonPropertyName("signature")]
        public string Signature { get; init; } = string.Empty;
    }
}

public sealed record ManifestSignatureVerificationResult(
    bool IsValid,
    string Message,
    string? CertificateSha256,
    DateTimeOffset? CertificateNotBeforeUtc,
    DateTimeOffset? CertificateNotAfterUtc)
{
    public static ManifestSignatureVerificationResult Success(
        string certificateSha256,
        DateTimeOffset certificateNotBeforeUtc,
        DateTimeOffset certificateNotAfterUtc) =>
        new(
            true,
            "The update manifest publisher is trusted.",
            certificateSha256,
            certificateNotBeforeUtc,
            certificateNotAfterUtc);

    public static ManifestSignatureVerificationResult Failure(string message) =>
        new(false, message, null, null, null);
}
