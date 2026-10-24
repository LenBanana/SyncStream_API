using System;
using System.Collections.Generic;
using System.Linq;
using SyncStreamAPI.Models.WebRTC;

namespace SyncStreamAPI.ServerData;

/// <summary>Who is in which room's voice call, and how they last said their microphone and camera were.</summary>
public sealed class VoipRoster
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Participant> _byConnection = new(StringComparer.Ordinal);

    private sealed class Participant
    {
        public required string RoomId { get; init; }
        public required string Name { get; init; }
        public bool IsMuted { get; set; }
        public bool IsVideoMuted { get; set; } = true;
        public bool IsScreenMuted { get; set; } = true;
    }

    /// <summary>Puts the connection in a room's call, leaving any other call it was in.</summary>
    /// <returns>The call it left (null when none), and everyone else already in the joined call.</returns>
    public (string? PreviousRoomId, IReadOnlyList<VoipParticipantDto> Others) Join(
        string connectionId, string roomId, string name)
    {
        lock (_gate)
        {
            string? previous = null;
            if (_byConnection.TryGetValue(connectionId, out var existing) && existing.RoomId != roomId)
                previous = existing.RoomId;

            _byConnection[connectionId] = new Participant { RoomId = roomId, Name = name };

            var others = _byConnection
                .Where(entry => entry.Key != connectionId && entry.Value.RoomId == roomId)
                .Select(entry => ToDto(entry.Key, entry.Value))
                .ToList();
            return (previous, others);
        }
    }

    /// <returns>The room whose call the connection left, or null when it was in none.</returns>
    public string? Leave(string connectionId)
    {
        lock (_gate)
        {
            return _byConnection.Remove(connectionId, out var participant) ? participant.RoomId : null;
        }
    }

    public string? RoomOf(string connectionId)
    {
        lock (_gate)
        {
            return _byConnection.TryGetValue(connectionId, out var participant) ? participant.RoomId : null;
        }
    }

    public bool IsIn(string connectionId, string roomId)
    {
        return RoomOf(connectionId) == roomId;
    }

    /// <returns>The participant as others should now see them, or null when not in a call.</returns>
    public VoipParticipantDto? UpdateStatus(string connectionId, bool isMuted, bool isVideoMuted, bool isScreenMuted)
    {
        lock (_gate)
        {
            if (!_byConnection.TryGetValue(connectionId, out var participant)) return null;
            participant.IsMuted = isMuted;
            participant.IsVideoMuted = isVideoMuted;
            participant.IsScreenMuted = isScreenMuted;
            return ToDto(connectionId, participant);
        }
    }

    public VoipParticipantDto? Describe(string connectionId)
    {
        lock (_gate)
        {
            return _byConnection.TryGetValue(connectionId, out var participant)
                ? ToDto(connectionId, participant)
                : null;
        }
    }

    private static VoipParticipantDto ToDto(string connectionId, Participant participant)
    {
        return new VoipParticipantDto
        {
            ParticipantId = connectionId,
            ParticipantName = participant.Name,
            IsMuted = participant.IsMuted,
            IsVideoMuted = participant.IsVideoMuted,
            IsScreenMuted = participant.IsScreenMuted
        };
    }
}
