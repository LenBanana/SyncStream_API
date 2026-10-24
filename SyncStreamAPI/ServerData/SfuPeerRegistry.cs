using System;
using System.Collections.Generic;
using System.Linq;

namespace SyncStreamAPI.ServerData;

/// <summary>The SFU resources a connection held in one SFU room, handed back when it leaves.</summary>
public sealed record SfuPeerResources(string SfuRoomId, IReadOnlyList<string> Transports, IReadOnlyList<string> Producers);

/// <summary>
///     Which SFU rooms, transports and producers each connection owns.
/// </summary>
/// <remarks>
///     Every transport and producer id is visible to more people than its owner: producer ids are announced to
///     everyone in the SFU room so they can consume them. The hub used to act on whatever id it was given, so any
///     viewer could close the streamer's producer. Ownership is checked here instead, and leaving hands back
///     exactly what has to be closed.
/// </remarks>
public sealed class SfuPeerRegistry
{
    /// <summary>More than a peer ever needs (audio, a video, and a replacement while switching sources).</summary>
    public const int MaxProducersPerRoom = 4;

    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, PeerRoom>> _byConnection = new(StringComparer.Ordinal);

    private sealed class PeerRoom
    {
        public HashSet<string> SendTransports { get; } = new(StringComparer.Ordinal);
        public HashSet<string> RecvTransports { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Producers { get; } = new(StringComparer.Ordinal);
    }

    public void Join(string connectionId, string sfuRoomId)
    {
        lock (_gate)
        {
            if (!_byConnection.TryGetValue(connectionId, out var rooms))
                _byConnection[connectionId] = rooms = new Dictionary<string, PeerRoom>(StringComparer.Ordinal);
            if (!rooms.ContainsKey(sfuRoomId)) rooms[sfuRoomId] = new PeerRoom();
        }
    }

    public bool HasJoined(string connectionId, string sfuRoomId)
    {
        lock (_gate)
        {
            return Find(connectionId, sfuRoomId) != null;
        }
    }

    /// <returns>False when the connection has not joined the room.</returns>
    public bool AddTransport(string connectionId, string sfuRoomId, string transportId, bool sending)
    {
        lock (_gate)
        {
            var room = Find(connectionId, sfuRoomId);
            if (room == null) return false;
            (sending ? room.SendTransports : room.RecvTransports).Add(transportId);
            return true;
        }
    }

    public bool OwnsSendTransport(string connectionId, string sfuRoomId, string transportId)
    {
        lock (_gate)
        {
            return Find(connectionId, sfuRoomId)?.SendTransports.Contains(transportId) == true;
        }
    }

    public bool OwnsRecvTransport(string connectionId, string sfuRoomId, string transportId)
    {
        lock (_gate)
        {
            return Find(connectionId, sfuRoomId)?.RecvTransports.Contains(transportId) == true;
        }
    }

    public bool OwnsTransport(string connectionId, string sfuRoomId, string transportId)
    {
        return OwnsSendTransport(connectionId, sfuRoomId, transportId)
               || OwnsRecvTransport(connectionId, sfuRoomId, transportId);
    }

    public bool CanAddProducer(string connectionId, string sfuRoomId)
    {
        lock (_gate)
        {
            var room = Find(connectionId, sfuRoomId);
            return room != null && room.Producers.Count < MaxProducersPerRoom;
        }
    }

    public void AddProducer(string connectionId, string sfuRoomId, string producerId)
    {
        lock (_gate)
        {
            Find(connectionId, sfuRoomId)?.Producers.Add(producerId);
        }
    }

    /// <returns>True when the producer was the caller's.</returns>
    public bool RemoveProducer(string connectionId, string sfuRoomId, string producerId)
    {
        lock (_gate)
        {
            return Find(connectionId, sfuRoomId)?.Producers.Remove(producerId) == true;
        }
    }

    /// <summary>Forgets the connection's place in one SFU room and returns what it held there.</summary>
    public SfuPeerResources? Leave(string connectionId, string sfuRoomId)
    {
        lock (_gate)
        {
            if (!_byConnection.TryGetValue(connectionId, out var rooms)) return null;
            if (!rooms.Remove(sfuRoomId, out var room)) return null;
            if (rooms.Count == 0) _byConnection.Remove(connectionId);
            return ToResources(sfuRoomId, room);
        }
    }

    /// <summary>Forgets the connection's place in every SFU room matching the filter.</summary>
    public IReadOnlyList<SfuPeerResources> LeaveWhere(string connectionId, Func<string, bool> sfuRoomFilter)
    {
        lock (_gate)
        {
            if (!_byConnection.TryGetValue(connectionId, out var rooms)) return Array.Empty<SfuPeerResources>();

            var leaving = rooms.Where(entry => sfuRoomFilter(entry.Key)).ToList();
            foreach (var entry in leaving) rooms.Remove(entry.Key);
            if (rooms.Count == 0) _byConnection.Remove(connectionId);
            return leaving.Select(entry => ToResources(entry.Key, entry.Value)).ToList();
        }
    }

    private PeerRoom? Find(string connectionId, string sfuRoomId)
    {
        return _byConnection.TryGetValue(connectionId, out var rooms) && rooms.TryGetValue(sfuRoomId, out var room)
            ? room
            : null;
    }

    private static SfuPeerResources ToResources(string sfuRoomId, PeerRoom room)
    {
        return new SfuPeerResources(
            sfuRoomId,
            room.SendTransports.Concat(room.RecvTransports).ToList(),
            room.Producers.ToList());
    }
}
