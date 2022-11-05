using System.Collections.Generic;

namespace SyncStreamAPI.Models
{
    public class BrowserM3U8Response
    {
        public string InputUrl { get; set; }
        public List<string> OutputUrls { get; set; } = new List<string>();
    }
}
