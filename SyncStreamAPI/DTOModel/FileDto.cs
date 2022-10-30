using SyncStreamAPI.Helper;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.DTOModel
{
    public class FileDto
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public string FileEnding { get; set; }
        public string FileKey { get; set; }
        public long Length => new System.IO.FileInfo($"{General.FilePath}\\{FileKey}{FileEnding}").Length;
        public FileDto(int id, string name, string fileEnding, string fileKey)
        {
            ID = id;
            Name = name;
            FileEnding = fileEnding;
            FileKey = fileKey;
        }

        public FileDto(DbFile file)
        {
            ID = file.ID;
            Name = file.Name;
            FileEnding = file.FileEnding;
            FileKey = file.FileKey;
        }
    }
}
