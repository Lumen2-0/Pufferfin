using System.Collections.Generic;

namespace Jellyfin.MediaEncoding.Hls.Playlist;

/// <summary>
/// Generator for dynamic HLS playlists where the segment lengths aren't known in advance.
/// </summary>
public interface IDynamicHlsPlaylistGenerator
{
    /// <summary>
    /// Creates the main playlist containing the main video or audio stream.
    /// </summary>
    /// <param name="request">An instance of the <see cref="CreateMainPlaylistRequest"/> class.</param>
    /// <returns>The playlist as a formatted string.</returns>
    string CreateMainPlaylist(CreateMainPlaylistRequest request);

    /// <summary>
    /// Gets the lengths, in seconds, of the segments the main playlist of a request lists, in order.
    /// </summary>
    /// <param name="request">An instance of the <see cref="CreateMainPlaylistRequest"/> class.</param>
    /// <returns>The lengths of the segments.</returns>
    IReadOnlyList<double> GetSegmentLengths(CreateMainPlaylistRequest request);
}
