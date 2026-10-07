using System.Collections.Generic;

namespace Pufferfin.Catalog.Audible;

/// <summary>
/// The subset of the Audible catalog payloads that Pufferfin reads.
/// </summary>
internal static class AudibleModels
{
    internal sealed class ProductList
    {
        public List<Product> Products { get; set; } = [];
    }

    internal sealed class ProductEnvelope
    {
        public Product? Product { get; set; }
    }

    internal sealed class Product
    {
        public string? Asin { get; set; }

        public string? Title { get; set; }

        public string? Subtitle { get; set; }

        public List<Contributor> Authors { get; set; } = [];

        public List<Contributor> Narrators { get; set; } = [];

        public List<SeriesRef> Series { get; set; } = [];

        public string? Language { get; set; }

        public string? ReleaseDate { get; set; }

        public string? MerchandisingSummary { get; set; }

        public string? PublisherSummary { get; set; }

        public int? RuntimeLengthMin { get; set; }

        public Dictionary<string, string> ProductImages { get; set; } = [];

        public bool? IsAdultProduct { get; set; }

        /// <summary>Gets or sets the kind of product: <c>SinglePartBook</c> and <c>MultiPartBook</c> are books, the rest podcasts or episodes.</summary>
        public string? ContentDeliveryType { get; set; }
    }

    internal sealed class Contributor
    {
        public string? Name { get; set; }
    }

    internal sealed class SeriesRef
    {
        public string? Title { get; set; }

        public string? Sequence { get; set; }
    }
}
