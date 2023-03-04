using SyncStreamAPI.Helper;
using SyncStreamAPI.PostgresModels;
using System;

namespace SyncStreamAPI.DTOModel
{
    public class FileDto
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public string FileEnding { get; set; }
        public string FileKey { get; set; }
        public DateTime Created { get; set; }
        public int FileFolderId { get; set; }
        public long Length => !System.IO.File.Exists($"{General.FilePath}/{FileKey}{FileEnding}") ? 0 : new System.IO.FileInfo($"{General.FilePath}/{FileKey}{FileEnding}").Length;

        public FileDto(DbFile file)
        {
            ID = file.ID;
            Name = file.Name;
            FileEnding = file.FileEnding;
            FileKey = file.FileKey;
            Created = file.Created;
            FileFolderId = file.DbFileFolderId;
        }
    }
}
