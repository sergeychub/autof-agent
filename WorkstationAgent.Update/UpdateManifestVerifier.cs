using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WorkstationAgent.Update;

public static class UpdateManifestVerifier
{
    public static bool Verify(UpdateManifest manifest, string publicKeyPem, out string error)
    {
        error = string.Empty;

        if (!UpdateTrust.IsConfigured(publicKeyPem))
        {
            error = "Update manifest public key is not configured.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.Signature))
        {
            error = "Update manifest signature is empty.";
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            var payload = BuildSigningPayload(manifest);
            var signature = Convert.FromBase64String(manifest.Signature);

            if (rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            {
                return true;
            }

            error = "Update manifest signature is invalid.";
            return false;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            error = $"Update manifest signature could not be verified: {ex.Message}";
            return false;
        }
    }

    public static byte[] BuildSigningPayload(UpdateManifest manifest)
    {
        var fields = new[]
        {
            manifest.ReleaseId.Trim(),
            manifest.Channel.Trim(),
            manifest.Runtime.Trim(),
            manifest.Version.Trim(),
            manifest.CommitSha.Trim(),
            manifest.PublishedAtUtc.Trim(),
            manifest.SizeBytes.ToString(CultureInfo.InvariantCulture),
            manifest.Sha256.Trim().ToLowerInvariant(),
            manifest.DownloadUrl.Trim()
        };

        return Encoding.UTF8.GetBytes(string.Join("\n", fields));
    }
}
