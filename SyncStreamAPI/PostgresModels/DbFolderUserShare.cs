namespace SyncStreamAPI.PostgresModels
{
    public class DbFolderUserShare
    {
        public int Id { get; set; }
        public int DbUserID { get; set; }
        public DbUser User { get; set; }
        public int DbFolderID { get; set; }
        public DbFileFolder Folder { get; set; }
    }
}
