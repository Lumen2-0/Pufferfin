using System.Text.Json.Nodes;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Tests.TestHelpers;

namespace Pufferfin.Server.Tests.AutoConfig;

/// <summary>
/// Settings and service answers shared by the auto-configuration tests, shaped like the real *arr payloads.
/// </summary>
public static class ArrFixtures
{
    public const string RadarrKey = "radarr-secret-key-0001";
    public const string SonarrKey = "sonarr-secret-key-0002";
    public const string ProwlarrKey = "prowlarr-secret-key-0003";
    public const string BazarrKey = "bazarr-secret-key-0004";
    public const string QBittorrentPassword = "qbit-secret-password";

    public static PufferfinSettings Settings(params (string Key, object? Value)[] changes)
    {
        using var context = new StoreTestContext();
        var store = context.Start();
        store.Update(StoreTestContext.Changes(changes));
        return store.Current.Settings;
    }

    public static PufferfinSettings FullStack(string languages = "fr,en")
        => Settings(
            (SettingKeys.RadarrUrl, "http://radarr:7878"),
            (SettingKeys.RadarrApiKey, RadarrKey),
            (SettingKeys.SonarrUrl, "http://sonarr:8989"),
            (SettingKeys.SonarrApiKey, SonarrKey),
            (SettingKeys.ProwlarrUrl, "http://prowlarr:9696"),
            (SettingKeys.ProwlarrApiKey, ProwlarrKey),
            (SettingKeys.BazarrUrl, "http://bazarr:6767"),
            (SettingKeys.BazarrApiKey, BazarrKey),
            (SettingKeys.QBittorrentUrl, "http://qbittorrent:8080"),
            (SettingKeys.QBittorrentUsername, "admin"),
            (SettingKeys.QBittorrentPassword, QBittorrentPassword),
            (SettingKeys.FlareSolverrUrl, "http://flaresolverr:8191"),
            (SettingKeys.PreferredAudioLanguages, languages));

    public static JsonArray Languages() => (JsonArray)JsonNode.Parse("""
        [{"id":1,"name":"English"},{"id":2,"name":"French"},{"id":4,"name":"German"},{"id":-2,"name":"Original"},{"id":-1,"name":"Any"}]
        """)!;

    public static JsonArray CustomFormatSchema() => (JsonArray)JsonNode.Parse("""
        [
          {"implementation":"LanguageSpecification","implementationName":"Language","negate":false,"required":false,
           "fields":[{"order":0,"name":"value","label":"Language","value":0,"type":"select"},{"order":1,"name":"exceptLanguage","value":false,"type":"checkbox"}]},
          {"implementation":"ReleaseTitleSpecification","implementationName":"Release Title","negate":false,"required":false,
           "fields":[{"order":0,"name":"value","label":"Regular Expression","value":null,"type":"textbox"}]}
        ]
        """)!;

    public static JsonObject QualityProfileSchema() => (JsonObject)JsonNode.Parse("""
        {
          "name":"","upgradeAllowed":true,"cutoff":7,"minFormatScore":0,"cutoffFormatScore":0,"formatItems":[],
          "language":{"id":1,"name":"English"},
          "items":[
            {"quality":{"id":0,"name":"Unknown","resolution":0},"items":[],"allowed":false},
            {"quality":{"id":1,"name":"SDTV","resolution":480},"items":[],"allowed":false},
            {"quality":{"id":6,"name":"Bluray-720p","resolution":720},"items":[],"allowed":true},
            {"name":"WEB 1080p","id":1000,"allowed":true,"items":[
              {"quality":{"id":3,"name":"WEBDL-1080p","resolution":1080},"items":[],"allowed":true},
              {"quality":{"id":15,"name":"WEBRip-1080p","resolution":1080},"items":[],"allowed":true}]},
            {"quality":{"id":7,"name":"Bluray-1080p","resolution":1080},"items":[],"allowed":true},
            {"quality":{"id":30,"name":"Remux-1080p","resolution":1080},"items":[],"allowed":false},
            {"quality":{"id":19,"name":"Bluray-2160p","resolution":2160},"items":[],"allowed":false},
            {"quality":{"id":31,"name":"Remux-2160p","resolution":2160},"items":[],"allowed":false}
          ]
        }
        """)!;

    public static JsonArray DownloadClientSchema() => (JsonArray)JsonNode.Parse("""
        [
          {"implementation":"Transmission","name":"","fields":[{"name":"host","value":"localhost"}]},
          {"implementation":"QBittorrent","name":"","enable":false,"protocol":"torrent","priority":1,"fields":[
            {"name":"host","value":"localhost"},{"name":"port","value":8080},{"name":"useSsl","value":false},{"name":"urlBase","value":""},
            {"name":"username","value":""},{"name":"password","value":""},{"name":"movieCategory","value":"radarr"},{"name":"tvCategory","value":"tv-sonarr"}]}
        ]
        """)!;

    public static FakeArrServer RadarrLike()
    {
        var server = new FakeArrServer();
        server.Resources["language"] = Languages();
        server.Resources["customformat"] = [];
        server.Resources["qualityprofile"] = [];
        server.Resources["rootfolder"] = [];
        server.Resources["downloadclient"] = [];
        server.Schemas["customformat"] = CustomFormatSchema();
        server.Schemas["qualityprofile"] = QualityProfileSchema();
        server.Schemas["downloadclient"] = DownloadClientSchema();
        return server;
    }
}
