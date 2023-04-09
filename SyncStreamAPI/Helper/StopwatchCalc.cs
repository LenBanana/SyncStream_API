using System;
using System.Diagnostics;

namespace SyncStreamAPI.Helper
{
    public class StopwatchCalc
    {
        public static string CalculateRemainingTime(Stopwatch watch, double perc)
        {
            if (watch == null)
            {
                return "-1";
            }

            var millis = watch.ElapsedMilliseconds;
            var timeLeft = millis / perc * (100 - perc);
            timeLeft = timeLeft < 0 || timeLeft > TimeSpan.MaxValue.TotalMilliseconds ? 0 : timeLeft;
            var timeString = TimeSpan.FromMilliseconds(timeLeft).ToString(@"hh\:mm\:ss");
            return timeString;
        }
    }
}
