using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.DTOModel
{
    public class FileDto
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public string FileEnding { get; set; }
        public long Length { get; set; }
        public FileDto(int id, string name, string fileEnding)
        {
            ID = id;
            Name = name;
            FileEnding = fileEnding;
        }

        public FileDto(DbFile file)
        {
            ID = file.ID;
            Name = file.Name;
            FileEnding = file.FileEnding;
            Length = file.VideoFile.Length;
        }
    }
}
