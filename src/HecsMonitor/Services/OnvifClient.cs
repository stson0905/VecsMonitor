using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace HecsMonitor.Services;

public sealed record OnvifStream(string ProfileName, string Encoding, int Width, int Height, string Uri);

/// <summary>
/// ONVIF 장치에서 RTSP 주소를 조회한다 (GetCapabilities → GetProfiles → GetStreamUri).
/// 인증은 WS-Security UsernameToken(PasswordDigest)과 HTTP Digest를 함께 사용한다.
/// 카메라 시계가 PC와 다르면 인증이 거부되므로 GetSystemDateAndTime으로 시차를 보정한다.
/// </summary>
public static class OnvifClient
{
    const string NsDevice = "http://www.onvif.org/ver10/device/wsdl";
    const string NsMedia = "http://www.onvif.org/ver10/media/wsdl";
    const string NsSchema = "http://www.onvif.org/ver10/schema";

    public static async Task<List<OnvifStream>> GetStreamsAsync(string deviceXAddr, string user, string password)
    {
        using var handler = new HttpClientHandler
        {
            Credentials = string.IsNullOrEmpty(user) ? null : new NetworkCredential(user, password ?? ""),
            PreAuthenticate = false,
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };

        TimeSpan offset = await GetClockOffsetAsync(http, deviceXAddr);

        // 1) Media 서비스 주소 (인증 외의 이유로 실패하면 표준 기본 경로 사용)
        string mediaAddr = null;
        try
        {
            var caps = await CallAsync(http, deviceXAddr, user, password, offset,
                $"<GetCapabilities xmlns=\"{NsDevice}\"><Category>Media</Category></GetCapabilities>",
                NsDevice + "/GetCapabilities");
            mediaAddr = caps.Descendants().FirstOrDefault(e => e.Name.LocalName == "Media")?
                            .Descendants().FirstOrDefault(e => e.Name.LocalName == "XAddr")?.Value?.Trim();
        }
        catch (UnauthorizedAccessException) { throw; }
        catch { }
        if (string.IsNullOrEmpty(mediaAddr) && Uri.TryCreate(deviceXAddr, UriKind.Absolute, out var dev))
            mediaAddr = $"{dev.Scheme}://{dev.Authority}/onvif/media_service";
        mediaAddr = FixHost(mediaAddr ?? deviceXAddr, deviceXAddr);

        // 2) 프로필 목록
        var profilesDoc = await CallAsync(http, mediaAddr, user, password, offset, $"<GetProfiles xmlns=\"{NsMedia}\"/>",
            NsMedia + "/GetProfiles");
        var profiles = profilesDoc.Descendants().Where(e => e.Name.LocalName == "Profiles").ToList();

        // 3) 프로필별 RTSP 주소
        var result = new List<OnvifStream>();
        foreach (var p in profiles)
        {
            string token = p.Attribute("token")?.Value;
            if (string.IsNullOrEmpty(token)) continue;
            var venc = p.Descendants().FirstOrDefault(e => e.Name.LocalName == "VideoEncoderConfiguration");
            if (venc == null) continue;   // 오디오/메타데이터 전용 프로필 제외
            string enc = venc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Encoding")?.Value ?? "";
            int.TryParse(venc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Width")?.Value, out int w);
            int.TryParse(venc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Height")?.Value, out int h);
            string name = p.Descendants().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value ?? token;

            var uriDoc = await CallAsync(http, mediaAddr, user, password, offset,
                $"<GetStreamUri xmlns=\"{NsMedia}\"><StreamSetup><Stream xmlns=\"{NsSchema}\">RTP-Unicast</Stream>" +
                $"<Transport xmlns=\"{NsSchema}\"><Protocol>RTSP</Protocol></Transport></StreamSetup>" +
                $"<ProfileToken>{SecurityElementEscape(token)}</ProfileToken></GetStreamUri>",
                NsMedia + "/GetStreamUri");
            string uri = uriDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Uri")?.Value?.Trim();
            if (!string.IsNullOrEmpty(uri)) result.Add(new OnvifStream(name, enc, w, h, FixHost(uri, deviceXAddr)));
        }
        // 해상도가 큰 스트림(메인 스트림)을 먼저
        return result.OrderByDescending(s => s.Width * s.Height).ToList();
    }

    /// <summary>NAT/다중 NIC 환경에서 카메라가 내부 IP나 0.0.0.0을 돌려주는 경우 검색된 IP로 바꾼다.</summary>
    static string FixHost(string url, string deviceXAddr)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !Uri.TryCreate(deviceXAddr, UriKind.Absolute, out var d)) return url;
        if (u.Host == d.Host) return url;
        if (u.Host is "0.0.0.0" or "127.0.0.1" or "localhost" || u.HostNameType != UriHostNameType.IPv4)
            return new UriBuilder(u) { Host = d.Host }.Uri.ToString();
        return url;
    }

    static async Task<TimeSpan> GetClockOffsetAsync(HttpClient http, string addr)
    {
        try
        {
            var doc = await PostAsync(http, addr, Envelope("", $"<GetSystemDateAndTime xmlns=\"{NsDevice}\"/>"),
                NsDevice + "/GetSystemDateAndTime");
            var utc = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "UTCDateTime");
            if (utc == null) return TimeSpan.Zero;
            int V(string n) => int.Parse(utc.Descendants().First(e => e.Name.LocalName == n).Value);
            var cam = new DateTime(V("Year"), V("Month"), V("Day"), V("Hour"), V("Minute"), V("Second"), DateTimeKind.Utc);
            return cam - DateTime.UtcNow;
        }
        catch { return TimeSpan.Zero; }
    }

    static Task<XDocument> CallAsync(HttpClient http, string addr, string user, string password, TimeSpan offset, string body, string action) =>
        PostAsync(http, addr, Envelope(SecurityHeader(user, password, offset), body), action);

    static async Task<XDocument> PostAsync(HttpClient http, string addr, string envelope, string action)
    {
        using var content = new StringContent(envelope, Encoding.UTF8, "application/soap+xml");
        // SOAP 1.2: action은 Content-Type 파라미터로 전달 (일부 카메라는 없으면 거부)
        content.Headers.ContentType!.Parameters.Add(new System.Net.Http.Headers.NameValueHeaderValue("action", $"\"{action}\""));
        using var resp = await http.PostAsync(addr, content);
        string text = await resp.Content.ReadAsStringAsync();
        XDocument doc = null;
        try { doc = XDocument.Parse(text); } catch { }

        if (!resp.IsSuccessStatusCode || doc?.Descendants().Any(e => e.Name.LocalName == "Fault") == true)
        {
            string reason = doc?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Text")?.Value
                            ?? doc?.Descendants().LastOrDefault(e => e.Name.LocalName == "Value")?.Value;
            if (resp.StatusCode == HttpStatusCode.Unauthorized || (reason ?? "").Contains("NotAuthorized", StringComparison.OrdinalIgnoreCase)
                || (reason ?? "").Contains("Sender not Authorized", StringComparison.OrdinalIgnoreCase)
                || (reason ?? "").Contains("could not be authenticated", StringComparison.OrdinalIgnoreCase)
                || (reason ?? "").Contains("FailedAuthentication", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("ONVIF 인증 실패 · ID/비밀번호를 확인하세요");
            throw new InvalidOperationException($"ONVIF 오류 ({(int)resp.StatusCode}) {reason}".Trim());
        }
        return doc ?? throw new InvalidOperationException("ONVIF 응답을 해석할 수 없습니다");
    }

    static string Envelope(string header, string body) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\">" +
        $"<s:Header>{header}</s:Header><s:Body>{body}</s:Body></s:Envelope>";

    static string SecurityHeader(string user, string password, TimeSpan offset)
    {
        if (string.IsNullOrEmpty(user)) return "";
        byte[] nonce = RandomNumberGenerator.GetBytes(16);
        string created = (DateTime.UtcNow + offset).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        byte[] digestInput = nonce.Concat(Encoding.UTF8.GetBytes(created + (password ?? ""))).ToArray();
        string digest = Convert.ToBase64String(SHA1.HashData(digestInput));
        return
            "<Security s:mustUnderstand=\"1\" xmlns=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd\">" +
            "<UsernameToken>" +
            $"<Username>{SecurityElementEscape(user)}</Username>" +
            "<Password Type=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest\">" + digest + "</Password>" +
            "<Nonce EncodingType=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary\">" + Convert.ToBase64String(nonce) + "</Nonce>" +
            "<Created xmlns=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd\">" + created + "</Created>" +
            "</UsernameToken></Security>";
    }

    static string SecurityElementEscape(string s) => System.Security.SecurityElement.Escape(s ?? "");
}
