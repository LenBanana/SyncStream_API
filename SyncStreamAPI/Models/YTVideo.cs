using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models
{
    public class YTVideo
    {
        public string title { get; set; }
        public string url { get; set; }
        public bool ended { get; set; }
    }
}
