using System;

namespace SyncStreamAPI.PostgresModels;

public class DbConversation
{
    public DbConversation()
    {
        Id = 0;
        LastUpdated = DateTime.Now;
    }

    public DbConversation(int senderId, int receiverId, string message)
    {
        Id = 0;
        LastUpdated = DateTime.Now;
        SenderId = senderId;
        ReceiverId = receiverId;
        Message = new DbMessage(message, senderId);
    }

    public int Id { get; set; }
    public DateTime LastUpdated { get; set; }
    public int SenderId { get; set; }
    public virtual DbUser Sender { get; set; }
    public int ReceiverId { get; set; }
    public virtual DbUser Receiver { get; set; }
    public DbMessage Message { get; set; }
}