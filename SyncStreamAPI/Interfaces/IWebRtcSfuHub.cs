using System.Threading.Tasks;
using SyncStreamAPI.Models.WebRTC;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    /// <summary>
    /// Pushed to all peers in the SFU room when a new producer becomes available.
    /// The viewer should immediately call <c>Consume</c> for this producer.
    /// </summary>
    Task sfuNewProducer(SfuNewProducerNotification notification);

    /// <summary>
    /// Pushed to all peers in the SFU room when a producer is closed
    /// (e.g. streamer disconnected or muted a track).
    /// </summary>
    Task sfuProducerClosed(string producerId);

    /// <summary>Pushed to the SFU room when a recording has started.</summary>
    Task sfuRecordingStarted(SfuRecordingInfo info);

    /// <summary>Pushed to the SFU room when the recording has stopped.</summary>
    Task sfuRecordingStopped(string roomId);
}
