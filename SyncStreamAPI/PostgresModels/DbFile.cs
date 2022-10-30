using SyncStreamAPI.Helper;
using System.Diagnostics.CodeAnalysis;

namespace SyncStreamAPI.PostgresModels
{
    public class DbFile
    {
        public int ID { get; set; }
        public int UserID { get; set; }
        public string Name { get; set; }
        public string FileEnding { get; set; }
        [NotNull]
        public string FileKey { get; set; }
        public DbFile(string name, string fileEnding, User user)
        {
            ID = 0;
            UserID = user.ID;
            Name = name;            
            FileEnding = fileEnding;
            FileKey = user.GenerateToken(name).Token;
        }

        public DbFile()
        {
            ID = 0;
        }
    }
}
