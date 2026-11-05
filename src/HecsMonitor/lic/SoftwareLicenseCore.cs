// 라이선스 공용 코어: HecsMonitor.exe(검증)와 HecsMonitorLicenseGen.exe(발급)가 함께 사용한다.
//
// 1) 서명: ECDSA P-256 + SHA-256 공개키 서명
//    - 개인키는 발급 프로그램만 가지며(프로그램 소스·설치 파일에 없음), 이 파일에는 공개키만 있다.
//    - 공개키만으로는 서명을 만들 수 없으므로 프로그램을 분석해도 키를 위조할 수 없다.
// 2) 파일: 내용(채널 수·생성일·장치 ID·사용자)과 서명을 AES-256-GCM으로 암호화해 Base64로 저장
//    - 암호화 키는 장치 ID(MAC)에서 만들므로, 텍스트 편집기로 열면 의미 없는 문자열만 보이고
//      해당 PC가 아니면 내용을 풀 수도 없다.
//
// license.key
//   -----BEGIN HECS MONITOR LICENSE-----
//   Base64( "HL" | 형식(1) | nonce(12) | tag(16) | 암호문 )
//   -----END HECS MONITOR LICENSE-----
//   암호문을 풀면: 내용 길이(2) | 내용 | ECDSA 서명(64, IEEE P1363)
using System.Buffers.Binary;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace HecsMonitor.Licensing;

/// <summary>라이선스 내용 (서명 대상)</summary>
public sealed record LicensePayload(int Channels, DateOnly IssuedDate, string MachineId, string Licensee, string LicenseId);

public static class SoftwareLicenseCore
{
    // 발급 프로그램의 개인키와 짝인 공개키 (SubjectPublicKeyInfo, Base64).
    // 서명 키를 새로 만들면 이 값을 바꾸고 다시 빌드해야 하며, 이전에 발급한 키는 모두 무효가 된다.
    public const string PublicKeyBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEnA4LojDhqa/NKN1x0Fq/59sStN07rnPXW5naXnNSsQHlzfpcTPGqIcx8JLEhYBnWSND0ROsgVqQZI2EMa93rqQ==";

    // 파일 암호화용 텍스트 (보안은 서명이 담당하고, 이 값은 내용을 감추는 용도)
    const string EnvelopeSecret = "SOLDIA_PUBLIC_SECURITY";

    public const string Header = "-----BEGIN HECS MONITOR LICENSE-----";
    public const string Footer = "-----END HECS MONITOR LICENSE-----";
    public const int MaxKeyChannels = 999;

    const byte FormatVersion = 1;
    static readonly byte[] Magic = "HL"u8.ToArray();
    static readonly byte[] Aad = "HECS-MONITOR-LICENSE-v1"u8.ToArray();
    const int NonceSize = 12, TagSize = 16, SignatureSize = 64;

    // ---------- 장치 ID ----------

    /// <summary>16진수 문자만 남겨 대문자로. 12자리(MAC 6바이트)가 아니면 false.</summary>
    public static bool NormalizeMachineId(string input, out string normalized)
    {
        var sb = new StringBuilder(12);
        foreach (char c in input ?? "")
            if (Uri.IsHexDigit(c)) sb.Append(char.ToUpperInvariant(c));
        normalized = sb.ToString();
        return normalized.Length == 12;
    }

    /// <summary>표시용: 001122AABBCC → 00-11-22-AA-BB-CC</summary>
    public static string FormatMachineId(string machineId) =>
        NormalizeMachineId(machineId, out var mac)
            ? string.Join("-", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)))
            : machineId ?? "";

    /// <summary>이 PC의 유선 이더넷 MAC 목록 (시스템이 알려 주는 순서). 연결된(Up) 것이 앞.</summary>
    public static IReadOnlyList<string> GetWiredMacs()
    {
        var up = new List<string>();
        var down = new List<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet) continue;   // IF_TYPE_ETHERNET_CSMACD
            byte[] addr = nic.GetPhysicalAddress().GetAddressBytes();
            if (addr.Length != 6) continue;
            (nic.OperationalStatus == OperationalStatus.Up ? up : down).Add(Convert.ToHexString(addr));
        }
        return up.Concat(down).Distinct().ToList();
    }

    /// <summary>라이선스 발급 요청에 쓰는 이 PC의 장치 ID: 연결된 첫 유선 MAC, 없으면 첫 유선 MAC.</summary>
    public static string GetMachineIdForActivation() => GetWiredMacs().FirstOrDefault();

    // ---------- 내용 ----------

    public static string NewLicenseId()
    {
        string hex = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        return $"HL-{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..]}";
    }

    public static byte[] SerializePayload(LicensePayload p)
    {
        if (!NormalizeMachineId(p.MachineId, out var mac)) throw new ArgumentException("장치 ID가 올바르지 않습니다.");
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write(FormatVersion);
        w.Write((ushort)p.Channels);
        w.Write(p.IssuedDate.DayNumber);
        w.Write(Convert.FromHexString(mac));
        w.Write(p.Licensee ?? "");
        w.Write(p.LicenseId ?? "");
        w.Flush();
        return ms.ToArray();
    }

    public static LicensePayload DeserializePayload(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        if (r.ReadByte() != FormatVersion) throw new InvalidDataException("지원하지 않는 라이선스 형식입니다.");
        int channels = r.ReadUInt16();
        var date = DateOnly.FromDayNumber(r.ReadInt32());
        string mac = Convert.ToHexString(r.ReadBytes(6));
        string licensee = r.ReadString();
        string id = r.ReadString();
        return new LicensePayload(channels, date, mac, licensee, id);
    }

    // ---------- 서명 ----------

    static readonly Lazy<ECDsa> PublicKey = new(() =>
    {
        var ec = ECDsa.Create();
        ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeyBase64), out _);
        return ec;
    });

    public static bool VerifySignature(byte[] payload, byte[] signature) =>
        signature?.Length == SignatureSize && PublicKey.Value.VerifyData(payload, signature, HashAlgorithmName.SHA256);

    /// <summary>공개키 지문 (발급 프로그램에서 개인키 짝이 맞는지 확인용)</summary>
    public static string PublicKeyFingerprint => Fingerprint(Convert.FromBase64String(PublicKeyBase64));

    public static string Fingerprint(byte[] subjectPublicKeyInfo)
    {
        string hex = Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo))[..16];
        return string.Join(":", Enumerable.Range(0, 8).Select(i => hex.Substring(i * 2, 2)));
    }

    // ---------- 파일 (암호화 봉투) ----------

    static byte[] EnvelopeKey(string mac) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"HECS-LICENSE-ENVELOPE|{EnvelopeSecret}|{mac}"));

    /// <summary>내용 + 서명을 장치 ID로 암호화해 license.key 텍스트로 만든다.</summary>
    public static string Seal(byte[] payload, byte[] signature, string machineId)
    {
        if (!NormalizeMachineId(machineId, out var mac)) throw new ArgumentException("장치 ID가 올바르지 않습니다.");
        var plain = new byte[2 + payload.Length + signature.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(plain, (ushort)payload.Length);
        payload.CopyTo(plain, 2);
        signature.CopyTo(plain, 2 + payload.Length);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plain.Length];
        using (var aes = new AesGcm(EnvelopeKey(mac), TagSize))
            aes.Encrypt(nonce, plain, cipher, tag, Aad);

        byte[] blob = [.. Magic, FormatVersion, .. nonce, .. tag, .. cipher];
        var sb = new StringBuilder();
        sb.AppendLine(Header);
        string b64 = Convert.ToBase64String(blob);
        for (int i = 0; i < b64.Length; i += 64) sb.AppendLine(b64.Substring(i, Math.Min(64, b64.Length - i)));
        sb.AppendLine(Footer);
        return sb.ToString();
    }

    /// <summary>license.key 텍스트를 해당 장치 ID로 풀어 내용과 서명을 꺼낸다. 다른 장치면 false.</summary>
    public static bool TryOpen(string text, string machineId, out byte[] payload, out byte[] signature)
    {
        payload = signature = null;
        if (!NormalizeMachineId(machineId, out var mac)) return false;
        try
        {
            string b64 = string.Concat((text ?? "").Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("-----")));
            byte[] blob = Convert.FromBase64String(b64);
            int head = Magic.Length + 1;
            if (blob.Length < head + NonceSize + TagSize + 2 + SignatureSize ||
                blob[0] != Magic[0] || blob[1] != Magic[1] || blob[2] != FormatVersion) return false;

            var nonce = blob.AsSpan(head, NonceSize);
            var tag = blob.AsSpan(head + NonceSize, TagSize);
            var cipher = blob.AsSpan(head + NonceSize + TagSize);
            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(EnvelopeKey(mac), TagSize))
                aes.Decrypt(nonce, cipher, tag, plain, Aad);   // 다른 장치·변조면 예외

            int len = BinaryPrimitives.ReadUInt16LittleEndian(plain);
            if (2 + len + SignatureSize != plain.Length) return false;
            payload = plain.AsSpan(2, len).ToArray();
            signature = plain.AsSpan(2 + len).ToArray();
            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// 장치 ID 후보들로 license.key를 열고 서명·장치를 확인한다.
    /// 성공하면 내용을 돌려주고, 실패하면 사유를 error에 담는다.
    /// </summary>
    public static LicensePayload Read(string text, IEnumerable<string> machineIds, out string error)
    {
        error = null;
        bool opened = false;
        foreach (var id in machineIds)
        {
            if (!TryOpen(text, id, out var payload, out var sig)) continue;
            opened = true;
            if (!VerifySignature(payload, sig)) { error = "라이선스 서명이 올바르지 않습니다."; continue; }
            LicensePayload p;
            try { p = DeserializePayload(payload); }
            catch (Exception ex) { error = ex.Message; continue; }
            NormalizeMachineId(id, out var mac);
            if (p.MachineId != mac) { error = "라이선스의 장치 ID가 이 PC와 다릅니다."; continue; }
            if (p.Channels <= 0) { error = "라이선스 채널 수가 올바르지 않습니다."; continue; }
            return p;
        }
        error ??= opened ? "라이선스를 확인할 수 없습니다." : "이 PC에 발급된 라이선스 키가 아니거나 파일이 손상되었습니다.";
        return null;
    }
}
