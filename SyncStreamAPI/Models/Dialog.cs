using SyncStreamAPI.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models
{
    public class Dialog
    {
        public string Id { get; set; }
        public string Header { get; set; }
        public string Question { get; set; }
        public string Answer1 { get; set; }
        public string Answer2 { get; set; }
        public AlertTypes AlertType { get; set; }
        public Dialog(AlertTypes type = AlertTypes.Info)
        {
            AlertType = type;
        }
    }
}
