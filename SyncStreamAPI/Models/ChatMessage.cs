using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models
{
    public class ChatMessage
    {
        public string message { get; set; }
        public string username { get; set; }
        public DateTime time { get; set; }
        public string color { get; set; }
        public string usercolor { get; set; }
    }
}
