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
    public DbSet<DbServerSettings> ServerSettings { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Deliberately not seeded through HasData. Seed data is for rows the model owns and expects to stay as
        // declared, and this row is edited by an administrator: EF would compare it against the declaration and
        // could emit an update that puts their choice back. It is inserted once by its migration, and read
        // through a helper that creates it when absent. HasData also rebuilt the category list on every model
        // build, which made the model look different each time and stopped the application starting at all.

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