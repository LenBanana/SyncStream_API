using System.Linq;
using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.Helper.SponsorBlock;
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
    public DbSet<DbServerSettings> ServerSettings { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Seeded so the row always exists and reading it never has to cope with its absence. The values are the
        // shipped defaults; an administrator changing them updates this row rather than inserting another.
        builder.Entity<DbServerSettings>().HasData(new DbServerSettings
        {
            ID = DbServerSettings.SingletonId,
            SponsorBlockSkipCategories = SponsorBlockCategories.Default.ToList()
        });

        // Every authenticated request looks a session up by this digest, so it is indexed, and unique because
        // two sessions sharing one digest would mean the random source repeated itself.
        builder.Entity<DbRememberToken>().Property(token => token.TokenHash).IsRequired();
        builder.Entity<DbRememberToken>().HasIndex(token => token.TokenHash).IsUnique();

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