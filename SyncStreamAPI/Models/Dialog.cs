﻿using SyncStreamAPI.Enums;

namespace SyncStreamAPI.Models
{
    public class Dialog
    {
        public string Id { get; set; }
        public string Header { get; set; }
        public string Question { get; set; }
        public string Answer1 { get; set; }
        public string Answer2 { get; set; }
        public AlertType AlertType { get; set; }
        public Dialog(AlertType type = AlertType.Info)
        {
            AlertType = type;
        }
    }
}
