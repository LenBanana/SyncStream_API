using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.MariaModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.DataContext
{
    public class MariaContext : DbContext
    {
        public MariaContext(DbContextOptions<MariaContext> options) : base(options)
        {

        }

        public DbSet<User> Users { get; set; }
        public DbSet<RememberToken> RememberTokens { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
        }
    }
}
