using System;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Org.WebRtc;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models.WebRTC;

namespace SyncStreamAPI.ServerData;

public class WebRtcSfuManager
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    public RTCConfiguration WebRtcConfiguration { get; private set; }
    public RTCPeerConnection PeerConnection { get; private set; }

    public WebRtcSfuManager(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        Setup();
    }

    private void Setup()
    {
        var credentials = General.GenerateTemporaryCredentials(_configuration);
        var stunServer = _configuration.GetSection("WebRtcStunServer").Value;
        var turnServer = _configuration.GetSection("WebRtcTurnServer").Value;
        var configuration = new RTCConfiguration()
        {
            IceServers = new[]
            {
                new RTCIceServer()
                {
                    Urls = new[] { stunServer }
                },
                new RTCIceServer()
                {
                    Urls = new[] { turnServer },
                    Username = credentials.Username,
                    Credential = credentials.Password
                }
            }
        };
        WebRtcConfiguration = configuration;
        PeerConnection = CreatePeerConnection();
    }

    private RTCPeerConnection CreatePeerConnection()
    {
        var peerConnection = new RTCPeerConnection(WebRtcConfiguration);
        SetupNewPeerConnection(peerConnection);
        return peerConnection;
    }

    private void SetupNewPeerConnection(RTCPeerConnection peerConnection)
    {
        peerConnection.OnIceCandidate += (candidate) =>
        {
            if (candidate == null) return;
            
        };
        peerConnection.OnNegotiationNeeded += () =>
        {
            
        };
        peerConnection.OnIceConnectionStateChange += () =>
        {
            
        };
        peerConnection.OnTrack += (trackEvent) =>
        {
            
        };
    }
    
    public async void CreateOffer(RTCPeerConnection peerConnection)
    {
        var offer = await peerConnection.CreateOffer(new RTCOfferOptions()
        {
            IceRestart = false,
            OfferToReceiveAudio = true,
            OfferToReceiveVideo = true
        });
        await peerConnection.SetLocalDescription(offer);
    }
    
    public async void CreateAnswer(RTCPeerConnection peerConnection)
    {
        var answer = await peerConnection.CreateAnswer(new RTCAnswerOptions()
        {
            VoiceActivityDetection = true
        });
        await peerConnection.SetLocalDescription(answer);
    }
    
    public async Task SendOfferToParticipant(string participantId, RTCSessionDescription offer)
    {
        var scope = _serviceProvider.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
        await hubContext.Clients.Client(participantId).receiveOfferFromServer(participantId, offer);
    }
}