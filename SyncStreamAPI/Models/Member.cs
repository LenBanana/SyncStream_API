using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models
{
    public class Member
    {
        [Key]
        public string username { get; set; }
        public string RoomId { get; set; }
        public string ip { get; set; }
        private string _uptime { get; set; } = DateTime.Now.ToString("MM.dd.yyyy HH:mm:ss");
        public string uptime { get { return _uptime; } set { _uptime = value; ConsecutiveAFK = 0; } }
        public bool ishost { get; set; }
        private int _ConsecutiveAFK { get; set; } = 0;
        private int ConsecutiveAFK { get { return _ConsecutiveAFK; } set { _ConsecutiveAFK = value; if (value >= 10) { Kicked?.Invoke(this); } } }
        public List<Drawing> drawings { get; set; } = new List<Drawing>();

        public delegate void KickEvent(Member e);
        public event KickEvent Kicked;

        public Member()
        {
            CountDown();
        }

        private async void CountDown()
        {
            await Task.Delay(1000);
            ConsecutiveAFK += 1;
            CountDown();
        }
    }
}

