using System.Collections.Generic;
using System.Threading.Tasks;
using SyncStreamAPI.Wire.Games;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task playblackjack(bool playing);
    Task askforbet();
    Task askforpull(bool doubleOption);
    Task askforsplitpull(bool forSplitHand);
    Task sendblackjackself(BlackjackMemberDto you);
    Task sendblackjackmembers(List<BlackjackMemberDto> others);
    Task sendblackjackdealer(BlackjackDealerDto dealer);
}