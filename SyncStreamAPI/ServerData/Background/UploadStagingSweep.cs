using System;
using System.IO;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Helper.Streaming;

namespace SyncStreamAPI.ServerData.Background;

/// <summary>
///     Clears staging directories nothing is going to finish.
/// </summary>
/// <remarks>
///     Uploads are assembled on the same volume as the finished files, so that finishing one is a rename instead
///     of a copy of the whole thing. The consequence is that an abandoned upload occupies space that is not
///     temporary, where before it sat in a directory somebody else was clearing. Nothing swept the old location
///     either, so a cancelled upload leaked there too; it just leaked somewhere less costly.
/// </remarks>
public static class UploadStagingSweep
{
    public static void Run(Action<string> log)
    {
        Sweep(RoomUploadPaths.Root, log);

        // The directory uploads used to be assembled in. Swept so whatever was left there when the location
        // changed does not sit forever, and so cancelled uploads from before any of this get cleared too.
        Sweep(RoomUploadPaths.LegacyRoot, log);
    }

    private static void Sweep(string root, Action<string> log)
    {
        if (!Directory.Exists(root)) return;

        foreach (var directory in Directory.GetDirectories(root))
            try
            {
                if (!IsAbandoned(directory)) continue;

                Directory.Delete(directory, true);
                log($"Removed abandoned upload staging directory {Path.GetFileName(directory)}");
            }
            catch (Exception ex)
            {
                // One directory that will not go, often because something still holds the file open, must not
                // stop the rest of the sweep or take the timer down with it.
                log($"Could not remove upload staging directory {directory}: {ex.Message}");
            }
    }

    /// <summary>
    ///     Whether a staging directory has been left behind.
    /// </summary>
    /// <remarks>
    ///     Two thresholds, because the two cases look different. An upload in progress is written to constantly,
    ///     so one that has not been touched for a day is not coming back. A finished server file-share, on the
    ///     other hand, keeps its file in place on purpose while the room plays it, and the room removes it when
    ///     the share stops; the longer threshold is only for the case where that never happened.
    /// </remarks>
    private static bool IsAbandoned(string directory)
    {
        var lastTouched = LastWriteUtc(directory);
        var age = DateTime.UtcNow - lastTouched;

        if (age > General.DaysToKeepUploadStaging) return true;

        // Data but no session file means the session was never recorded or has already been cleared, so nothing
        // is going to complete it.
        var hasSession = File.Exists(Path.Combine(directory, "session.json"));
        return !hasSession && age > General.HoursToKeepUnfinishedUploads;
    }

    /// <summary>
    ///     The most recent write anywhere in the directory.
    /// </summary>
    /// <remarks>
    ///     The directory's own stamp does not move when a file inside it is appended to, which is exactly what an
    ///     upload in progress does, so going by that alone would delete an upload that was still arriving.
    /// </remarks>
    private static DateTime LastWriteUtc(string directory)
    {
        var latest = Directory.GetLastWriteTimeUtc(directory);

        foreach (var file in Directory.GetFiles(directory))
        {
            var written = File.GetLastWriteTimeUtc(file);
            if (written > latest) latest = written;
        }

        return latest;
    }
}
