using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Catalog.Audible;
using Pufferfin.Catalog.Http;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Requests.Integration;
using Pufferfin.Server.Users;

namespace Pufferfin.Server.Requests;

/// <summary>
/// Creates and changes requests on behalf of a user, with the rights, quotas and rate limits checked here, on the server.
/// </summary>
public sealed partial class RequestService
{
    /// <summary>
    /// The number of requests a user may create per minute, administrators included.
    /// </summary>
    public const int MaxRequestsPerMinute = 10;

    private readonly RequestStore _store;
    private readonly IRequestWaker _waker;
    private readonly IPufferfinConfigurationStore _configuration;
    private readonly IPufferfinUserSettingsStore _userSettings;
    private readonly ITitleMetadata _metadata;
    private readonly IAudiobookMetadata _audiobooks;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _admission = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestService"/> class.
    /// </summary>
    /// <param name="store">The request store.</param>
    /// <param name="waker">Wakes the processor.</param>
    /// <param name="configuration">The configuration store.</param>
    /// <param name="userSettings">The per user settings.</param>
    /// <param name="metadata">The TMDB metadata.</param>
    /// <param name="audiobooks">The Audible metadata.</param>
    /// <param name="timeProvider">The time provider.</param>
    public RequestService(
        RequestStore store,
        IRequestWaker waker,
        IPufferfinConfigurationStore configuration,
        IPufferfinUserSettingsStore userSettings,
        ITitleMetadata metadata,
        IAudiobookMetadata audiobooks,
        TimeProvider timeProvider)
    {
        _store = store;
        _waker = waker;
        _configuration = configuration;
        _userSettings = userSettings;
        _metadata = metadata;
        _audiobooks = audiobooks;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Creates a request, or returns the request the user already has on its way for the same title.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="isAdministrator">Whether the user is an administrator.</param>
    /// <param name="input">What the user asks for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<RequestCreation> CreateAsync(Guid userId, bool isAdministrator, RequestInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Validate(input) is { } invalid)
        {
            return RequestCreation.Refused(RequestRefusal.Invalid, invalid);
        }

        if (input.Kind == RequestKind.Audiobook)
        {
            input = input with { Asin = input.Asin!.Trim().ToUpperInvariant(), Market = string.IsNullOrWhiteSpace(input.Market) ? null : input.Market.Trim().ToLowerInvariant() };
        }

        var settings = _configuration.Current.Settings;
        var service = input.Kind switch
        {
            RequestKind.Movie => PufferfinService.Radarr,
            RequestKind.Audiobook => PufferfinService.Shelfmark,
            _ => PufferfinService.Sonarr
        };
        if (!settings.IsEnabled(service))
        {
            return RequestCreation.Refused(RequestRefusal.ServiceDisabled, $"{service} is not configured.");
        }

        // A cheap first pass, so a refused request never reaches TMDB. The decisive pass runs under the admission lock.
        if (Admit(userId, isAdministrator, input, _timeProvider.GetUtcNow()) is { } early)
        {
            return early;
        }

        string title;
        string? bookLanguage = null;
        if (input.Kind == RequestKind.Audiobook)
        {
            try
            {
                var book = await _audiobooks.GetAsync(input.Asin!, input.Market, cancellationToken).ConfigureAwait(false);
                if (book is null)
                {
                    return RequestCreation.Refused(RequestRefusal.TitleUnknown, "Audible does not know this audiobook.");
                }

                title = book.Title;
                bookLanguage = book.Language;
                input = input with { Market = book.Market ?? input.Market };
            }
            catch (CatalogSourceException)
            {
                // Audible is unreachable now: the search will check the book again.
                title = "Audible " + input.Asin;
            }
        }
        else
        {
            try
            {
                var metadata = input.Kind == RequestKind.Movie
                    ? await _metadata.GetMovieAsync(input.TmdbId, cancellationToken).ConfigureAwait(false)
                    : await _metadata.GetSeriesAsync(input.TmdbId, cancellationToken).ConfigureAwait(false);
                if (metadata is null)
                {
                    return RequestCreation.Refused(RequestRefusal.TitleUnknown, "TMDB does not know this title.");
                }

                title = metadata.Title;
            }
            catch (CatalogSourceException)
            {
                // TMDB is unreachable now: the search will check the title again.
                title = "TMDB " + input.TmdbId.ToString(CultureInfo.InvariantCulture);
            }
        }

        title += input.Kind switch
        {
            RequestKind.Season => string.Create(CultureInfo.InvariantCulture, $" S{input.SeasonNumber:00}"),
            RequestKind.Episode => string.Create(CultureInfo.InvariantCulture, $" S{input.SeasonNumber:00}E{input.EpisodeNumber:00}"),
            _ => string.Empty
        };

        // An audiobook is in the language chosen when searching Audible: the language of the book.
        var language = input.Language
            ?? (input.Kind is RequestKind.Season or RequestKind.Episode ? _store.GetSeriesLanguage(userId, input.TmdbId) : null)
            ?? bookLanguage
            ?? _userSettings.Get(userId).PreferredAudioLanguages.FirstOrDefault()
            ?? settings.PreferredAudioLanguages.FirstOrDefault()
            ?? MediaRequest.OriginalLanguage;
        MediaRequest request;

        // Checking and adding under one lock: concurrent creations cannot all pass the quota before any is counted.
        lock (_admission)
        {
            var now = _timeProvider.GetUtcNow();
            if (Admit(userId, isAdministrator, input, now) is { } refused)
            {
                return refused;
            }

            if (input.Kind is RequestKind.Season or RequestKind.Episode)
            {
                _store.SetSeriesLanguage(userId, input.TmdbId, language);
            }

            var permissions = isAdministrator ? RequestPermissions.Default : _store.GetPermissions(userId);
            request = _store.Add(new MediaRequest
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Kind = input.Kind,
                TmdbId = input.TmdbId,
                Asin = input.Asin,
                Market = input.Market,
                SeasonNumber = input.SeasonNumber,
                EpisodeNumber = input.EpisodeNumber,
                AirDate = input.AirDate,
                Title = title,
                Language = language,
                State = !isAdministrator && permissions.RequiresApproval ? RequestState.PendingApproval : RequestState.Queued,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        _waker.Wake();
        return new RequestCreation(request, false, null, null);
    }

    /// <summary>
    /// Checks a creation against the requests the user already has: the same request on its way, the rate limit,
    /// the permission and the weekly quota.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="isAdministrator">Whether the user is an administrator.</param>
    /// <param name="input">What the user asks for.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The outcome when the creation stops here, or <c>null</c> when it may go on.</returns>
    private RequestCreation? Admit(Guid userId, bool isAdministrator, RequestInput input, DateTimeOffset now)
    {
        var mine = _store.GetAll().Where(r => r.UserId.Equals(userId)).ToList();
        var existing = mine.FirstOrDefault(r => r.IsActive
            && r.Kind == input.Kind
            && r.TmdbId == input.TmdbId
            && string.Equals(r.Asin, input.Asin, StringComparison.OrdinalIgnoreCase)
            && r.SeasonNumber == input.SeasonNumber
            && r.EpisodeNumber == input.EpisodeNumber);
        if (existing is not null)
        {
            return new RequestCreation(existing, true, null, null);
        }

        if (mine.Count(r => r.CreatedAt > now - TimeSpan.FromMinutes(1)) >= MaxRequestsPerMinute)
        {
            return RequestCreation.Refused(RequestRefusal.RateLimited, "Too many requests in a minute. Try again shortly.");
        }

        var permissions = isAdministrator ? RequestPermissions.Default : _store.GetPermissions(userId);
        if (!permissions.CanRequest)
        {
            return RequestCreation.Refused(RequestRefusal.NotAllowed, "This account may not request titles.");
        }

        if (permissions.WeeklyQuota is int quota
            && mine.Count(r => r.CreatedAt > now - TimeSpan.FromDays(7) && r.State != RequestState.Rejected) >= quota)
        {
            return RequestCreation.Refused(RequestRefusal.QuotaExceeded, string.Create(CultureInfo.InvariantCulture, $"The quota of {quota} requests in seven days is reached."));
        }

        return null;
    }

    /// <summary>
    /// Cancels a request that is not available yet. Nothing is deleted from Radarr, Sonarr or qBittorrent.
    /// </summary>
    /// <param name="id">The request.</param>
    /// <param name="userId">The user acting.</param>
    /// <param name="isAdministrator">Whether the user is an administrator.</param>
    /// <returns>The outcome.</returns>
    public RequestChange Cancel(Guid id, Guid userId, bool isAdministrator)
        => Change(id, userId, isAdministrator, ownerAllowed: true, r => r.IsActive || r.State == RequestState.Failed, r => r.To(RequestState.Cancelled, _timeProvider.GetUtcNow()));

    /// <summary>
    /// Puts a failed request back in the queue.
    /// </summary>
    /// <param name="id">The request.</param>
    /// <param name="userId">The user acting.</param>
    /// <param name="isAdministrator">Whether the user is an administrator.</param>
    /// <returns>The outcome.</returns>
    public RequestChange Retry(Guid id, Guid userId, bool isAdministrator)
        => Change(id, userId, isAdministrator, ownerAllowed: true, r => r.State == RequestState.Failed, r => r.To(RequestState.Queued, _timeProvider.GetUtcNow()) with { Attempts = 0 });

    /// <summary>
    /// Approves a request waiting for approval. Administrators only.
    /// </summary>
    /// <param name="id">The request.</param>
    /// <param name="userId">The administrator.</param>
    /// <returns>The outcome.</returns>
    public RequestChange Approve(Guid id, Guid userId)
        => Change(id, userId, true, ownerAllowed: false, r => r.State == RequestState.PendingApproval, r => r.To(RequestState.Queued, _timeProvider.GetUtcNow()));

    /// <summary>
    /// Refuses a request waiting for approval. Administrators only.
    /// </summary>
    /// <param name="id">The request.</param>
    /// <param name="userId">The administrator.</param>
    /// <returns>The outcome.</returns>
    public RequestChange Reject(Guid id, Guid userId)
        => Change(id, userId, true, ownerAllowed: false, r => r.State == RequestState.PendingApproval, r => r.To(RequestState.Rejected, _timeProvider.GetUtcNow()));

    private static string? Validate(RequestInput input)
    {
        if (input.Kind == RequestKind.Audiobook)
        {
            return ValidateAudiobook(input);
        }

        if (input.Asin is not null || input.Market is not null)
        {
            return "Only an audiobook has an ASIN and a market.";
        }

        if (input.TmdbId <= 0)
        {
            return "The TMDB id must be positive.";
        }

        var numbersOk = input.Kind switch
        {
            RequestKind.Movie => input.SeasonNumber is null && input.EpisodeNumber is null,
            RequestKind.Season => input.SeasonNumber >= 0 && input.EpisodeNumber is null,
            RequestKind.Episode => input.SeasonNumber >= 0 && input.EpisodeNumber >= 1,
            _ => false
        };
        if (!numbersOk)
        {
            return "A film has no season nor episode, a season has a season number, an episode has both.";
        }

        if (input.Language is { } language && language != MediaRequest.OriginalLanguage && ArrLanguages.GetName(language) is null)
        {
            return "The language must be an ISO 639-1 code Radarr and Sonarr know, such as fr or pt-BR, or \"original\".";
        }

        return null;
    }

    private static string? ValidateAudiobook(RequestInput input)
    {
        if (input.TmdbId != 0 || input.SeasonNumber is not null || input.EpisodeNumber is not null || input.AirDate is not null)
        {
            return "An audiobook has no TMDB id, season nor episode.";
        }

        if (input.Asin is not { } asin || !AsinRegex().IsMatch(asin.Trim()))
        {
            return "An audiobook needs its Audible ASIN: ten letters and digits.";
        }

        if (!string.IsNullOrWhiteSpace(input.Market) && !AudibleMarkets.TryGetBaseAddress(input.Market.Trim(), out _))
        {
            return "The Audible market is unknown.";
        }

        if (input.Language is { } language && language != MediaRequest.OriginalLanguage && ArrLanguages.GetName(language) is null)
        {
            return "The language must be an ISO 639-1 code, such as fr or en, or \"original\" for the language of the book.";
        }

        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9]{10}$", RegexOptions.CultureInvariant)]
    private static partial Regex AsinRegex();

    private RequestChange Change(Guid id, Guid userId, bool isAdministrator, bool ownerAllowed, Func<MediaRequest, bool> allowed, Func<MediaRequest, MediaRequest> change)
    {
        if (_store.Get(id) is not { } request)
        {
            return new RequestChange(RequestChangeOutcome.NotFound, null);
        }

        if (!isAdministrator && !(ownerAllowed && request.UserId.Equals(userId)))
        {
            // Another user's request does not exist for this user.
            return new RequestChange(ownerAllowed ? RequestChangeOutcome.NotFound : RequestChangeOutcome.Forbidden, null);
        }

        if (!allowed(request))
        {
            return new RequestChange(RequestChangeOutcome.InvalidState, request);
        }

        var updated = _store.Update(id, request.Version, change);
        if (updated is null)
        {
            return new RequestChange(RequestChangeOutcome.InvalidState, _store.Get(id));
        }

        _waker.Wake();
        return new RequestChange(RequestChangeOutcome.Done, updated);
    }
}
