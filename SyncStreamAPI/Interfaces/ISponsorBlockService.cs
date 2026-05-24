using System.Threading;
using System.Threading.Tasks;
using SyncStreamAPI.DTOModel;

namespace SyncStreamAPI.Interfaces;

public interface ISponsorBlockService
{
    Task<SponsorBlockSegmentsResponseDto> GetSegmentsAsync(string videoId, CancellationToken cancellationToken = default);
}