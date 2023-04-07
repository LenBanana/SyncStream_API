using SyncStreamAPI.Helper;
using System;
using System.Diagnostics.CodeAnalysis;

namespace SyncStreamAPI.PostgresModels
{
    public class DbFile
    {
        public int ID { get; set; }
        public int DbUserID { get; set; }
        public string Name { get; set; }
        public string FileEnding { get; set; }
        [NotNull]
        public string FileKey { get; set; }
        public DateTime Created { get; set; }
        public int DbFileFolderId { get; set; }
        public bool Temporary { get; set; }
        public DbFile(string name, string fileEnding, DbUser user, bool temporary = false)
        {
            ID = 0;
            DbFileFolderId = 1;
            DbUserID = user == null ? -1 : user.ID;
            Name = name;            
            FileEnding = fileEnding;
            FileKey = user?.GenerateToken(Guid.NewGuid().ToString() + name).Token;
            Created = DateTime.Now;
            Temporary = temporary;
        }

        public DbFile()
        {
            ID = 0;
            Created = DateTime.Now;
        }

        public string GetPath()
        {
            var filePath = $"{(Temporary ? General.TemporaryFilePath : General.FilePath)}/{FileKey}{FileEnding}".Replace('\\', '/');
            return filePath;
        }
    }
}
