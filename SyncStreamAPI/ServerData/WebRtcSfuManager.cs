using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SyncStreamAPI.Models.WebRTC;

namespace SyncStreamAPI.ServerData;

/// <summary>
/// HTTP client that proxies signaling commands to the mediasoup Node.js SFU server.
/// All communication is JSON over HTTP; the actual WebRTC media flows directly
/// between browser clients and the mediasoup server (never through this .NET process).
/// </summary>
public class WebRtcSfuManager
{
    private readonly HttpClient _http;
    private readonly string _sfuBaseUrl;

    public WebRtcSfuManager(IConfiguration configuration)
    {
        _sfuBaseUrl = configuration["SfuUrl"]?.TrimEnd('/') ?? "http://localhost:3000";
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    // ---------------------------------------------------------------
    // Router capabilities
    // ---------------------------------------------------------------

    /// <summary>Returns the mediasoup router's RTP capabilities so clients can load their Device.</summary>
    public async Task<JObject> GetRtpCapabilitiesAsync()
    {
        var response = await _http.GetAsync($"{_sfuBaseUrl}/rtp-capabilities");
        response.EnsureSuccessStatusCode();
        return JObject.Parse(await response.Content.ReadAsStringAsync());
    }

    // ---------------------------------------------------------------
    // Room management
    // ---------------------------------------------------------------

    /// <summary>Creates the room on the SFU if it does not already exist.</summary>
    public async Task EnsureRoomAsync(string roomId)
    {
        var response = await _http.PostAsync($"{_sfuBaseUrl}/rooms/{Uri.EscapeDataString(roomId)}", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Closes the room and all its transports/producers/consumers.</summary>
    public async Task CloseRoomAsync(string roomId)
    {
        await _http.DeleteAsync($"{_sfuBaseUrl}/rooms/{Uri.EscapeDataString(roomId)}");
    }

    /// <summary>Returns all active producers in the room.</summary>
    public async Task<List<SfuProducerEntry>> GetProducersAsync(string roomId)
    {
        var response = await _http.GetAsync($"{_sfuBaseUrl}/rooms/{Uri.EscapeDataString(roomId)}/producers");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<List<SfuProducerEntry>>(json) ?? new List<SfuProducerEntry>();
    }

    // ---------------------------------------------------------------
    // Transport management
    // ---------------------------------------------------------------

    /// <summary>
    /// Creates a WebRTC transport in the specified room.
    /// <paramref name="producing"/> and <paramref name="consuming"/> signal the
    /// mediasoup server the intended direction so it can configure the transport.
    /// </summary>
    public async Task<SfuTransportInfo> CreateTransportAsync(string roomId, bool producing, bool consuming)
    {
        var body = new JObject
        {
            ["producing"] = producing,
            ["consuming"] = consuming
        };
        var response = await Post($"{_sfuBaseUrl}/rooms/{Uri.EscapeDataString(roomId)}/transports", body);
        return MapTransport(response);
    }

    /// <summary>Finalises the DTLS handshake for an existing transport.</summary>
    public async Task ConnectTransportAsync(string roomId, string transportId, JObject dtlsParameters)
    {
        var body = new JObject { ["dtlsParameters"] = dtlsParameters };
        var response = await Post(
            $"{_sfuBaseUrl}/rooms/{Uri.EscapeDataString(roomId)}/transports/{Uri.EscapeDataString(transportId)}/connect",
            body);
        EnsureOk(response);
    }

    /// <summary>Closes a transport and all of its producers/consumers.</summary>
    public async Task CloseTransportAsync(string roomId, string transportId)
    {
        await _http.DeleteAsync(
            $"{_sfuBaseUrl}/rooms/{Uri.EscapeDataString(roomId)}/transports/{Uri.EscapeDataString(transportId)}");
    }

    // ---------------------------------------------------------------
    // Producer management
    // ---------------------------------------------------------------

    /// <summary>
    /// Registers a new media producer on the send transport.
    /// Returns the mediasoup producer ID that viewers will use to consume the track.
    /// </summary>
    public async Task<string> ProduceAsync(
        string roomId, string transportId, string kind, JObject rtpParameters, string peerId)
    {
        var body = new JObject
        {
            ["kind"] = kind,
            ["rtpParameters"] = rtpParameters,
            ["peerId"] = peerId
        };
        var response = await Post(
            $"{_sfuBaseUrl}/rooms/{Uri.EscapeDataString(roomId)}/transports/{Uri.EscapeDataString(transportId)}/produce",
            body);
        return response["id"]?.Value<string>() ?? throw new InvalidOperationException("SFU produce returned no id");
    }

    /// <summary>Closes an existing producer (e.g. when the streamer mutes a track or disconnects).</summary>
    public async Task CloseProducerAsync(string roomId, string producerId)
    {
        await _http.DeleteAsync(
            $"{_sfuBaseUrl}/rooms/{Uri.EscapeDataString(roomId)}/producers/{Uri.EscapeDataString(producerId)}");
    }

    // ---------------------------------------------------------------
    // Consumer management
    // ---------------------------------------------------------------

    /// <summary>
    /// Creates a consumer for <paramref name="producerId"/> on a recv transport.
    /// The returned <see cref="SfuConsumerInfo"/> is forwarded to the viewer's mediasoup-client.
    /// </summary>
    public async Task<SfuConsumerInfo> ConsumeAsync(
        string roomId, string transportId, string producerId, JObject rtpCapabilities, string peerId)
    {
        var body = new JObject
        {
            ["producerId"] = producerId,
            ["rtpCapabilities"] = rtpCapabilities,
            ["peerId"] = peerId
        };
        var response = await Post(
            $"{_sfuBaseUrl}/rooms/{Uri.EscapeDataString(roomId)}/transports/{Uri.EscapeDataString(transportId)}/consume",
            body);
        return new SfuConsumerInfo
        {
            Id = response["id"]?.Value<string>() ?? string.Empty,
            ProducerId = response["producerId"]?.Value<string>() ?? string.Empty,
            Kind = response["kind"]?.Value<string>() ?? string.Empty,
            RtpParameters = response["rtpParameters"] as JObject ?? new JObject()
        };
    }

    // ---------------------------------------------------------------
    // Consumer preferred layers (simulcast / SVC)
    // ---------------------------------------------------------------

    /// <summary>
    /// Sets the preferred simulcast/SVC spatial and temporal layers for a consumer,
    /// enabling adaptive bitrate from the viewer side.
    /// </summary>
    public async Task SetPreferredLayersAsync(string roomId, string consumerId, int spatialLayer, int temporalLayer)
    {
        var body = new JObject
        {
            ["spatialLayer"] = spatialLayer,
            ["temporalLayer"] = temporalLayer
        };
        var response = await Post(
            $"{_sfuBaseUrl}/rooms/{Uri.EscapeDataString(roomId)}/consumers/{Uri.EscapeDataString(consumerId)}/preferred-layers",
            body);
        EnsureOk(response);
    }

    // ---------------------------------------------------------------
    // Server-side file streaming
    // ---------------------------------------------------------------

    /// <summary>
    /// Starts an ffmpeg-based server-side file stream into the SFU room.
    /// The SFU transcodes the file to VP9+Opus and injects it as producers
    /// that all room consumers can subscribe to — no client changes needed.
    /// Returns the video and audio producer IDs.
    /// </summary>
    public async Task<(string VideoProducerId, string AudioProducerId)> StartServerFileStreamAsync(
        string roomId, string filePath, long targetBitrate = 3_000_000)
    {
        var body = new JObject
        {
            ["filePath"]       = filePath,
            ["targetBitrate"]  = targetBitrate
        };
        var response = await Post(
            $"{_sfuBaseUrl}/rooms/{Uri.EscapeDataString(roomId)}/server-stream", body);
        return (
            response["videoProducerId"]?.Value<string>() ?? string.Empty,
            response["audioProducerId"]?.Value<string>() ?? string.Empty
        );
    }

    /// <summary>Controls an active server file stream (play / pause / seek).</summary>
    public async Task ControlServerFileStreamAsync(string roomId, string action, double? positionSec = null)
    {
        var body = new JObject {["action"] = action};
        if (positionSec.HasValue) body["position"] = positionSec.Value;
        var response = await Post(
            $"{_sfuBaseUrl}/rooms/{Uri.EscapeDataString(roomId)}/server-stream/control", body);
        EnsureOk(response);
    }

    /// <summary>Stops the active server file stream and releases SFU resources.</summary>
    public async Task StopServerFileStreamAsync(string roomId)
    {
        await _http.DeleteAsync(
            $"{_sfuBaseUrl}/rooms/{Uri.EscapeDataString(roomId)}/server-stream");
    }

    // ---------------------------------------------------------------
    // Recording
    // ---------------------------------------------------------------

    /// <summary>
    /// Tells the SFU server to start recording all producers in the room to a local file.
    /// Returns the recording info (filename, start timestamp).
    /// </summary>
    public async Task<SfuRecordingInfo> StartRecordingAsync(string roomId)
    {
        var response = await Post($"{_sfuBaseUrl}/rooms/{Uri.EscapeDataString(roomId)}/recording/start", null);
        return new SfuRecordingInfo
        {
            RoomId = roomId,
            Filename = response["filename"]?.Value<string>() ?? string.Empty,
            StartedAtUnixMs = response["startedAtUnixMs"]?.Value<long>() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    /// <summary>Stops an active recording and returns the filename of the finished file.</summary>
    public async Task<string> StopRecordingAsync(string roomId)
    {
        var response = await Post($"{_sfuBaseUrl}/rooms/{Uri.EscapeDataString(roomId)}/recording/stop", null);
        return response["filename"]?.Value<string>() ?? string.Empty;
    }

    // ---------------------------------------------------------------
    // Health check
    // ---------------------------------------------------------------

    /// <summary>Returns true if the SFU server is reachable and healthy.</summary>
    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            var response = await _http.GetAsync($"{_sfuBaseUrl}/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private async Task<JObject> Post(string url, JObject? body)
    {
        var content = new StringContent(
            body?.ToString(Formatting.None) ?? "{}",
            Encoding.UTF8,
            "application/json");
        var response = await _http.PostAsync(url, content);
        response.EnsureSuccessStatusCode();
        return JObject.Parse(await response.Content.ReadAsStringAsync());
    }

    private static SfuTransportInfo MapTransport(JObject j) => new()
    {
        Id = j["id"]?.Value<string>() ?? string.Empty,
        IceParameters = j["iceParameters"] as JObject ?? new JObject(),
        IceCandidates = j["iceCandidates"] as JArray ?? new JArray(),
        DtlsParameters = j["dtlsParameters"] as JObject ?? new JObject()
    };

    private static void EnsureOk(JObject j)
    {
        if (j["ok"]?.Value<bool>() == false)
            throw new InvalidOperationException($"SFU returned error: {j}");
    }
}
