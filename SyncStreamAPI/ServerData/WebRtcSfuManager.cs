using System;
using System.Collections.Generic;
using System.Net;
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
/// <remarks>
/// Every failure, whether the SFU refused the request or could not be reached, surfaces as
/// <see cref="SfuRequestException"/>, which the hub turns into a message the person can read.
/// </remarks>
public class WebRtcSfuManager
{
    private readonly HttpClient _http;
    private readonly string _sfuBaseUrl;
    private JObject? _rtpCapabilities;

    public WebRtcSfuManager(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _sfuBaseUrl = configuration["SfuUrl"]?.TrimEnd('/') ?? "http://localhost:3000";
    }

    private string RoomUrl(string sfuRoomId) => $"{_sfuBaseUrl}/rooms/{Uri.EscapeDataString(sfuRoomId)}";

    // ---------------------------------------------------------------
    // Router capabilities
    // ---------------------------------------------------------------

    /// <summary>
    /// Returns the mediasoup router's RTP capabilities so clients can load their Device.
    /// They are fixed by the SFU's codec list, so the first answer is kept.
    /// </summary>
    public async Task<JObject> GetRtpCapabilitiesAsync()
    {
        _rtpCapabilities ??= await Request(HttpMethod.Get, $"{_sfuBaseUrl}/rtp-capabilities");
        return _rtpCapabilities;
    }

    // ---------------------------------------------------------------
    // Room management
    // ---------------------------------------------------------------

    /// <summary>Creates the room on the SFU if it does not already exist. The SFU closes it once it is idle.</summary>
    public async Task EnsureRoomAsync(string sfuRoomId)
    {
        await Request(HttpMethod.Post, RoomUrl(sfuRoomId));
    }

    /// <summary>Returns all active producers in the room; none for a room that does not exist.</summary>
    public async Task<List<SfuProducerEntry>> GetProducersAsync(string sfuRoomId)
    {
        var json = await RequestText(HttpMethod.Get, $"{RoomUrl(sfuRoomId)}/producers");
        return JsonConvert.DeserializeObject<List<SfuProducerEntry>>(json) ?? new List<SfuProducerEntry>();
    }

    // ---------------------------------------------------------------
    // Transport management
    // ---------------------------------------------------------------

    /// <summary>
    /// Creates a WebRTC transport in the specified room and returns what the client needs to
    /// create its end of it.
    /// </summary>
    public async Task<SfuTransportInfo> CreateTransportAsync(string sfuRoomId)
    {
        var response = await Request(HttpMethod.Post, $"{RoomUrl(sfuRoomId)}/transports");
        return new SfuTransportInfo
        {
            Id = response["id"]?.Value<string>() ?? throw new InvalidOperationException("SFU created a transport with no id"),
            IceParameters = response["iceParameters"] as JObject ?? new JObject(),
            IceCandidates = response["iceCandidates"] as JArray ?? new JArray(),
            DtlsParameters = response["dtlsParameters"] as JObject ?? new JObject()
        };
    }

    /// <summary>Finalises the DTLS handshake for an existing transport.</summary>
    public async Task ConnectTransportAsync(string sfuRoomId, string transportId, JObject dtlsParameters)
    {
        await Request(HttpMethod.Post,
            $"{RoomUrl(sfuRoomId)}/transports/{Uri.EscapeDataString(transportId)}/connect",
            new JObject { ["dtlsParameters"] = dtlsParameters });
    }

    /// <summary>New ICE credentials for a transport whose connection failed.</summary>
    public async Task<JObject> RestartIceAsync(string sfuRoomId, string transportId)
    {
        var response = await Request(HttpMethod.Post,
            $"{RoomUrl(sfuRoomId)}/transports/{Uri.EscapeDataString(transportId)}/restart-ice");
        return response["iceParameters"] as JObject
               ?? throw new InvalidOperationException("SFU restarted ICE but returned no parameters");
    }

    /// <summary>Closes a transport and all of its producers/consumers.</summary>
    /// <returns>False when the SFU no longer had it.</returns>
    public Task<bool> CloseTransportAsync(string sfuRoomId, string transportId)
    {
        return Delete($"{RoomUrl(sfuRoomId)}/transports/{Uri.EscapeDataString(transportId)}");
    }

    // ---------------------------------------------------------------
    // Producer management
    // ---------------------------------------------------------------

    /// <summary>
    /// Registers a new media producer on the send transport.
    /// Returns the mediasoup producer ID that viewers will use to consume the track.
    /// </summary>
    public async Task<string> ProduceAsync(
        string sfuRoomId, string transportId, string kind, JObject rtpParameters, string peerId)
    {
        var body = new JObject
        {
            ["kind"] = kind,
            ["rtpParameters"] = rtpParameters,
            ["peerId"] = peerId
        };
        var response = await Request(HttpMethod.Post,
            $"{RoomUrl(sfuRoomId)}/transports/{Uri.EscapeDataString(transportId)}/produce", body);
        return response["id"]?.Value<string>() ?? throw new InvalidOperationException("SFU produce returned no id");
    }

    /// <summary>Closes an existing producer.</summary>
    /// <returns>False when the SFU no longer had it.</returns>
    public Task<bool> CloseProducerAsync(string sfuRoomId, string producerId)
    {
        return Delete($"{RoomUrl(sfuRoomId)}/producers/{Uri.EscapeDataString(producerId)}");
    }

    // ---------------------------------------------------------------
    // Consumer management
    // ---------------------------------------------------------------

    /// <summary>
    /// Creates a consumer for <paramref name="producerId"/> on a recv transport.
    /// The returned <see cref="SfuConsumerInfo"/> is forwarded to the viewer's mediasoup-client.
    /// </summary>
    public async Task<SfuConsumerInfo> ConsumeAsync(
        string sfuRoomId, string transportId, string producerId, JObject rtpCapabilities, string peerId)
    {
        var body = new JObject
        {
            ["producerId"] = producerId,
            ["rtpCapabilities"] = rtpCapabilities,
            ["peerId"] = peerId
        };
        var response = await Request(HttpMethod.Post,
            $"{RoomUrl(sfuRoomId)}/transports/{Uri.EscapeDataString(transportId)}/consume", body);
        return new SfuConsumerInfo
        {
            Id = response["id"]?.Value<string>() ?? string.Empty,
            ProducerId = response["producerId"]?.Value<string>() ?? string.Empty,
            Kind = response["kind"]?.Value<string>() ?? string.Empty,
            RtpParameters = response["rtpParameters"] as JObject ?? new JObject()
        };
    }

    // ---------------------------------------------------------------
    // Server-side file streaming
    // ---------------------------------------------------------------

    /// <summary>
    /// Starts an ffmpeg-based server-side file stream into the SFU room.
    /// The SFU transcodes the file to H.264+Opus and injects it as producers
    /// that all room consumers can subscribe to.
    /// </summary>
    /// <param name="growing">The file is still being uploaded; running out of data is not the end.</param>
    public async Task<ServerFileStreamStatus> StartServerFileStreamAsync(
        string sfuRoomId, string filePath, bool growing, long targetBitrate = 3_000_000)
    {
        var body = new JObject
        {
            ["filePath"] = filePath,
            ["targetBitrate"] = targetBitrate,
            ["growing"] = growing
        };
        return ParseStreamStatus(await Request(HttpMethod.Post, $"{RoomUrl(sfuRoomId)}/server-stream", body));
    }

    /// <returns>The stream's state, or null when the room has no server stream.</returns>
    public async Task<ServerFileStreamStatus?> GetServerFileStreamAsync(string sfuRoomId)
    {
        var response = await Send(HttpMethod.Get, $"{RoomUrl(sfuRoomId)}/server-stream", null);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return ParseStreamStatus(JObject.Parse(await ReadSuccess(response)));
    }

    /// <summary>Controls an active server file stream (play / pause / seek / upload-complete).</summary>
    public async Task ControlServerFileStreamAsync(string sfuRoomId, string action, double? positionSec = null)
    {
        var body = new JObject { ["action"] = action };
        if (positionSec.HasValue) body["position"] = positionSec.Value;
        await Request(HttpMethod.Post, $"{RoomUrl(sfuRoomId)}/server-stream/control", body);
    }

    /// <summary>Stops the active server file stream and releases SFU resources.</summary>
    /// <returns>False when the SFU had no stream to stop.</returns>
    public Task<bool> StopServerFileStreamAsync(string sfuRoomId)
    {
        return Delete($"{RoomUrl(sfuRoomId)}/server-stream");
    }

    // ---------------------------------------------------------------
    // Recording
    // ---------------------------------------------------------------

    /// <summary>
    /// Tells the SFU server to start recording all producers in the room to a local file.
    /// Returns the recording info (filename, start timestamp).
    /// </summary>
    public async Task<SfuRecordingInfo> StartRecordingAsync(string roomId, string sfuRoomId)
    {
        var response = await Request(HttpMethod.Post, $"{RoomUrl(sfuRoomId)}/recording/start");
        return new SfuRecordingInfo
        {
            RoomId = roomId,
            Filename = response["filename"]?.Value<string>() ?? string.Empty,
            StartedAtUnixMs = response["startedAtUnixMs"]?.Value<long>() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    /// <summary>Stops an active recording and returns the filename of the finished file.</summary>
    public async Task<string> StopRecordingAsync(string sfuRoomId)
    {
        var response = await Request(HttpMethod.Post, $"{RoomUrl(sfuRoomId)}/recording/stop");
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
            var response = await Send(HttpMethod.Get, $"{_sfuBaseUrl}/health", null);
            return response.IsSuccessStatusCode;
        }
        catch (SfuRequestException ex)
        {
            Console.WriteLine($"[SFU] health check failed: {ex.Message}");
            return false;
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    /// <summary>Sends a request, turning "could not reach the SFU" into the same exception as a refusal.</summary>
    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, JObject? body)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body != null || method == HttpMethod.Post)
            request.Content = new StringContent(body?.ToString(Formatting.None) ?? "{}", Encoding.UTF8, "application/json");
        try
        {
            return await _http.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            throw new SfuRequestException(HttpStatusCode.ServiceUnavailable,
                "The media server is not reachable right now.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new SfuRequestException(HttpStatusCode.GatewayTimeout,
                "The media server did not answer in time.", ex);
        }
    }

    private static async Task<string> ReadSuccess(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new SfuRequestException(response.StatusCode, ReadError(text));
        return text;
    }

    private async Task<string> RequestText(HttpMethod method, string url, JObject? body = null)
    {
        return await ReadSuccess(await Send(method, url, body));
    }

    private async Task<JObject> Request(HttpMethod method, string url, JObject? body = null)
    {
        return JObject.Parse(await RequestText(method, url, body));
    }

    /// <returns>True when deleted, false when the SFU no longer had it.</returns>
    private async Task<bool> Delete(string url)
    {
        var response = await Send(HttpMethod.Delete, url, null);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        await ReadSuccess(response);
        return true;
    }

    private static string ReadError(string json)
    {
        try
        {
            return JObject.Parse(json)["error"]?.Value<string>() ?? json;
        }
        catch (JsonReaderException)
        {
            return json;
        }
    }

    private static ServerFileStreamStatus ParseStreamStatus(JObject json)
    {
        return new ServerFileStreamStatus(
            json["state"]?.Value<string>() ?? "failed",
            json["buffering"]?.Value<bool>() ?? false,
            json["positionSec"]?.Value<double>() ?? 0,
            json["error"]?.Value<string>(),
            json["videoProducerId"]?.Value<string>() ?? string.Empty,
            json["audioProducerId"]?.Value<string>());
    }
}

/// <summary>Where a server-side file stream is: playing, paused, ended or failed, and how far in.</summary>
public sealed record ServerFileStreamStatus(
    string State,
    bool Buffering,
    double PositionSec,
    string? Error,
    string VideoProducerId,
    string? AudioProducerId)
{
    public bool IsOver => State is "ended" or "failed";
}

/// <summary>The SFU refused a request or could not be reached; the message says which, in plain words.</summary>
public sealed class SfuRequestException : Exception
{
    public SfuRequestException(HttpStatusCode statusCode, string message, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
