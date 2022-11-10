namespace SyncStreamAPI.PostgresModels
{
    public class DbFolderUserShare
    {
        public int Id { get; set; }
        public int DbUserId { get; set; }
        public DbUser User { get; set; }
        public int DbFolderId { get; set; }
        public DbFileFolder Folder { get; set; }
    }
}
