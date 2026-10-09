using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.AutoConfig.Steps;

/// <summary>
/// Declares Radarr and Sonarr as Prowlarr applications and, when <c>FLARESOLVERR_URL</c> is set, puts the indexers
/// that Cloudflare blocks behind FlareSolverr.
/// </summary>
/// <remarks>
/// An application of the same kind, a FlareSolverr proxy or a tag already on an indexer is never modified.
/// FlareSolverr applies through a tag: an indexer is tagged only after Prowlarr reported it failing and a test
/// confirmed that Cloudflare blocks it. At most <see cref="MaxIndexerTestsPerRun"/> indexers are tested per run,
/// so the step stays within its time limit; the next run continues.
/// </remarks>
public sealed class ProwlarrStep : IAutoConfigStep
{
    /// <summary>
    /// The label of the tag that routes an indexer through FlareSolverr.
    /// </summary>
    public const string FlareSolverrTag = "flaresolverr";

    /// <summary>
    /// The maximum number of failing indexers tested in one run.
    /// </summary>
    public const int MaxIndexerTestsPerRun = 3;

    /// <summary>
    /// The adult categories (XXX, XXX/Other) where trackers file adult anime, searched for anime only.
    /// </summary>
    private static readonly int[] AdultAnimeCategories = [6000, 6070];

    private readonly ProwlarrClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProwlarrStep"/> class.
    /// </summary>
    /// <param name="client">The Prowlarr client.</param>
    public ProwlarrStep(ProwlarrClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public string Name => "Prowlarr applications and FlareSolverr";

    /// <inheritdoc />
    public async Task<AutoConfigStepOutcome> ApplyAsync(PufferfinSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.GetService(PufferfinService.Prowlarr) is not { } prowlarr)
        {
            return StepOutcomes.NotConfigured("Prowlarr");
        }

        var created = new List<string>();
        try
        {
            var applications = await _client.GetItemsAsync(prowlarr, "applications", cancellationToken).ConfigureAwait(false);
            List<JsonObject>? schema = null;
            foreach (var service in new[] { PufferfinService.Radarr, PufferfinService.Sonarr })
            {
                var implementation = service.ToString();
                if (settings.GetService(service) is not { } target
                    || applications.Any(a => string.Equals(a["implementation"]?.GetValue<string>(), implementation, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                schema ??= await _client.GetSchemaAsync(prowlarr, "applications", cancellationToken).ConfigureAwait(false);
                if (ArrFields.Template(new JsonArray(schema.Select(s => (JsonNode)s.DeepClone()).ToArray()), implementation) is not { } application)
                {
                    continue;
                }

                application["name"] = implementation;
                application["syncLevel"] = "fullSync";
                ArrFields.Set(application, "prowlarrUrl", prowlarr.BaseUrl!.AbsoluteUri.TrimEnd('/'));
                ArrFields.Set(application, "baseUrl", target.BaseUrl!.AbsoluteUri.TrimEnd('/'));
                ArrFields.Set(application, "apiKey", target.ApiKey);
                await _client.CreateItemAsync(prowlarr, "applications", application, cancellationToken).ConfigureAwait(false);
                created.Add("the application " + implementation);
            }

            await WidenAnimeCategoriesAsync(prowlarr, created, cancellationToken).ConfigureAwait(false);

            if (settings.GetService(PufferfinService.FlareSolverr) is { BaseUrl: { } flareSolverrUrl })
            {
                await ApplyFlareSolverrAsync(prowlarr, flareSolverrUrl, created, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (ArrApiException ex)
        {
            return StepOutcomes.Failed(ex, created);
        }

        return StepOutcomes.FromCreated(created);
    }

    /// <summary>
    /// Lets Sonarr search anime on the trackers that have no anime category (YggReborn, The Pirate Bay): Sonarr searches
    /// an anime series in the anime categories of each indexer only, and Prowlarr gives an indexer the anime categories
    /// of the application it also has. Adding the series categories to them gives such trackers their series categories
    /// for anime too; adding the adult categories finds the adult anime, which trackers file there (owner's decision).
    /// Sonarr keeps only the releases whose title matches the series asked for. Categories are only added, never
    /// removed, then the indexers are synced again.
    /// </summary>
    private async Task WidenAnimeCategoriesAsync(ServiceSettings prowlarr, List<string> created, CancellationToken cancellationToken)
    {
        var applications = await _client.GetItemsAsync(prowlarr, "applications", cancellationToken).ConfigureAwait(false);
        foreach (var application in applications.Where(a => string.Equals(a["implementation"]?.GetValue<string>(), "Sonarr", StringComparison.OrdinalIgnoreCase)))
        {
            if (ArrFields.Get(application, "syncCategories") is not JsonArray series
                || ArrFields.Get(application, "animeSyncCategories") is not JsonArray anime
                || application["id"]?.GetValue<int>() is not int id)
            {
                continue;
            }

            var animeIds = anime.Select(c => c?.GetValue<int>()).OfType<int>().ToHashSet();
            var missing = series.Select(c => c?.GetValue<int>()).OfType<int>()
                .Concat(AdultAnimeCategories)
                .Distinct()
                .Where(c => !animeIds.Contains(c))
                .ToList();
            if (missing.Count == 0)
            {
                continue;
            }

            ArrFields.Set(application, "animeSyncCategories", new JsonArray(animeIds.Concat(missing).Order().Select(c => (JsonNode)c).ToArray()));
            await _client.UpdateItemAsync(prowlarr, "applications", id, application, cancellationToken).ConfigureAwait(false);
            await _client.CreateItemAsync(prowlarr, "command", new JsonObject { ["name"] = "ApplicationIndexerSync" }, cancellationToken).ConfigureAwait(false);
            created.Add("the series categories for the anime searches of " + (application["name"]?.GetValue<string>() ?? "Sonarr"));
        }
    }

    private async Task ApplyFlareSolverrAsync(ServiceSettings prowlarr, Uri flareSolverrUrl, List<string> created, CancellationToken cancellationToken)
    {
        var proxies = await _client.GetItemsAsync(prowlarr, "indexerProxy", cancellationToken).ConfigureAwait(false);
        var proxy = proxies.FirstOrDefault(p => string.Equals(p["implementation"]?.GetValue<string>(), "FlareSolverr", StringComparison.OrdinalIgnoreCase));
        int tagId;
        if (proxy is null)
        {
            var tags = await _client.GetItemsAsync(prowlarr, "tag", cancellationToken).ConfigureAwait(false);
            var tag = tags.FirstOrDefault(t => string.Equals(t["label"]?.GetValue<string>(), FlareSolverrTag, StringComparison.OrdinalIgnoreCase))
                ?? await _client.CreateItemAsync(prowlarr, "tag", new JsonObject { ["label"] = FlareSolverrTag }, cancellationToken).ConfigureAwait(false);
            tagId = tag["id"]!.GetValue<int>();

            var schema = await _client.GetSchemaAsync(prowlarr, "indexerProxy", cancellationToken).ConfigureAwait(false);
            if (ArrFields.Template(new JsonArray(schema.Select(s => (JsonNode)s.DeepClone()).ToArray()), "FlareSolverr") is not { } template)
            {
                return;
            }

            template["name"] = "FlareSolverr";
            template["tags"] = new JsonArray(tagId);
            ArrFields.Set(template, "host", flareSolverrUrl.AbsoluteUri);
            await _client.CreateItemAsync(prowlarr, "indexerProxy", template, cancellationToken).ConfigureAwait(false);
            created.Add("the FlareSolverr proxy");
        }
        else if ((proxy["tags"] as JsonArray)?.FirstOrDefault()?.GetValue<int>() is int existingTag)
        {
            tagId = existingTag;
        }
        else
        {
            // A proxy set up by hand without a tag: how it applies is the administrator's choice.
            return;
        }

        var failing = (await _client.GetItemsAsync(prowlarr, "indexerstatus", cancellationToken).ConfigureAwait(false))
            .Select(s => s["indexerId"]?.GetValue<int>())
            .OfType<int>()
            .ToHashSet();
        if (failing.Count == 0)
        {
            return;
        }

        var indexers = await _client.GetItemsAsync(prowlarr, "indexer", cancellationToken).ConfigureAwait(false);
        var candidates = indexers
            .Where(i => i["id"]?.GetValue<int>() is int id && failing.Contains(id))
            .Where(i => (i["tags"] as JsonArray)?.Any(t => t?.GetValue<int>() == tagId) != true)
            .Take(MaxIndexerTestsPerRun);
        foreach (var indexer in candidates)
        {
            try
            {
                await _client.TestIndexerAsync(prowlarr, indexer, cancellationToken).ConfigureAwait(false);
            }
            catch (ArrApiException ex) when (ex.RemoteMessages.Any(m => m.Contains("cloudflare", StringComparison.OrdinalIgnoreCase)))
            {
                var tags = indexer["tags"] as JsonArray ?? [];
                tags.Add(tagId);
                indexer["tags"] = tags;
                await _client.UpdateItemAsync(prowlarr, "indexer", indexer["id"]!.GetValue<int>(), indexer, cancellationToken).ConfigureAwait(false);
                created.Add("the FlareSolverr tag on the indexer " + indexer["name"]?.GetValue<string>());
            }
            catch (ArrApiException ex) when (ex.Failure == ArrFailure.Rejected)
            {
                // Failing for another reason: not a Cloudflare matter.
            }
        }
    }
}
