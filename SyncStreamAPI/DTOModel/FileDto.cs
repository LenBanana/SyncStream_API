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
        public long Length => new System.IO.FileInfo($"{General.FilePath}/{FileKey}{FileEnding}").Length;
        public FileDto(int id, string name, string fileEnding, string fileKey, DateTime created)
        {
            ID = id;
            Name = name;
            FileEnding = fileEnding;
            FileKey = fileKey;
            Created = created;
        }

        public FileDto(DbFile file)
        {
            ID = file.ID;
            Name = file.Name;
            FileEnding = file.FileEnding;
            FileKey = file.FileKey;
            Created = file.Created;
        }
    }
}
