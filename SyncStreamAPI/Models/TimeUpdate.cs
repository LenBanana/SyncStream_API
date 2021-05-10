using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models
{
    public class TimeUpdate
    {
        public double NewTime { get; set; }
        public bool HostSkip { get; set; }
    }
}
