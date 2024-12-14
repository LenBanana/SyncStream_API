using System.Threading.Tasks;
using SyncStreamAPI.Models.StreamExtraction;

namespace SyncStreamAPI.Models.Interfaces;

public interface IStreamDownloader
{
    Task<DownloadExtract> GetDownloadLink(DownloadClientValue client);
}