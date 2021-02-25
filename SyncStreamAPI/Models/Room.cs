using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models
{
    public class Room
    {
        public string uniqueId { get; set; }
        public string name { get; set; }
        public string password { get; set; }
        public Server server { get { return _server; } set { _server = value; } } //server.CheckMembers();
        private Server _server { get; set; }


        public Room()
        {
        }

    }
}
