using SyncStreamAPI.Enums;

namespace SyncStreamAPI.Models
{
    public class Dialog
    {
        public Dialog(AlertType alertType, string question)
        {
            Question = question;
            AlertType = alertType;
            Answer1 = "Ok";
        }

        public Dialog(AlertType type = AlertType.Info)
        {
            AlertType = type;
        }

        public string Id { get; set; }
        public string Header { get; set; }
        public string Question { get; set; }
        public string Answer1 { get; set; }
        public string Answer2 { get; set; }
        public AlertType AlertType { get; set; }
    }
}