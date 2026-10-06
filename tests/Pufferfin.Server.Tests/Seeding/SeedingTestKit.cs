using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Seeding;
using Pufferfin.Server.Storage;
using Pufferfin.Server.Tests.AutoConfig;
using Pufferfin.Server.Tests.TestHelpers;

namespace Pufferfin.Server.Tests.Seeding;

/// <summary>
/// qBittorrent, Radarr and Sonarr in memory, a configuration store, a clock and the seeding store on disk.
/// Every torrent carries a tracker URL and a magnet link with a passkey, so every test also proves it never leaks.
/// </summary>
public sealed class SeedingTestKit : IDisposable
{
    public const string Passkey = "PASSKEY-5e3d91c7a0";

    public static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly StoreTestContext _context = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pufferfin-tests", Guid.NewGuid().ToString("N"));

    public SeedingTestKit(params (string Key, object? Value)[] settings)
    {
        Directory.CreateDirectory(_directory);
        Server.Resources["torrents/info"] = [];
        Server.Resources["queue/details"] = [];
        Server.Resources["movie"] = [];
        Server.Resources["series"] = [];
        Server.Resources["episode"] = [];
        Server.Resources["history/movie"] = [];
        Server.Resources["history/series"] = [];
        Server.Overrides.Add((method, path, _) =>
        {
            if (method == HttpMethod.Get && path.StartsWith("torrents/files?hash=", StringComparison.Ordinal))
            {
                var hash = path["torrents/files?hash=".Length..];
                return FakeArrServer.Json(Files.TryGetValue(hash, out var files) ? files.DeepClone() : new JsonArray());
            }

            if (method == HttpMethod.Get && path.StartsWith("torrents/properties?hash=", StringComparison.Ordinal))
            {
                var hash = path["torrents/properties?hash=".Length..];
                var properties = new JsonObject { ["comment"] = "https://tracker.example/details?passkey=" + Passkey };
                if (Properties.TryGetValue(hash, out var isPrivate))
                {
                    properties["is_private"] = isPrivate;
                }

                return FakeArrServer.Json(properties);
            }

            if (method == HttpMethod.Post && path == "torrents/delete")
            {
                return QBittorrentDown ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
            }

            if (QBittorrentDown && path.StartsWith("torrents/", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            return null;
        });

        Configuration = _context.Start();
        var changes = new List<(string, object?)>
        {
            (SettingKeys.QBittorrentUrl, "http://qbittorrent:8080"),
            (SettingKeys.RadarrUrl, "http://radarr:7878"),
            (SettingKeys.RadarrApiKey, ArrFixtures.RadarrKey),
            (SettingKeys.SonarrUrl, "http://sonarr:8989"),
            (SettingKeys.SonarrApiKey, ArrFixtures.SonarrKey),
            (SettingKeys.SeedMinDays, 7),
        };
        changes.AddRange(settings);
        Configuration.Update(StoreTestContext.Changes(changes.ToArray()));

        QBittorrent = new QBittorrentClient(Server, Logs.CreateLogger<QBittorrentClient>(), TimeSpan.FromSeconds(5));
        Arr = new ArrClient(new ArrHttpClient(Server, Logs.CreateLogger<ArrHttpClient>(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.Zero));
        Restart();
    }

    public FakeArrServer Server { get; } = new();

    public CapturingLoggerProvider Logs => _context.Logs;

    public ManualTimeProvider Clock { get; } = new(Now);

    public PufferfinConfigurationStore Configuration { get; }

    public QBittorrentClient QBittorrent { get; }

    public ArrClient Arr { get; }

    public FakeFileLinks Links { get; } = new();

    public Dictionary<string, JsonArray> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool QBittorrentDown { get; set; }

    public SeedingStore Store { get; private set; } = null!;

    public SeedPolicyRunner Runner { get; private set; } = null!;

    public string SeedingFile => Path.Combine(_directory, "pufferfin-seeding.json");

    /// <summary>
    /// Gets the hashes qBittorrent was asked to remove, one entry per call.
    /// </summary>
    public IReadOnlyList<string[]> Deletions
        => Server.Calls
            .Where(c => c.Method == HttpMethod.Post && c.Path == "torrents/delete")
            .Select(c => System.Web.HttpUtility.ParseQueryString(c.Body!)["hashes"]!.Split('|'))
            .ToList();

    /// <summary>
    /// Simulates a restart: the seeding store and the runner are read again from the file.
    /// </summary>
    public void Restart()
    {
        Runner?.Dispose();
        Store = new SeedingStore(SeedingFile, Logs.CreateLogger<SeedingStore>());
        Runner = new SeedPolicyRunner(Configuration, QBittorrent, Arr, Store, Links, Clock, Logs.CreateLogger<SeedPolicyRunner>());
    }

    // Adds a torrent to qBittorrent, with a passkey in its tracker URL and magnet link.
    public JsonObject AddTorrent(
        string hash,
        string category = MediaLayout.RadarrCategory,
        bool? isPrivate = true,
        double ratio = 0,
        double seededDays = 0,
        double completedDaysAgo = -1,
        double progress = 1,
        string savePath = "/downloads/radarr")
    {
        var completed = completedDaysAgo < 0 ? seededDays : completedDaysAgo;
        var torrent = new JsonObject
        {
            ["hash"] = hash,
            ["name"] = "Release." + hash,
            ["category"] = category,
            ["progress"] = progress,
            ["ratio"] = ratio,
            ["seeding_time"] = (long)TimeSpan.FromDays(seededDays).TotalSeconds,
            ["completion_on"] = progress >= 1 ? (Clock.GetUtcNow() - TimeSpan.FromDays(completed)).ToUnixTimeSeconds() : -1,
            ["added_on"] = (Clock.GetUtcNow() - TimeSpan.FromDays(completed + 1)).ToUnixTimeSeconds(),
            ["size"] = 4_000_000_000L,
            ["state"] = progress >= 1 ? "uploading" : "downloading",
            ["save_path"] = savePath,
            ["content_path"] = savePath + "/Release." + hash,
            ["tracker"] = "https://tracker.example/announce?passkey=" + Passkey,
            ["magnet_uri"] = "magnet:?xt=urn:btih:" + hash + "&tr=https%3A%2F%2Ftracker.example%2Fannounce%3Fpasskey%3D" + Passkey
        };
        if (isPrivate is bool value)
        {
            torrent["private"] = value;
        }

        Server.Resource("torrents/info").Add(torrent);
        return torrent;
    }

    public void RemoveTorrent(string hash)
    {
        var torrents = Server.Resource("torrents/info");
        var torrent = torrents.OfType<JsonObject>().First(t => t["hash"]!.GetValue<string>() == hash);
        torrents.Remove(torrent);
    }

    public void Dispose()
    {
        Runner?.Dispose();
        _context.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// Inodes and hard links decided by the test.
    /// </summary>
    public sealed class FakeFileLinks : IFileLinks
    {
        public Dictionary<string, FileIdentity> Identities { get; } = new(StringComparer.Ordinal);

        public bool CanLink { get; set; } = true;

        public bool IsSupported => true;

        public FileIdentity? GetIdentity(string path) => Identities.TryGetValue(path.Replace('\\', '/'), out var identity) ? identity : null;

        public bool TryCreateHardLink(string existingFile, string newLink) => CanLink;
    }
}
