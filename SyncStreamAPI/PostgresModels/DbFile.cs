using SyncStreamAPI.Helper;
using System;
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
        public DateTime Created { get; set; }
        public DbFile(string name, string fileEnding, User user)
        {
            ID = 0;
            UserID = user.ID;
            Name = name;            
            FileEnding = fileEnding;
            FileKey = user.GenerateToken(Guid.NewGuid().ToString() + name).Token;
            Created = DateTime.Now;
        }

        public DbFile()
        {
            ID = 0;
            Created = DateTime.Now;
        }
    }
}
