using System;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper
{
    public static class BlackjackTimer
    {
        static readonly Random rnd = new Random();
        public static async Task RndDelay(TimeSpan min, TimeSpan max)
        {
            var rndTime = rnd.NextDouble() * (max.TotalMilliseconds - min.TotalMilliseconds) + min.TotalMilliseconds;
            await Task.Delay(TimeSpan.FromMilliseconds(rndTime));
        }
    }
}
