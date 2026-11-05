using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace HecsMonitor.Services;

public sealed record OnvifDevice(string Name, string Model, string Manufacturer, string Ip, string XAddr)
{
    /// <summary>ONVIF 장치의 일반적인 RTSP 주소. 실제 경로는 카메라마다 다르므로 사용자가 보완한다.</summary>
    public string SuggestedRtsp => $"rtsp://{Ip}:554/";
}

/// <summary>WS-Discovery(UDP 239.255.255.250:3702)로 LAN의 ONVIF 카메라를 찾는다.</summary>
public static class OnvifDiscovery
{
    static readonly IPEndPoint Multicast = new(IPAddress.Parse("239.255.255.250"), 3702);

    public static async Task<List<OnvifDevice>> DiscoverAsync(int timeoutMs = 3000)
    {
        string probe = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope"
                        xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing"
                        xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery"
                        xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
              <e:Header>
                <w:MessageID>uuid:{Guid.NewGuid()}</w:MessageID>
                <w:To e:mustUnderstand="true">urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>
                <w:Action e:mustUnderstand="true">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action>
              </e:Header>
              <e:Body><d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe></e:Body>
            </e:Envelope>
            """;
        var bytes = Encoding.UTF8.GetBytes(probe);
        var found = new Dictionary<string, OnvifDevice>();
        using var cts = new CancellationTokenSource(timeoutMs);

        // NIC가 여러 개인 PC에서도 찾을 수 있도록 각 IPv4 인터페이스에서 보낸다
        var tasks = LocalAddresses().Select(async local =>
        {
            try
            {
                using var udp = new UdpClient(new IPEndPoint(local, 0));
                await udp.SendAsync(bytes, bytes.Length, Multicast);
                while (!cts.IsCancellationRequested)
                {
                    var r = await udp.ReceiveAsync(cts.Token);
                    var dev = Parse(Encoding.UTF8.GetString(r.Buffer), r.RemoteEndPoint.Address.ToString());
                    if (dev != null) lock (found) found.TryAdd(dev.Ip, dev);
                }
            }
            catch { }
        });
        await Task.WhenAll(tasks);
        return found.Values.OrderBy(d => IpKey(d.Ip)).ThenBy(d => d.Ip, StringComparer.Ordinal).ToList();
    }

    /// <summary>IPv4는 숫자 순서로 정렬 (192.168.30.69 &lt; 192.168.30.105), 호스트명은 뒤로.</summary>
    static ulong IpKey(string ip) =>
        IPAddress.TryParse(ip, out var a) && a.AddressFamily == AddressFamily.InterNetwork
            ? (ulong)a.GetAddressBytes().Aggregate(0L, (acc, b) => acc << 8 | b)
            : ulong.MaxValue;

    static IEnumerable<IPAddress> LocalAddresses()
    {
        var list = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .ToList();
        return list.Count > 0 ? list : new[] { IPAddress.Any };
    }

    static OnvifDevice Parse(string xml, string remoteIp)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            string xaddrs = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "XAddrs")?.Value ?? "";
            string scopes = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Scopes")?.Value ?? "";
            string xaddr = xaddrs.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

            string Scope(params string[] keys)
            {
                foreach (var s in scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    foreach (var k in keys)
                    {
                        var marker = $"onvif://www.onvif.org/{k}/";
                        if (s.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                            return Uri.UnescapeDataString(s[marker.Length..]);
                    }
                return "";
            }

            string ip = Uri.TryCreate(xaddr, UriKind.Absolute, out var u) ? u.Host : remoteIp;
            return new OnvifDevice(Scope("name"), Scope("hardware"), Scope("mfr", "manufacturer"), ip, xaddr);
        }
        catch { return null; }
    }
}
