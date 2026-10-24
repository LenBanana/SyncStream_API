using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SyncStreamAPI.Interfaces;
using Xunit;

namespace SyncStreamAPI.Tests.Wire;

/// <summary>
///     Pins the exact fields of every domain type that still reaches a client.
/// </summary>
/// <remarks>
///     The other contract tests catch a <em>new type</em> crossing the wire. None of them catch a
///     <em>new field</em> on a type that already crosses, which is the more likely accident: adding a
///     property to a type in <c>Models/</c> is a one-line change with no hint that clients will receive it.
///     Add a server path to <c>DreckVideo</c> and every other test still passes.
///     <para>
///         That is the whole reason a wire boundary exists, and it is why leaving these types in
///         <c>Models/</c> was not merely untidy. Converting them all to DTOs would mean roughly thirty send
///         sites and a polymorphic chat hierarchy; pinning their shape costs one test and closes the same
///         hole, so the conversion can happen later on its own merits rather than under pressure.
///     </para>
/// </remarks>
public class DomainFieldsOnTheWireTests
{
    /// <summary>
    ///     Every public property of each domain type currently reaching clients.
    /// </summary>
    /// <remarks>
    ///     Adding a field to one of these types fails this test. That is the point: the failure asks whether
    ///     clients should really see it. If they should, add it here; if not, it does not belong on a type
    ///     that crosses the wire.
    /// </remarks>
    private static readonly Dictionary<string, string[]> ExpectedFields = new(StringComparer.Ordinal)
    {
        ["BotLiveChannelInfo"] = new[] { "ChannelId", "RoomName" },
        ["ChatMessage"] = new[] { "color", "message", "time", "usercolor", "username" },
        ["ChessMove"] = new[] { "Check", "Checkmate", "Color", "FEN", "Move" },
        ["Dialog"] = new[] { "AlertType", "Answer1", "Answer2", "Header", "Id", "Question" },
        ["DownloadFileInfo"] = new[]
        {
            "AudioBitrate", "AudioChannels", "AudioCodec", "CreationDate", "Duration", "FileName", "FileType",
            "Hash", "Size", "VideoBitrate", "VideoCodec", "VideoFramerate", "VideoHeight", "VideoWidth"
        },
        ["DownloadInfo"] = new[] { "Id", "Name", "Progress", "Type" },
        ["Drawing"] = new[] { "SelectedShape", "SelectedShapeOptions", "Type", "Uuid", "X", "Y" },
        ["DreckVideo"] = new[] { "AddedBy", "Length", "ended", "title", "url" },
        ["EditorProcess"] = new[] { "AlertType", "Id", "Progress", "Text" },

        // Elevated-only storage report, so the real paths it carries are the point of the view rather than
        // a leak. Still pinned: it is the one type here where a new field is most likely to be sensitive.
        ["FileStorageInfo"] = new[]
        {
            "DirectoryCount", "FileCount", "LargestFiles", "Path", "TotalDiskSpace", "TotalSize"
        },

        ["PrivilegeInfo"] = new[] { "Description", "MethodName", "Policy", "RequiredPrivileges", "TypeName" },
        ["SelectedShapeOptions"] = new[]
        {
            "FillStyle", "LineCap", "LineJoin", "LineWidth", "ShouldFillShape", "StrokeStyle"
        },
        ["ServerHealthDto"] = new[] { "CpuUsage", "DiskUsage", "MemoryUsage", "UpTime" },
        ["SfuNewProducerNotification"] = new[] { "Kind", "PeerId", "ProducerId", "RoomId" },
        ["SfuRecordingInfo"] = new[] { "Filename", "RoomId", "StartedAtUnixMs" },
        ["VoipParticipantDto"] = new[]
        {
            "IsMuted", "IsScreenMuted", "IsVideoMuted", "ParticipantId", "ParticipantName"
        }
    };

    private static IEnumerable<Type> ReachableTypes(Type root)
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();

            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                foreach (var argument in type.GetGenericArguments())
                    queue.Enqueue(argument);

            if (type.IsArray)
            {
                var element = type.GetElementType();
                if (element != null) queue.Enqueue(element);
                continue;
            }

            if (type.IsPrimitive || type == typeof(string) || type.IsEnum) continue;
            if (type.Namespace == null || !type.Namespace.StartsWith("SyncStreamAPI")) continue;
            if (!seen.Add(type)) continue;

            yield return type;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                queue.Enqueue(property.PropertyType);
        }
    }

    private static Dictionary<string, string[]> ActualFields()
    {
        return (from callback in typeof(IServerHub).GetMethods()
                from parameter in callback.GetParameters()
                from type in ReachableTypes(parameter.ParameterType)
                where type.Namespace != null && type.Namespace.StartsWith("SyncStreamAPI.Models")
                select type)
            .Distinct()
            .ToDictionary(
                type => type.Name,
                type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(property => property.Name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
    }

    [Fact]
    public void No_domain_type_on_the_wire_has_gained_or_lost_a_field()
    {
        var actual = ActualFields();
        var problems = new List<string>();

        foreach (var (typeName, expected) in ExpectedFields)
        {
            if (!actual.TryGetValue(typeName, out var current))
            {
                problems.Add($"{typeName} no longer reaches clients: remove it from ExpectedFields");
                continue;
            }

            var added = current.Except(expected, StringComparer.Ordinal).ToList();
            var removed = expected.Except(current, StringComparer.Ordinal).ToList();

            if (added.Count > 0)
                problems.Add($"{typeName} now also sends: {string.Join(", ", added)}");
            if (removed.Count > 0)
                problems.Add($"{typeName} no longer sends: {string.Join(", ", removed)}");
        }

        foreach (var typeName in actual.Keys.Where(name => !ExpectedFields.ContainsKey(name)))
            problems.Add($"{typeName} newly reaches clients and has no pinned field set");

        Assert.True(problems.Count == 0,
            "Domain payload shapes changed. Decide whether clients should see the difference:\n  "
            + string.Join("\n  ", problems));
    }
}
