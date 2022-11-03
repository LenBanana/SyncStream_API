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

        public DbSet<User> Users { get; set; }
        public DbSet<DbFile> Files { get; set; }
        public DbSet<DbFileFolder> Folders { get; set; }
        public DbSet<RememberToken> RememberTokens { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
        }
    }
}
