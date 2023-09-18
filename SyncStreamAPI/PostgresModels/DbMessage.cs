using System;

namespace SyncStreamAPI.PostgresModels;

public class DbMessage
{
    public DbMessage()
    {
        Id = 0;
        Time = DateTime.Now;
    }
    public DbMessage(string message, int senderId)
    {
        Id = 0;
        Message = message;
        SenderId = senderId;
        Time = DateTime.Now;
    }

    public int Id { get; set; }
    public string Message { get; set; }
    public DateTime Time { get; set; }
    public int SenderId { get; set; }
    public virtual DbUser Sender { get; set; }
}