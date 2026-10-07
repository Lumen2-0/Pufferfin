using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Pufferfin.Catalog.Library;
using Pufferfin.Catalog.Models;
using Pufferfin.Catalog.Tmdb;

namespace Pufferfin.Catalog.Search;

/// <summary>
/// Merges local, TMDB and Audible results into one list where a title present on disk and found remotely is a single entry.
/// </summary>
/// <remarks>
/// A local item matches a remote entry by external id first (TMDB id, ASIN). A local item without that id falls back to
/// its normalized title and year (one year of tolerance), or title and author for an audiobook or a book. A local item that carries
/// a different id is never merged by title: it is another work with the same name.
/// </remarks>
public static class CatalogMerger
{
    /// <summary>
    /// Merges the results of the sources.
    /// </summary>
    /// <param name="local">The local results, by relevance.</param>
    /// <param name="tmdb">The TMDB results, by relevance.</param>
    /// <param name="audible">The Audible results, or the Shelfmark book results, by relevance: each matches a local item of its own kind.</param>
    /// <param name="limit">The maximum number of entries.</param>
    /// <returns>The merged entries, by relevance.</returns>
    public static IReadOnlyList<CatalogItem> Merge(
        IReadOnlyList<LocalCatalogEntry> local,
        IReadOnlyList<CatalogItem> tmdb,
        IReadOnlyList<CatalogItem> audible,
        int limit)
    {
        var localRank = new Dictionary<Guid, double>();
        var byTmdbId = new Dictionary<(CatalogItemKind Kind, int Id), LocalCatalogEntry>();
        var byAsin = new Dictionary<string, LocalCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < local.Count; i++)
        {
            var entry = local[i];
            if (!localRank.TryAdd(entry.ItemId, Rank(i, local.Count)))
            {
                continue;
            }

            if (entry.TmdbId is int tmdbId)
            {
                byTmdbId.TryAdd((entry.Kind, tmdbId), entry);
            }

            if (!string.IsNullOrEmpty(entry.Asin))
            {
                byAsin.TryAdd(entry.Asin, entry);
            }
        }

        var consumed = new HashSet<Guid>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<(CatalogItem Item, double Rank, int Order)>();

        void Add(CatalogItem item, double rank)
        {
            if (keys.Add(item.Key))
            {
                merged.Add((item, rank, merged.Count));
            }
        }

        LocalCatalogEntry? Claim(LocalCatalogEntry? candidate)
            => candidate is not null && consumed.Add(candidate.ItemId) ? candidate : null;

        for (var i = 0; i < tmdb.Count; i++)
        {
            var item = tmdb[i];
            var match = item.TmdbId is int id && byTmdbId.TryGetValue((item.Kind, id), out var byId)
                ? Claim(byId)
                : Claim(local.FirstOrDefault(l => l.TmdbId is null && !consumed.Contains(l.ItemId) && TitleMatches(l, item)));
            var rank = Rank(i, tmdb.Count);
            Add(match is null ? item : MergeLocal(item, match), match is null ? rank : Math.Min(rank, localRank[match.ItemId]));
        }

        for (var i = 0; i < audible.Count; i++)
        {
            var item = audible[i];
            var match = item.Asin is not null && byAsin.TryGetValue(item.Asin, out var byId)
                ? Claim(byId)
                : Claim(local.FirstOrDefault(l => l.Kind == item.Kind && l.Asin is null && !consumed.Contains(l.ItemId) && AudiobookMatches(l, item)));
            var rank = Rank(i, audible.Count);
            Add(match is null ? item : MergeLocal(item, match), match is null ? rank : Math.Min(rank, localRank[match.ItemId]));
        }

        foreach (var entry in local)
        {
            if (consumed.Add(entry.ItemId))
            {
                Add(FromLocal(entry), localRank[entry.ItemId]);
            }
        }

        return merged
            .OrderBy(m => m.Rank)
            .ThenBy(m => m.Item.Availability == CatalogAvailability.OnDisk ? 0 : 1)
            .ThenBy(m => m.Order)
            .Take(limit)
            .Select(m => m.Item)
            .ToList();
    }

    /// <summary>
    /// Normalizes a title for comparison: lower case, no diacritics, letters and digits only.
    /// </summary>
    /// <param name="value">The title.</param>
    /// <returns>The normalized title.</returns>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var lastWasSpace = true;
        foreach (var c in value.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static double Rank(int index, int count) => count <= 1 ? 0 : (double)index / count;

    private static bool TitleMatches(LocalCatalogEntry local, CatalogItem item)
    {
        if (local.Kind != item.Kind)
        {
            return false;
        }

        if (local.Year is int localYear && item.Year is int itemYear && Math.Abs(localYear - itemYear) > 1)
        {
            return false;
        }

        var localTitles = new[] { Normalize(local.Title), Normalize(local.OriginalTitle) };
        var itemTitles = new[] { Normalize(item.Title), Normalize(item.OriginalTitle) };
        return localTitles.Any(l => l.Length > 0 && itemTitles.Contains(l, StringComparer.Ordinal));
    }

    private static bool AudiobookMatches(LocalCatalogEntry local, CatalogItem item)
    {
        if (!string.Equals(Normalize(local.Title), Normalize(item.Title), StringComparison.Ordinal))
        {
            return false;
        }

        if (local.Authors.Count == 0 || item.Authors.Count == 0)
        {
            return true;
        }

        var authors = item.Authors.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        return local.Authors.Any(a => authors.Contains(Normalize(a)));
    }

    private static CatalogItem MergeLocal(CatalogItem item, LocalCatalogEntry local)
        => LocalAvailability.MarkOnDisk(item, local) with
        {
            Overview = item.Overview ?? local.Overview
        };

    private static CatalogItem FromLocal(LocalCatalogEntry local)
    {
        string key;
        if (local.TmdbId is int tmdbId && local.Kind != CatalogItemKind.Audiobook)
        {
            key = TmdbMapper.Key(local.Kind, tmdbId);
        }
        else if (!string.IsNullOrEmpty(local.Asin))
        {
            key = "audible:" + local.Asin.ToUpperInvariant();
        }
        else
        {
            key = "library:" + local.ItemId.ToString("N", CultureInfo.InvariantCulture);
        }

        return new CatalogItem
        {
            Key = key,
            Kind = local.Kind,
            Title = local.Title,
            OriginalTitle = local.OriginalTitle,
            Year = local.Year,
            Overview = local.Overview,
            Availability = CatalogAvailability.OnDisk,
            LocalItemId = local.ItemId,
            TmdbId = local.Kind == CatalogItemKind.Audiobook ? null : local.TmdbId,
            Asin = local.Asin,
            Authors = local.Authors,
            Sources = [CatalogSource.Library]
        };
    }
}
