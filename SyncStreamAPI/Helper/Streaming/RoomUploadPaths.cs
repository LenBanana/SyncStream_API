using System;
using System.IO;

namespace SyncStreamAPI.Helper.Streaming;

/// <summary>
///     Where a chunked upload is assembled.
/// </summary>
/// <remarks>
///     Deliberately inside the permanent file directory rather than the temporary one, which is what it used to
///     be. Finishing an upload moves the assembled file to its final home, and those two directories are separate
///     Docker volumes, so the move was across filesystems. A cross-device move is not a rename: the runtime
///     copies every byte and then deletes the original. That is why a large upload sat for a long time after the
///     transfer itself had finished, writing the whole file a second time before it became usable.
///     <para>
///         Staged on the same volume, the same move is a rename in the directory table and costs nothing
///         whatever the file's size. The cost is that an abandoned upload now occupies space on the permanent
///         volume, which is why the background sweep exists.
///     </para>
///     <para>
///         The name starts with a dot so it does not read as a user's folder, and the directory listing the file
///         manager builds enumerates files rather than directories, so nothing in here appears in it.
///     </para>
/// </remarks>
public static class RoomUploadPaths
{
    /// <summary>The directory holding one staging folder per upload session.</summary>
    public static string Root => Path.Combine(General.FilePath, ".uploads");

    /// <summary>The previous location, kept only so the sweep can clear what was left there.</summary>
    public static string LegacyRoot => Path.Combine(General.TemporaryFilePath, "room-uploads");

    public static string DirectoryFor(string uploadId)
    {
        RoomUploadIds.RequireWellFormed(uploadId);
        return Path.Combine(Root, uploadId);
    }

    /// <summary>The assembled bytes.</summary>
    public static string DataFileFor(string uploadId)
    {
        return Path.Combine(DirectoryFor(uploadId), "upload.bin");
    }

    /// <summary>What the session knows about itself, so a resumed upload can be recognised.</summary>
    public static string SessionFileFor(string uploadId)
    {
        return Path.Combine(DirectoryFor(uploadId), "session.json");
    }

    /// <summary>Files sent alongside a shared upload: its text subtitles and their fonts.</summary>
    public static string SidecarDirectoryFor(string uploadId)
    {
        return Path.Combine(DirectoryFor(uploadId), "sidecar");
    }
}
