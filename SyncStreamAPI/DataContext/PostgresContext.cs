using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.DataContext
{
    public class PostgresContext : DbContext
    {
        public PostgresContext(DbContextOptions<PostgresContext> options) : base(options)
        {

        }

        public DbSet<DbUser> Users { get; set; }
        public DbSet<DbRoom> Rooms { get; set; }
        public DbSet<DbFile> Files { get; set; }
        public DbSet<DbFileFolder> Folders { get; set; }
        public DbSet<DbFolderUserShare> FolderShare { get; set; }
        public DbSet<DbRememberToken> RememberTokens { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<DbFileFolder>().HasMany(x => x.Children).WithOne(x => x.Parent);
            builder.Entity<DbFileFolder>().HasMany(x => x.Files);
            //builder.Entity<DbUser>().Property(e => e.PrivateMessages)
            //.HasConversion(
            //    v => JsonConvert.SerializeObject(v),
            //    v => JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(v))
            //.HasColumnType("jsonb");
        }
    }
}
