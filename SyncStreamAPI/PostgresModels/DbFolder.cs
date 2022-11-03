using System.Collections.Generic;

namespace SyncStreamAPI.PostgresModels
{
    public class DbFileFolder
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<DbFile> Files { get; set; }
        public DbFileFolder()
        {
            Id = 0;
            Files = new List<DbFile>();
        }
    }
}
