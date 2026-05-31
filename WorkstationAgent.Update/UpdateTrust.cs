namespace WorkstationAgent.Update;

public static class UpdateTrust
{
    public const string ManifestPublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEA14qkpd4GqqHNmoEk5cgU
WF3vK8HHQy3gZ54n0TDGhQm30RVwCwUUJqGeLnGnXtz0+i2ZJ2p1EIFJuxFt7JHK
5jEBu8YxauOgSjrXWGFiXtN5KBGKYeSGEiZxog17ALklVCtYHcMII10AkXyDE15n
0LeQxOFfukZn872mxbscGivAfxsCnvEgPbajSptDTGmKtBCW4qNC3cFUSIBD8JRd
x0RE8NxlQJ9voXnT3niLd2w0C3N3yKS38LXSAV3Qy/9DKVvBB/SA1leJ9cSlaS1N
vyLJV3ZIY406hyOF/kL3CHbHIcxImrlvl/r/49fG1mQrokGh7AwAH2r0Wii6euad
FmjzOOHGdnTXepQWXJASxXP+QBf2ILspsFD+Q7RVBraOikv31tfJ4WLzgWWpcuUq
dKTaegbZJ9YBO2IbMraUkJ36xq7yyDqwSnZ6iLS3F6YfBxgUxeHnSyhmhfngOETo
+eFVG/BcCxFbyEL3D0HtNI0Wz2dI4hfLZNF2zJmeyqMtAgMBAAE=
-----END PUBLIC KEY-----
""";

    public static bool IsConfigured(string publicKeyPem)
    {
        return !string.IsNullOrWhiteSpace(publicKeyPem)
            && publicKeyPem.Contains("BEGIN PUBLIC KEY", StringComparison.Ordinal)
            && !publicKeyPem.Contains("REPLACE_WITH_UPDATE_MANIFEST_PUBLIC_KEY", StringComparison.Ordinal);
    }
}

