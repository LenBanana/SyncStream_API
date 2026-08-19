using System.Collections.Generic;
using System.Threading.Tasks;
using SyncStreamAPI.DTOModel;
using SyncStreamAPI.Models;
using SyncStreamAPI.Wire.Users;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task savedToDb(string id);
    Task getDownloads(List<FileDto> files);
    Task getFolderUsers(List<UserSummaryDto> users);
    Task getFolders(FolderDto folder);
    Task getFileInfo(DownloadFileInfo info);
    Task getStorageInfo(List<FileStorageInfo> info);
    Task getFolderFiles(List<FileDto> files);
    Task updateFolders(FileDto file);
    Task downloadListen(string id);
    Task downloadRemoved(string id);
    Task downloadProgress(DownloadInfo info);
    Task downloadFinished(string id);
    Task browserResults(List<string> urls);
}