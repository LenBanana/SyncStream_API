﻿using System;

namespace SyncStreamAPI.PostgresModels;

public class DbRememberToken
{
    public DbRememberToken()
    {
        Created = DateTime.UtcNow;
    }

    public int ID { get; set; }
    public string Token { get; set; }
    public DateTime Created { get; set; }
}