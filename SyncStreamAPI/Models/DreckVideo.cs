using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models
{
    public class DreckVideo
    {
        public DreckVideo()
        {
        }

        public DreckVideo(string title, string url, bool ended, TimeSpan length, string addedBy)
        {
            this.title = title;
            this.url = url;
            this.ended = ended;
            Length = length;
            AddedBy = addedBy;
        }

        public string title { get; set; }
        public string url { get; set; }
        public bool ended { get; set; }
        public TimeSpan Length { get; set; }
        public string AddedBy { get; set; }
    }
}
