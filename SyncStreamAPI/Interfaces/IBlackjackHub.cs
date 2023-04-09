using SyncStreamAPI.Models.GameModels.Blackjack;
using SyncStreamAPI.Models.GameModels.Members;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        Task playblackjack(bool playing);
        Task askforbet();
        Task askforpull(bool doubleOption);
        Task askforsplitpull(bool forSplitHand);
        Task sendblackjackself(BlackjackMember you);
        Task sendblackjackmembers(List<BlackjackMember> others);
        Task sendblackjackdealer(BlackjackDealer dealer);
    }
}
