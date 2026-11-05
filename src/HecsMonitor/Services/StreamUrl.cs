using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HecsMonitor.Services;

public static class StreamUrl
{
    /// <summary>
    /// rtsp://host/... 에 user:password@ 를 넣는다. 특수문자(@ : / # ? 등)는 퍼센트 인코딩하며
    /// FFmpeg가 인증 시 디코딩한다. URL에 이미 계정이 있거나 ID가 비어 있으면 그대로 둔다.
    /// </summary>
    public static string WithCredentials(string url, string user, string password)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrEmpty(user)) return url?.Trim();
        url = url.Trim();
        int scheme = url.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0 || url.StartsWith("demo://", StringComparison.OrdinalIgnoreCase)) return url;

        int hostStart = scheme + 3;
        int pathStart = url.IndexOf('/', hostStart);
        string authority = pathStart < 0 ? url[hostStart..] : url[hostStart..pathStart];
        if (authority.Contains('@')) return url;

        string cred = Uri.EscapeDataString(user) + (string.IsNullOrEmpty(password) ? "" : ":" + Uri.EscapeDataString(password));
        return url[..hostStart] + cred + "@" + url[hostStart..];
    }

    /// <summary>rtsp://user:pass@host 형태에서 계정을 분리한다 (URL에 계정을 붙여 넣은 경우 정리용).</summary>
    public static (string Url, string User, string Password) SplitCredentials(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return (url, null, null);
        int scheme = url.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0) return (url, null, null);
        int hostStart = scheme + 3;
        int pathStart = url.IndexOf('/', hostStart);
        string authority = pathStart < 0 ? url[hostStart..] : url[hostStart..pathStart];
        int at = authority.LastIndexOf('@');
        if (at < 0) return (url, null, null);
        string cred = authority[..at];
        int colon = cred.IndexOf(':');
        string user = Uri.UnescapeDataString(colon < 0 ? cred : cred[..colon]);
        string pass = colon < 0 ? "" : Uri.UnescapeDataString(cred[(colon + 1)..]);
        return (url[..hostStart] + url[(hostStart + at + 1)..], user, pass);
    }

    static readonly Regex UrlPassword = new(@"(://[^:/@\s]+:)[^@\s]+@", RegexOptions.Compiled);

    /// <summary>로그·오류 메시지 안의 URL 비밀번호를 가린다.</summary>
    public static string MaskSecrets(string text) =>
        string.IsNullOrEmpty(text) ? text : UrlPassword.Replace(text, "$1****@");
}

/// <summary>Windows DPAPI(현재 사용자)로 비밀번호를 암호화해 설정 파일에 저장한다.</summary>
public static class SecretProtector
{
    const string Prefix = "dpapi:";

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        try
        {
            var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(data);
        }
        catch { return ""; }
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;   // 직접 입력한 평문
        try
        {
            var data = ProtectedData.Unprotect(Convert.FromBase64String(stored[Prefix.Length..]), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch { return ""; }   // 다른 PC/사용자에서 복사한 설정
    }
}
