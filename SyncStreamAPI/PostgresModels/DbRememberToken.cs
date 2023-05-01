using System;

namespace SyncStreamAPI.PostgresModels
{
    public class DbRememberToken
    {
        public int ID { get; set; }
        public string Token { get; set; }
        public DateTime Created { get; set; }
        public DbRememberToken()
        {
            Created = DateTime.UtcNow;
        }
    }
}
