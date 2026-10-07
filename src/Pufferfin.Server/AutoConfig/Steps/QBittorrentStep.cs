using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.AutoConfig.Steps;

/// <summary>
/// Creates the qBittorrent categories of Radarr, Sonarr, the Shelfmark audiobooks and the Shelfmark ebooks and, on the first setup only, turns on the automatic
/// torrent management, so each category lands in its own folder under the default save path.
/// </summary>
/// <remarks>
/// The first setup is the run that finds none of the Pufferfin categories. Later runs never touch the
/// preferences again: an administrator who turned the automatic management off keeps it off.
/// </remarks>
public sealed class QBittorrentStep : IAutoConfigStep
{
    private readonly QBittorrentClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="QBittorrentStep"/> class.
    /// </summary>
    /// <param name="client">The qBittorrent client.</param>
    public QBittorrentStep(QBittorrentClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public string Name => "qBittorrent categories";

    /// <inheritdoc />
    public async Task<AutoConfigStepOutcome> ApplyAsync(PufferfinSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.GetService(PufferfinService.QBittorrent) is not { } service)
        {
            return StepOutcomes.NotConfigured("qBittorrent");
        }

        var wanted = new List<string>();
        if (settings.IsEnabled(PufferfinService.Radarr))
        {
            wanted.Add(MediaLayout.RadarrCategory);
        }

        if (settings.IsEnabled(PufferfinService.Sonarr))
        {
            wanted.Add(MediaLayout.SonarrCategory);
        }

        // Shelfmark sends the audiobooks to one category (its QBITTORRENT_CATEGORY_AUDIOBOOK), the ebooks to another (its QBITTORRENT_CATEGORY).
        if (settings.IsEnabled(PufferfinService.Shelfmark))
        {
            wanted.Add(MediaLayout.AudiobooksCategory);
            wanted.Add(MediaLayout.BooksCategory);
        }

        if (wanted.Count == 0)
        {
            return new AutoConfigStepOutcome(AutoConfigStepStatus.Skipped, "Neither Radarr, Sonarr nor Shelfmark is configured.");
        }

        var created = new List<string>();
        try
        {
            var categories = await _client.GetCategoriesAsync(service, cancellationToken).ConfigureAwait(false);
            foreach (var category in wanted)
            {
                if (!categories.ContainsKey(category))
                {
                    await _client.CreateCategoryAsync(service, category, cancellationToken).ConfigureAwait(false);
                    created.Add("the category " + category);
                }
            }

            if (created.Count == wanted.Count)
            {
                var preferences = await _client.GetPreferencesAsync(service, cancellationToken).ConfigureAwait(false);
                if (preferences["auto_tmm_enabled"]?.GetValue<bool>() != true)
                {
                    await _client.SetPreferencesAsync(service, new JsonObject { ["auto_tmm_enabled"] = true }, cancellationToken).ConfigureAwait(false);
                    created.Add("the automatic torrent management");
                }
            }
        }
        catch (ArrApiException ex)
        {
            return StepOutcomes.Failed(ex, created);
        }

        return StepOutcomes.FromCreated(created);
    }
}
