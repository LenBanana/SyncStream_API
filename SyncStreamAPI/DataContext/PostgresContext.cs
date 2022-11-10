using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.PostgresModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.DataContext
{
    public class PostgresContext : DbContext
    {
        public PostgresContext(DbContextOptions<PostgresContext> options) : base(options)
        {

        }

        public DbSet<DbUser> Users { get; set; }
        public DbSet<DbFile> Files { get; set; }
        public DbSet<DbFileFolder> Folders { get; set; }
        public DbSet<DbFolderUserShare> FolderShare { get; set; }
        public DbSet<DbRememberToken> RememberTokens { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<DbFileFolder>().HasMany(x => x.Children).WithOne(x => x.Parent);
            builder.Entity<DbFileFolder>().HasMany(x => x.Files);
        }
    }
}
