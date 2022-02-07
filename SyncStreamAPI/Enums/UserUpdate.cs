﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Enums
{
    public enum UserUpdate
    {
        RoomNotExist = -3,
        Banned = -2,
        WrongPassword = -1,
        Success = 1
    }
}
