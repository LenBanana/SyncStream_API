using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.DataContext;

public class PostgresContext : DbContext
{
    public PostgresContext(DbContextOptions<PostgresContext> options) : base(options)
    {
    }

    public DbSet<DbApplicationVersion> AppVersions { get; set; }
    public DbSet<DbUser> Users { get; set; }
    public DbSet<DbRoom> Rooms { get; set; }
    public DbSet<DbFile> Files { get; set; }
    public DbSet<DbFileFolder> Folders { get; set; }
    public DbSet<DbFolderUserShare> FolderShare { get; set; }
    public DbSet<DbRememberToken> RememberTokens { get; set; }
    public DbSet<DbMessage> Messages { get; set; }
    public DbSet<DbConversation> Conversations { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<DbFileFolder>().HasMany(x => x.Children).WithOne(x => x.Parent);
        builder.Entity<DbFileFolder>().HasMany(x => x.Files);

        //Build the relationship between a conversation and the users involved
        builder.Entity<DbConversation>().HasOne(x => x.Sender).WithMany(x => x.StartedConversations)
            .HasForeignKey(x => x.SenderId);
        builder.Entity<DbConversation>().HasOne(x => x.Receiver).WithMany(x => x.ReceivedConversations)
            .HasForeignKey(x => x.ReceiverId);

        //builder.Entity<DbUser>().Property(e => e.PrivateMessages)
        //.HasConversion(
        //    v => JsonConvert.SerializeObject(v),
        //    v => JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(v))
        //.HasColumnType("jsonb");
    }
}