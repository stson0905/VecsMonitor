using System.Security.Cryptography;
using HecsMonitor.Licensing;

namespace HecsMonitorLicenseGen;

/// <summary>
/// 서명 개인키(ECDSA P-256, PKCS#8 PEM) 관리.
/// 개인키 파일은 이 프로그램만 사용하며, HecsMonitor 설치 파일이나 소스에는 절대 넣지 않는다.
/// </summary>
public sealed class SigningKeyStore
{
    public ECDsa Key { get; private set; }
    public string KeyPath { get; private set; }
    public string Fingerprint { get; private set; }
    /// <summary>HecsMonitor에 들어 있는 공개키와 짝이 맞는지</summary>
    public bool MatchesApp { get; private set; }

    public static bool IsEncrypted(string path) =>
        File.ReadAllText(path).Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal);

    /// <summary>개인키를 불러온다. 비밀번호로 보호된 파일이면 password가 필요하다.</summary>
    public void Load(string path, string password = null)
    {
        string pem = File.ReadAllText(path);
        var ec = ECDsa.Create();
        if (pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal))
            ec.ImportFromEncryptedPem(pem, password ?? "");
        else
            ec.ImportFromPem(pem);
        if (ec.KeySize != 256) throw new CryptographicException("ECDSA P-256 개인키가 아닙니다.");

        Key?.Dispose();
        Key = ec;
        KeyPath = path;
        Fingerprint = SoftwareLicenseCore.Fingerprint(ec.ExportSubjectPublicKeyInfo());
        MatchesApp = Fingerprint == SoftwareLicenseCore.PublicKeyFingerprint;
        PublicKeyBase64 = Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo());
        IsEncryptedFile = pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal);
    }

    /// <summary>불러온 개인키의 공개키 (Base64)</summary>
    public string PublicKeyBase64 { get; private set; }
    public bool IsEncryptedFile { get; private set; }

    /// <summary>새 키 쌍을 만들어 개인키를 저장하고, 프로그램에 넣을 공개키(Base64)를 돌려준다.</summary>
    public static string CreateNew(string path, string password)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string pem = string.IsNullOrEmpty(password)
            ? ec.ExportPkcs8PrivateKeyPem()
            : ec.ExportEncryptedPkcs8PrivateKeyPem(password,
                new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, pem);
        return Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo());
    }

    public byte[] Sign(byte[] payload) => Key.SignData(payload, HashAlgorithmName.SHA256);

    /// <summary>
    /// 마지막으로 쓴 키 경로 → 없으면 설치 폴더(또는 개발 폴더 상위)의 keys\hecs-license-signing.pem을 찾는다.
    /// </summary>
    public static string FindDefaultKeyPath(string lastUsed)
    {
        if (!string.IsNullOrEmpty(lastUsed) && File.Exists(lastUsed)) return lastUsed;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "keys", "hecs-license-signing.pem");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
