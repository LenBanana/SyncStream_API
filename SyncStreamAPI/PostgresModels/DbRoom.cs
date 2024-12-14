using SyncStreamAPI.Models;

namespace SyncStreamAPI.PostgresModels;

public class DbRoom : Room
{
    public DbRoom(string Name, string UnqiueId, bool Deletable, bool Privileged) : base(Name, UnqiueId, Deletable,
        Privileged)
    {
    }

    public DbRoom(Room room) : base(room.name, room.uniqueId, room.deletable, room.isPrivileged)
    {
        password = room.password;
    }

    public DbRoom() : base(null, null, true, false)
    {
    }

    public int ID { get; set; }
}