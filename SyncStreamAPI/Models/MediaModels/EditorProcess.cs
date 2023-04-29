using SyncStreamAPI.Enums;
using System;

namespace SyncStreamAPI.Models.MediaModels
{
    public class EditorProcess
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public double Progress { get; set; } = 100
        public AlertType AlertType { get; set; }
        public EditorProcess(AlertType type = AlertType.Info)
        {
            Id = Guid.NewGuid().ToString();
            AlertType = type;
        }
    }
}
