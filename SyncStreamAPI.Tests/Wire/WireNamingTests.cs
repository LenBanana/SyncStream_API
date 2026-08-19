using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.Wire.Games;
using SyncStreamAPI.Wire.Rooms;
using SyncStreamAPI.Wire.Users;
using Xunit;

namespace SyncStreamAPI.Tests.Wire;

/// <summary>
///     Pins the property names that actually go over the wire.
/// </summary>
/// <remarks>
///     The Angular client reads these names as string keys, so renaming a C# property, or changing the
///     serializer settings, breaks the client silently: both projects still compile and the failure only
///     shows up as undefined values at runtime. Nothing else in either build catches that.
///     <para>
///         Serialisation goes through the same settings the hub uses, taken from
///         <see cref="NewtonsoftJsonHubProtocolOptions" /> rather than hardcoded, so if the SignalR default
///         ever changes these tests fail rather than quietly drifting.
///     </para>
/// </remarks>
public class WireNamingTests
{
    private static JsonSerializerSettings HubSerializerSettings =>
        new NewtonsoftJsonHubProtocolOptions().PayloadSerializerSettings;

    private static JObject Serialize(object payload)
    {
        return JObject.Parse(JsonConvert.SerializeObject(payload, HubSerializerSettings));
    }

    private static IEnumerable<string> Keys(object payload)
    {
        return Serialize(payload).Properties().Select(property => property.Name);
    }

    [Fact]
    public void Room_summary_sends_the_keys_the_lobby_reads()
    {
        var room = new Room("Movie night", "movie-night", true, false) { password = "hashed" };
        room.server.members.Add(new Member { username = "someone", ishost = true, ConnectionId = "conn-1" });
        room.server.playlist.Add(new DreckVideo { title = "A video", url = "https://example.invalid/v" });

        var keys = Keys(new RoomSummaryDto(room)).ToList();

        Assert.Equal(
            new[]
            {
                "currenttime", "deletable", "hasPassword", "isPrivileged", "isplaying",
                "members", "name", "playlist", "title", "uniqueId"
            },
            keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Room_summary_never_serialises_a_password_or_a_connection_id()
    {
        var room = new Room("Movie night", "movie-night", true, false) { password = "hashed-room-password" };
        room.server.members.Add(new Member { username = "someone", ishost = true, ConnectionId = "conn-1" });
        room.server.chatmessages.Add(new ChatMessage { username = "someone", message = "hello" });

        var json = JsonConvert.SerializeObject(new RoomSummaryDto(room), HubSerializerSettings);

        Assert.DoesNotContain("hashed-room-password", json);
        Assert.DoesNotContain("conn-1", json);
        Assert.DoesNotContain("hello", json);
        Assert.DoesNotContain("password", json);
    }

    [Fact]
    public void Room_member_and_playlist_entries_send_the_keys_the_lobby_reads()
    {
        var member = new RoomMemberSummaryDto(new Member { username = "someone", ishost = true });
        Assert.Equal(new[] { "ishost", "username" }, Keys(member).OrderBy(key => key, StringComparer.Ordinal).ToArray());

        var entry = new RoomPlaylistEntryDto(new DreckVideo { title = "A video", url = "https://example.invalid/v" });
        Assert.Equal(new[] { "title", "url" }, Keys(entry).OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void User_summary_sends_id_not_ID()
    {
        // The Angular UserSummary interface declares `id`, and the C# property is `ID`. That only lines up
        // because the hub serialiser camel-cases, which is exactly what this asserts.
        var keys = Keys(new UserSummaryDto(new DbUser { ID = 7, username = "someone" })).ToList();

        Assert.Equal(new[] { "approved", "id", "username", "userprivileges" }, keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Self_profile_sends_the_key_names_the_settings_ui_reads()
    {
        var user = new DbUser
        {
            ID = 7,
            username = "someone",
            StreamToken = "stream-token",
            ApiKey = "api-key"
        };

        var keys = Keys(new SelfProfileDto(user)).ToList();

        Assert.Equal(
            new[] { "apiKey", "approved", "id", "streamToken", "username", "userprivileges" },
            keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Self_profile_never_serialises_the_password_or_salt()
    {
        var user = new DbUser
        {
            ID = 7,
            username = "someone",
            password = "the-password-hash",
            usersalt = "the-salt"
        };

        var json = JsonConvert.SerializeObject(new SelfProfileDto(user), HubSerializerSettings);

        Assert.DoesNotContain("the-password-hash", json);
        Assert.DoesNotContain("the-salt", json);
    }

    [Fact]
    public void Chess_payload_sends_the_keys_the_board_reads()
    {
        var game = new SyncStreamAPI.Models.GameModels.Chess.ChessGame(
            new Member { username = "light", ConnectionId = "conn-light" },
            new Member { username = "dark", ConnectionId = "conn-dark" },
            "movie-night");

        var json = JsonConvert.SerializeObject(new ChessGameDto(game), HubSerializerSettings);
        var keys = Keys(new ChessGameDto(game)).ToList();

        Assert.Equal(
            new[] { "darkPlayer", "darkPlayerAi", "gameFEN", "lightPlayer", "lightPlayerAi", "uniqueId" },
            keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());

        // The whole point of the DTO: the players' connection ids stay on the server.
        Assert.DoesNotContain("conn-light", json);
        Assert.DoesNotContain("conn-dark", json);
    }
}
