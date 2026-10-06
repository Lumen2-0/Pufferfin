using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pufferfin.Catalog.Tmdb;

/// <summary>
/// The subset of the TMDB v3 payloads that Pufferfin reads.
/// </summary>
internal static class TmdbModels
{
    internal sealed class ResultPage<T>
    {
        public int Page { get; set; }

        public int TotalPages { get; set; }

        public List<T> Results { get; set; } = [];
    }

    /// <summary>
    /// A search, recommendation, credit or collection entry. Movies use <c>title</c>, series and people use <c>name</c>.
    /// </summary>
    internal sealed class Entry
    {
        public int Id { get; set; }

        public string? MediaType { get; set; }

        public string? Title { get; set; }

        public string? OriginalTitle { get; set; }

        public string? Name { get; set; }

        public string? OriginalName { get; set; }

        public string? ReleaseDate { get; set; }

        public string? FirstAirDate { get; set; }

        public string? Overview { get; set; }

        public string? OriginalLanguage { get; set; }

        public string? PosterPath { get; set; }

        public string? BackdropPath { get; set; }

        public string? ProfilePath { get; set; }

        public string? KnownForDepartment { get; set; }

        public string? Character { get; set; }

        public string? Job { get; set; }

        public double Popularity { get; set; }
    }

    /// <summary>
    /// A film or series sheet, requested with <c>append_to_response=credits,videos,recommendations,translations,external_ids</c>.
    /// </summary>
    internal sealed class TitleDetails
    {
        public int Id { get; set; }

        public string? Title { get; set; }

        public string? OriginalTitle { get; set; }

        public string? Name { get; set; }

        public string? OriginalName { get; set; }

        public string? OriginalLanguage { get; set; }

        public string? Overview { get; set; }

        public string? Tagline { get; set; }

        public string? ReleaseDate { get; set; }

        public string? FirstAirDate { get; set; }

        public int? Runtime { get; set; }

        public List<int> EpisodeRunTime { get; set; } = [];

        public int? NumberOfSeasons { get; set; }

        public double? VoteAverage { get; set; }

        public string? PosterPath { get; set; }

        public string? BackdropPath { get; set; }

        public string? ImdbId { get; set; }

        public List<Genre> Genres { get; set; } = [];

        public List<string> OriginCountry { get; set; } = [];

        public List<Country> ProductionCountries { get; set; } = [];

        public List<Entry> CreatedBy { get; set; } = [];

        public CollectionRef? BelongsToCollection { get; set; }

        public Credits? Credits { get; set; }

        public ResultPage<Video>? Videos { get; set; }

        public ResultPage<Entry>? Recommendations { get; set; }

        public TranslationList? Translations { get; set; }

        public ExternalIds? ExternalIds { get; set; }
    }

    internal sealed class PersonDetails
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Biography { get; set; }

        public string? Birthday { get; set; }

        public string? Deathday { get; set; }

        public string? PlaceOfBirth { get; set; }

        public string? ProfilePath { get; set; }

        public string? KnownForDepartment { get; set; }

        public Credits? CombinedCredits { get; set; }

        public TranslationList? Translations { get; set; }
    }

    internal sealed class CollectionDetails
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Overview { get; set; }

        public string? PosterPath { get; set; }

        public string? BackdropPath { get; set; }

        public List<Entry> Parts { get; set; } = [];
    }

    internal sealed class Genre
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }

    internal sealed class GenreList
    {
        public List<Genre> Genres { get; set; } = [];
    }

    internal sealed class Country
    {
        [JsonPropertyName("iso_3166_1")]
        public string? Iso31661 { get; set; }

        public string? Name { get; set; }
    }

    internal sealed class CollectionRef
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? PosterPath { get; set; }
    }

    internal sealed class Credits
    {
        public List<Entry> Cast { get; set; } = [];

        public List<Entry> Crew { get; set; } = [];
    }

    internal sealed class Video
    {
        public string? Name { get; set; }

        public string? Site { get; set; }

        public string? Key { get; set; }

        public string? Type { get; set; }

        [JsonPropertyName("iso_639_1")]
        public string? Iso6391 { get; set; }

        public bool Official { get; set; }

        public string? PublishedAt { get; set; }
    }

    internal sealed class TranslationList
    {
        public List<Translation> Translations { get; set; } = [];
    }

    internal sealed class Translation
    {
        [JsonPropertyName("iso_639_1")]
        public string? Iso6391 { get; set; }

        [JsonPropertyName("iso_3166_1")]
        public string? Iso31661 { get; set; }

        public TranslationData? Data { get; set; }
    }

    internal sealed class TranslationData
    {
        public string? Title { get; set; }

        public string? Name { get; set; }

        public string? Overview { get; set; }

        public string? Tagline { get; set; }

        public string? Biography { get; set; }
    }

    internal sealed class ExternalIds
    {
        public string? ImdbId { get; set; }

        public int? TvdbId { get; set; }
    }
}
