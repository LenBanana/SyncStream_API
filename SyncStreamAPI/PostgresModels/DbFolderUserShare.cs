namespace SyncStreamAPI.PostgresModels;

public class DbFolderUserShare
{
    public DbFolderUserShare()
    {
    }

    public DbFolderUserShare(int dbUserID, int dbFolderID)
    {
        Id = 0;
        DbUserID = dbUserID;
        DbFolderID = dbFolderID;
    }

    public int Id { get; set; }

    public int DbUserID { get; set; }

    //public DbUser User { get; set; }
    public int DbFolderID { get; set; }
    //public DbFileFolder Folder { get; set; }
}