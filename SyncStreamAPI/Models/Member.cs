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
        public string uptime { get; set; }
        public bool ishost { get; set; }
        public bool kick { get; set; }
        private int ConsecutiveAFK { get; set; }
        public List<Drawing> drawings { get; set; } = new List<Drawing>();

        public delegate void KickEvent(bool kick, Member e);
        public event KickEvent Kicked;

        public Member()
        {
            CountDown();
        }

        string _uptime = "";
        private async void CountDown()
        {
            await Task.Delay(1000);
            if (_uptime == uptime)
            {
                ConsecutiveAFK += 1;
                if (ConsecutiveAFK >= 10)
                {
                    kick = true;
                    Kicked?.Invoke(kick, this);
                    return;
                }
            }
            else
            {
                _uptime = uptime;
                ConsecutiveAFK = 0;
            }
            CountDown();
        }
    }
}

