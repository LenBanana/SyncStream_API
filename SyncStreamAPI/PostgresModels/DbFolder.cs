using System.Collections.Generic;

namespace SyncStreamAPI.PostgresModels
{
    public class DbFileFolder
    {
        public int Id { get; set; }
        public int? UserId { get; set; }
        public string Name { get; set; }
        public List<DbFile> Files { get; set; }
#nullable enable
        public DbFileFolder? Parent { get; set; }
        public int? ParentId { get; set; }
        public virtual ICollection<DbFileFolder> Children { get; set; }
#nullable disable
        public DbFileFolder()
        {
            Id = 0;
            Files = new List<DbFile>();
        }

        public DbFileFolder(int id)
        {
            Id = id;
        }

        public DbFileFolder(string name)
        {
            Name = name;
        }

        public DbFileFolder(string name, int parent, int userId)
        {
            Id = 0;
            Name = name;
            ParentId = parent;
            UserId = userId;
        }
    }
}
