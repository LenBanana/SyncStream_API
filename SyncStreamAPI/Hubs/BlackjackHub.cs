using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        public async Task PlayBlackjack(string UniqueId)
        {
            var playing = await _blackjackManager.PlayNewRound(UniqueId);
        }

        public async Task SetBet(string UniqueId, double bet)
        {
            var room = GetRoom(UniqueId);
            var game = room.BlackjackGame;
            var idx = game.members.FindIndex(x => x.ConnectionId == Context.ConnectionId);
            game.members[idx].SetBet(bet);
            _blackjackManager.AskForBet(game, idx + 1);
        }

        public async Task TakePull(string UniqueId, bool pull, bool doubleOption)
        {
            var room = GetRoom(UniqueId);
            var game = room.BlackjackGame;
            var idx = game.members.FindIndex(x => x.ConnectionId == Context.ConnectionId);
            var member = game.members[idx];
            if (pull)
            {
                 game.DealCard(member);
                await Task.Delay(500);
                _blackjackManager.AskForPull(game, idx);
                return;
            }
            else if (doubleOption)
            {
                member.doubleBet();
                game.DealCard(member);
            }
            await Task.Delay(500);
            _blackjackManager.AskForPull(game, idx + 1);

        }
    }
}
