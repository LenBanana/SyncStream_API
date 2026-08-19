using System;
using System.IO;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Helper.Streaming;
using Xunit;

namespace SyncStreamAPI.Tests.Streaming;

/// <summary>
///     The upload session id, and where the bytes are assembled.
/// </summary>
public class RoomUploadPathTests
{
    private const string RealId = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void The_id_the_server_generates_is_accepted()
    {
        // The shape the check is written around: Guid.NewGuid().ToString("N").
        Assert.True(RoomUploadIds.IsWellFormed(Guid.NewGuid().ToString("N")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../../..")]
    [InlineData("../../../etc")]
    [InlineData("0123456789abcdef0123456789abcde")] // one short
    [InlineData("0123456789abcdef0123456789abcdef0")] // one long
    [InlineData("0123456789ABCDEF0123456789ABCDEF")] // upper case, which the generator never produces
    [InlineData("0123456789abcdef0123456789abcdeZ")]
    [InlineData("..%2f..%2f")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void Anything_that_is_not_that_shape_is_refused(string? uploadId)
    {
        Assert.False(RoomUploadIds.IsWellFormed(uploadId));
    }

    [Fact]
    public void A_path_cannot_be_built_from_an_id_that_would_escape_the_upload_root()
    {
        // Two hubs built a path straight from the id with Path.Combine, one of them carrying a comment that the
        // id "is a GUID, 32 hex chars, cryptographically unguessable", which nothing checked. Path.Combine
        // resolves ../.. happily, so the file that was read and streamed to a room could come from anywhere on
        // the disk that happened to hold an upload.bin.
        Assert.Throws<ArgumentException>(() => RoomUploadPaths.DataFileFor("../../../etc"));
    }

    [Fact]
    public void A_real_id_produces_a_path_under_the_upload_root()
    {
        var path = RoomUploadPaths.DataFileFor(RealId);

        Assert.StartsWith(RoomUploadPaths.Root, path);
        Assert.EndsWith("upload.bin", path);
    }

    [Fact]
    public void Uploads_are_assembled_on_the_same_volume_they_end_up_on()
    {
        // The reason finishing a large upload used to take a long time after the transfer itself had finished.
        // Staging lived under the temporary directory and the finished file under the permanent one, which are
        // separate Docker volumes, so the move that finishes an upload crossed filesystems: the runtime copies
        // every byte and deletes the original instead of renaming. Under the same root it is a rename, whatever
        // the file's size. If these ever diverge again, the copy comes back silently and only shows up as
        // slowness.
        Assert.StartsWith(
            General.FilePath + Path.DirectorySeparatorChar,
            RoomUploadPaths.Root + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void The_previous_location_is_still_known_so_it_can_be_cleared()
    {
        // Nothing swept it, so whatever was left there when the location moved would sit forever.
        Assert.StartsWith(General.TemporaryFilePath, RoomUploadPaths.LegacyRoot);
        Assert.NotEqual(RoomUploadPaths.Root, RoomUploadPaths.LegacyRoot);
    }
}
