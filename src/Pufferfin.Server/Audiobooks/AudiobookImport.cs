using System.Collections.Generic;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// What the import of an audiobook filed.
/// </summary>
/// <param name="Folder">The folder of the book in the library.</param>
/// <param name="Files">The number of audio files linked or found linked already.</param>
/// <param name="HasCover">Whether the cover is in the folder.</param>
/// <param name="NotWritable">What Pufferfin cannot change or delete in the folder; empty when everything is writable.</param>
public sealed record AudiobookImport(string Folder, int Files, bool HasCover, IReadOnlyList<string> NotWritable);
