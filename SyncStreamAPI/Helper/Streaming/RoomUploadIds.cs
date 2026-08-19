using System;
using System.Text.RegularExpressions;

namespace SyncStreamAPI.Helper.Streaming;

/// <summary>
///     The shape of an upload session id.
/// </summary>
/// <remarks>
///     The id is generated here as <c>Guid.NewGuid().ToString("N")</c> and then travels through the client, which
///     hands it back on every chunk and when a share starts. Each of those paths built a filesystem path from it
///     with <c>Path.Combine</c>, and a comment in one of them recorded the assumption that it is "a GUID, 32 hex
///     chars, cryptographically unguessable". Nothing checked that. A value of <c>../../..</c> is not a GUID, and
///     <c>Path.Combine</c> resolves it happily, so the assumption held only for callers who chose to honour it.
///     <para>
///         Checked at the entry points rather than sanitised: there is exactly one legal shape, so anything else
///         is a caller doing something it has no reason to do, and stripping segments out of it to make it usable
///         would just be guessing what it meant.
///     </para>
/// </remarks>
public static class RoomUploadIds
{
    private static readonly Regex Shape = new("^[0-9a-f]{32}$", RegexOptions.Compiled);

    public static bool IsWellFormed(string? uploadId)
    {
        return uploadId != null && Shape.IsMatch(uploadId);
    }

    /// <summary>
    ///     Throws unless the id can safely become a path segment.
    /// </summary>
    /// <remarks>
    ///     The backstop under the entry-point checks. If a path is ever built from an unchecked id, this is what
    ///     stops it, loudly, rather than reading or writing somewhere outside the upload directory.
    /// </remarks>
    public static void RequireWellFormed(string? uploadId)
    {
        if (!IsWellFormed(uploadId))
            throw new ArgumentException(
                $"Upload id '{uploadId}' is not a 32-character hex id and cannot be used as a path segment.",
                nameof(uploadId));
    }
}
