using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Jellyfin.Api;
using Jellyfin.Api.Extensions;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pufferfin.Catalog.Library;

namespace Pufferfin.Catalog.Api;

/// <summary>
/// The books of a library, one entry per book: a book told in several files is its folder (see
/// <see cref="AudiobookFolders"/>), not one entry per file.
/// </summary>
[Route("Pufferfin/Library/Books")]
[Tags("Pufferfin")]
[Authorize]
public class PufferfinBooksController : BaseJellyfinApiController
{
    private const int MaxLimit = 200;

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IDtoService _dtoService;

    /// <summary>
    /// Initializes a new instance of the <see cref="PufferfinBooksController"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="userDataManager">The user data manager, for the progress of a book in parts.</param>
    /// <param name="dtoService">The DTO service.</param>
    public PufferfinBooksController(ILibraryManager libraryManager, IUserManager userManager, IUserDataManager userDataManager, IDtoService dtoService)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _dtoService = dtoService;
    }

    /// <summary>
    /// Gets the books of a library (books to read and audiobooks), one entry per book, by name.
    /// </summary>
    /// <remarks>
    /// A book in several files comes as its folder: <c>Type</c> is <c>Folder</c>, <c>AlbumArtist</c> names its authors
    /// and <c>UserData.PlayedPercentage</c> is the share of its parts already listened to.
    /// </remarks>
    /// <param name="parentId">The library.</param>
    /// <param name="inProgress">Optional. Only the books started and not finished, last listened first.</param>
    /// <param name="startIndex">Optional. The first entry to return.</param>
    /// <param name="limit">Optional. The maximum number of entries (1 to 200, default 60).</param>
    /// <response code="200">The books.</response>
    /// <response code="404">The user is unknown.</response>
    /// <returns>The page of books, with the total count.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<QueryResult<BaseItemDto>> GetPufferfinBooks(
        [FromQuery] Guid? parentId,
        [FromQuery] bool inProgress = false,
        [FromQuery, Range(0, int.MaxValue)] int startIndex = 0,
        [FromQuery, Range(1, MaxLimit)] int limit = 60)
    {
        var user = _userManager.GetUserById(User.GetUserId());
        if (user is null)
        {
            return NotFound();
        }

        var files = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            AncestorIds = parentId is { } library && !library.Equals(Guid.Empty) ? [library] : [],
            IncludeItemTypes = [BaseItemKind.AudioBook, BaseItemKind.Book],
            Recursive = true
        });

        // One entry per book: the files of a book in parts make way for their folder
        var books = new List<BookEntry>();
        var seen = new HashSet<Guid>();
        var partsByFolder = new Dictionary<Guid, IReadOnlyList<AudioBook>>();
        foreach (var file in files)
        {
            BaseItem book = file;
            IReadOnlyList<AudioBook> parts = [];
            if (file is AudioBook audioBook && audioBook.GetParent() is { } folder)
            {
                if (!partsByFolder.TryGetValue(folder.Id, out var folderParts))
                {
                    folderParts = AudiobookFolders.GetParts(folder, _libraryManager);
                    partsByFolder[folder.Id] = folderParts;
                }

                if (folderParts.Count > 0)
                {
                    book = folder;
                    parts = folderParts;
                }
            }

            if (seen.Add(book.Id))
            {
                books.Add(new BookEntry(book, parts, Progress(user, book, parts)));
            }
        }

        IEnumerable<BookEntry> listed = books;
        if (inProgress)
        {
            listed = books
                .Where(b => b.Progress.Started && !b.Progress.Finished)
                .OrderByDescending(b => b.Progress.LastPlayed);
        }
        else
        {
            listed = books.OrderBy(b => b.Item.SortName, StringComparer.OrdinalIgnoreCase);
        }

        var all = listed.ToList();
        var page = all.Skip(startIndex).Take(limit).ToList();
        var dtos = _dtoService.GetBaseItemDtos(page.Select(b => b.Item).ToList(), new DtoOptions(true), user);
        for (var i = 0; i < page.Count; i++)
        {
            var entry = page[i];
            if (entry.Parts.Count == 0)
            {
                continue;
            }

            // A folder knows neither its authors nor its progress: the parts tell them
            var dto = dtos[i];
            dto.AlbumArtist = string.Join(", ", AudiobookFolders.GetAuthors(entry.Item, entry.Parts));
            dto.ChildCount = entry.Parts.Count;
            if (dto.UserData is { } userData)
            {
                userData.PlayedPercentage = entry.Progress.Percentage;
                userData.Played = entry.Progress.Finished;
            }
        }

        return new QueryResult<BaseItemDto>(startIndex, all.Count, dtos);
    }

    private BookProgress Progress(Jellyfin.Database.Implementations.Entities.User user, BaseItem book, IReadOnlyList<AudioBook> parts)
    {
        var items = parts.Count > 0 ? parts.Cast<BaseItem>().ToList() : [book];
        var data = items.Select(i => _userDataManager.GetUserData(user, i)).ToList();
        var played = data.Count(d => d?.Played == true);
        var started = data.Any(d => d is not null && (d.Played || d.PlaybackPositionTicks > 0));
        var lastPlayed = data.Select(d => d?.LastPlayedDate).Where(d => d.HasValue).DefaultIfEmpty(null).Max();
        return new BookProgress(
            started,
            played == items.Count,
            items.Count == 0 ? 0 : played * 100.0 / items.Count,
            lastPlayed ?? DateTime.MinValue);
    }

    private sealed record BookEntry(BaseItem Item, IReadOnlyList<AudioBook> Parts, BookProgress Progress);

    private sealed record BookProgress(bool Started, bool Finished, double Percentage, DateTime LastPlayed);
}
