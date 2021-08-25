using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        public async Task SetBet(string UniqueId, int UserId, double bet)
        {
            var room = GetRoom(UniqueId);
            var game = room.BlackjackGame;
            game.members[UserId].SetBet(bet);
            _blackjackManager.AskForBet(game, UserId++);
        }

        public async Task TakePull(string UniqueId, int UserId, bool pull, bool doubleOption)
        {
            var room = GetRoom(UniqueId);
            var game = room.BlackjackGame;
            var member = game.members[UserId];
            if (pull)
            {
                game.DealCard(member);
                _blackjackManager.AskForPull(game, UserId);
            }
            else if (doubleOption)
            {
                member.doubleBet();
                game.DealCard(member);
                _blackjackManager.AskForPull(game, UserId++);
            }
            else
                _blackjackManager.AskForPull(game, UserId++);
            
        }
    }
}
