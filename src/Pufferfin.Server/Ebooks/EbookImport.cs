using System.Collections.Generic;

namespace Pufferfin.Server.Ebooks;

/// <summary>
/// What the import of a book filed.
/// </summary>
/// <param name="Folder">The folder of the book in the library.</param>
/// <param name="File">The book file in the library.</param>
/// <param name="TorrentFile">The file a torrent shares with the library, by inode: the book file itself, or the downloaded archive it was extracted from.</param>
/// <param name="Extracted">Whether the book file was extracted from an archive: a copy, not a hard link.</param>
/// <param name="HasCover">Whether the cover is in the folder.</param>
/// <param name="NotWritable">What Pufferfin cannot change or delete in the folder; empty when everything is writable.</param>
public sealed record EbookImport(string Folder, string File, string TorrentFile, bool Extracted, bool HasCover, IReadOnlyList<string> NotWritable);
