// HecsMonitor 전용: lic\license.key 검증
namespace HecsMonitor.Licensing;

/// <summary>검증에 성공한 라이선스.</summary>
public sealed record LicenseInfo(int Channels, DateOnly IssuedDate, string MachineId, string Licensee, string LicenseId);

public static class SoftwareLicense
{
    /// <summary>키 파일 폴더: 프로그램 폴더의 lic\ (설치 시 C:\Program Files\HECS Monitor\lic)</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "lic");
    public static string KeyPath => Path.Combine(Folder, "license.key");

    /// <summary>현재 적용된 라이선스. 검증 전이거나 실패하면 null.</summary>
    public static LicenseInfo Current { get; private set; }

    /// <summary>실패 사유 (화면 표시용)</summary>
    public static string Error { get; private set; }

    /// <summary>
    /// license.key를 이 PC의 유선 랜카드 MAC으로 풀고 공개키로 서명을 확인한다.
    /// 발급 기준은 '연결된 첫 유선 MAC'이지만, 이 PC의 모든 유선 MAC을 시도해
    /// 케이블 분리·랜카드 순서 변경으로 기준 MAC이 바뀌어도 같은 PC면 통과시킨다.
    /// </summary>
    public static bool Validate()
    {
        Current = null;
        Error = null;

        if (!File.Exists(KeyPath))
            return Fail("라이선스 키 파일이 없습니다.");

        string text;
        try { text = File.ReadAllText(KeyPath); }
        catch (Exception ex) { return Fail("라이선스 키 파일을 읽을 수 없습니다. " + ex.Message); }

        var macs = SoftwareLicenseCore.GetWiredMacs();
        if (macs.Count == 0)
            return Fail("이 PC에서 유선 네트워크 어댑터를 찾을 수 없습니다.");

        var p = SoftwareLicenseCore.Read(text, macs, out var error);
        if (p == null) return Fail(error);

        Current = new LicenseInfo(p.Channels, p.IssuedDate, p.MachineId, p.Licensee, p.LicenseId);
        return true;
    }

    static bool Fail(string message)
    {
        Error = message;
        return false;
    }
}
