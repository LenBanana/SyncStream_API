using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.MariaModels
{
    public class RememberToken
    {
        public int ID { get; set; }
        public int userID { get; set; }
        public string Token { get; set; }
    }
}
