using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Dango.Desktop.AppShell.Persistence;

/// <summary>Derives independent local files from an explicitly selected settings file without creating directories.</summary>
public sealed class DangoUserDataPaths
{
    public DangoUserDataPaths(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        SettingsPath = Path.GetFullPath(settingsPath);
        RootDirectory = Path.GetDirectoryName(SettingsPath) ?? throw new ArgumentException("设置文件缺少目录。", nameof(settingsPath));
        AppearancePath = Path.Combine(RootDirectory, "appearance.json");
        LocalRoomSavePath = Path.Combine(RootDirectory, "local-rooms.json");
    }

    public string RootDirectory { get; }
    public string SettingsPath { get; }
    public string AppearancePath { get; }
    public string LocalRoomSavePath { get; }

    /// <summary>Separates archives by canonical HTTPS origin and stable identities without storing endpoint credentials.</summary>
    public string GetAccountDirectory(Uri endpoint, Guid userId, Guid petId)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) || string.IsNullOrEmpty(endpoint.Host) || endpoint.Port <= 0)
            throw new ArgumentException("账号存档需要无凭据的绝对 HTTPS 服务端地址。", nameof(endpoint));
        if (userId == Guid.Empty) throw new ArgumentException("账号标识不能为空。", nameof(userId));
        if (petId == Guid.Empty) throw new ArgumentException("团子标识不能为空。", nameof(petId));
        var origin = new UriBuilder(Uri.UriSchemeHttps, endpoint.IdnHost, endpoint.Port).Uri
            .GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped).ToLowerInvariant();
        var originHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(origin))).ToLowerInvariant();
        return Path.Combine(RootDirectory, "accounts", originHash, userId.ToString("N"), petId.ToString("N"));
    }
}
