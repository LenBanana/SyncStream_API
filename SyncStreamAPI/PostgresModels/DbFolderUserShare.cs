namespace SyncStreamAPI.PostgresModels
{
    public class DbFolderUserShare
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DbUser User { get; set; }
        public int FolderId { get; set; }
        public DbFileFolder Folder { get; set; }
    }
}
