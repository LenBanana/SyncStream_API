using System.Threading.Tasks;
using SyncStreamAPI.Models.WebRTC;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    /// <summary>
    /// Pushed to everyone else in an SFU room when a new producer becomes available.
    /// The receiver should call <c>Consume</c> for this producer.
    /// </summary>
    Task sfuNewProducer(SfuNewProducerNotification notification);

    /// <summary>Pushed to everyone else in an SFU room when a producer is closed.</summary>
    Task sfuProducerClosed(string sfuRoomId, string producerId);

    /// <summary>Pushed to the stage's SFU room when a recording has started.</summary>
    Task sfuRecordingStarted(SfuRecordingInfo info);

    /// <summary>Pushed to the stage's SFU room when the recording has stopped, with the finished file.</summary>
    Task sfuRecordingStopped(string roomId, string filename);
}
