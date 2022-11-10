using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.PostgresModels
{
    public class DbRememberToken
    {
        public int ID { get; set; }
        public string Token { get; set; }
        public DateTime Created { get; set; }
        public DbRememberToken()
        {
            Created = DateTime.Now;
        }
    }
}
