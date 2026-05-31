using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkstationAgent.Update.Tests;

[TestClass]
public sealed class UpdateManifestVerifierTests
{
    [TestMethod]
    public void VerifyAcceptsValidSignature()
    {
        using var rsa = RSA.Create(3072);
        var manifest = CreateManifest();
        manifest.Signature = Sign(rsa, manifest);
        var publicKeyPem = ExportPublicKeyPem(rsa);

        var verified = UpdateManifestVerifier.Verify(manifest, publicKeyPem, out var error);

        Assert.IsTrue(verified, error);
    }

    [TestMethod]
    public void VerifyRejectsTamperedManifest()
    {
        using var rsa = RSA.Create(3072);
        var manifest = CreateManifest();
        manifest.Signature = Sign(rsa, manifest);
        manifest.Version = "0.1.125+changed";
        var publicKeyPem = ExportPublicKeyPem(rsa);

        var verified = UpdateManifestVerifier.Verify(manifest, publicKeyPem, out _);

        Assert.IsFalse(verified);
    }

    private static UpdateManifest CreateManifest()
    {
        return new UpdateManifest
        {
            ReleaseId = "main-123-abcdef",
            Channel = "main",
            Runtime = "win-x64",
            Version = "0.1.123+abcdef",
            CommitSha = "abcdef123456",
            PublishedAtUtc = "2026-05-31T12:00:00.0000000Z",
            SizeBytes = 1024,
            Sha256 = "abc123",
            DownloadUrl = "/workstation-agent/update/download/main-123-abcdef"
        };
    }

    private static string Sign(RSA rsa, UpdateManifest manifest)
    {
        var signature = rsa.SignData(
            UpdateManifestVerifier.BuildSigningPayload(manifest),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);

        return Convert.ToBase64String(signature);
    }

    private static string ExportPublicKeyPem(RSA rsa)
    {
        return new string(PemEncoding.Write("PUBLIC KEY", rsa.ExportSubjectPublicKeyInfo()));
    }
}
