namespace SyncStreamAPI.PostgresModels
{
    public class DbFile
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public string FileEnding { get; set; }
        public byte[] VideoFile { get; set; }
        public DbFile(string name, byte[] videoFile, string fileEnding)
        {
            ID = 0;
            Name = name;
            VideoFile = videoFile;
            FileEnding = fileEnding;
        }

        public DbFile()
        {
            ID = 0;
        }
    }
}
