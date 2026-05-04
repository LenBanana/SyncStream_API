using Newtonsoft.Json.Linq;

namespace SyncStreamAPI.Models.WebRTC;

/// <summary>Direction of a WebRTC transport from the mediasoup perspective.</summary>
public enum SfuTransportDirection
{
    Send,
    Recv
}

/// <summary>
/// Parameters returned when a WebRTC transport is created inside mediasoup.
/// Sent to the client so it can call device.createSendTransport / createRecvTransport.
/// </summary>
public class SfuTransportInfo
{
    public string Id { get; set; } = string.Empty;
    /// <summary>Serialised as-is from the mediasoup JSON response.</summary>
    public JObject IceParameters { get; set; } = new();
    public JArray IceCandidates { get; set; } = new();
    public JObject DtlsParameters { get; set; } = new();
}

/// <summary>
/// Sent back to the caller of <c>Produce</c>.
/// </summary>
public class SfuProducerInfo
{
    public string Id { get; set; } = string.Empty;
}

/// <summary>
/// Sent back to the caller of <c>Consume</c>; passed directly to
/// <c>recvTransport.consume()</c> in mediasoup-client.
/// </summary>
public class SfuConsumerInfo
{
    public string Id { get; set; } = string.Empty;
    public string ProducerId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public JObject RtpParameters { get; set; } = new();
}

/// <summary>
/// An active producer entry returned by <c>GET /rooms/:roomId/producers</c>.
/// </summary>
public class SfuProducerEntry
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    /// <summary>SignalR connectionId of the peer that created this producer.</summary>
    public string PeerId { get; set; } = string.Empty;
}

/// <summary>
/// Pushed to all viewers in the room when a new producer becomes available.
/// </summary>
public class SfuNewProducerNotification
{
    public string ProducerId { get; set; } = string.Empty;
    public string PeerId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    /// <summary>The SFU room this producer belongs to (includes prefix, e.g. "voip-roomId").</summary>
    public string RoomId { get; set; } = string.Empty;
}

/// <summary>Preferred spatial/temporal simulcast layers for a consumer.</summary>
public class SfuPreferredLayers
{
    public int SpatialLayer { get; set; }
    public int TemporalLayer { get; set; }
}

/// <summary>Information about an active or completed SFU recording.</summary>
public class SfuRecordingInfo
{
    public string RoomId { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public long StartedAtUnixMs { get; set; }
}
