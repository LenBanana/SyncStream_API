using SyncStreamAPI.Enums.Games;
using SyncStreamAPI.Enums.Games.Cards;
using SyncStreamAPI.Models.GameModels.Blackjack;
using SyncStreamAPI.Models.GameModels.Members;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Games.Blackjack
{
    public class BlackjackAi
    {
        public static BlackjackSmartReaction SmartPull(BlackjackMember member, BlackjackDealer dealer, bool doubleOption, bool forSplitHand)
        {
            var dealerCard = dealer.cards[0].Rank;
            var dealerPoints = dealer.pointsDTO;
            if (member.splitable && !member.didSplit)
                return ReactToSplit(member.cards[0].Rank, (int)dealerPoints, doubleOption);

            if ((!forSplitHand && member.cards.Count == 2) || (forSplitHand && member.splitCards.Count == 2))
            {
                var aceIdx = forSplitHand ? member.splitCards.FindIndex(x => x.Rank == PlayingCardRank.Ace) : member.cards.FindIndex(x => x.Rank == PlayingCardRank.Ace);
                if (aceIdx != -1)
                    return ReactToSingleAce(forSplitHand ? member.splitCards[aceIdx == 1 ? 0 : 1].Rank : member.cards[aceIdx == 1 ? 0 : 1].Rank, (int)dealerPoints, doubleOption);
            }
            return ReactToNormalHand(forSplitHand ? member.splitPoints : member.points, (int)dealerPoints, doubleOption);
        }

        private static BlackjackSmartReaction ReactToSplit(PlayingCardRank rank, int dealerPoints, bool doubleOption)
        {
            switch (rank)
            {
                case PlayingCardRank.Ace:
                case PlayingCardRank.Eight:
                    return BlackjackSmartReaction.Split;
                case PlayingCardRank.King:
                case PlayingCardRank.Queen:
                case PlayingCardRank.Jack:
                case PlayingCardRank.Ten:
                    return BlackjackSmartReaction.Stand;
                case PlayingCardRank.Nine:
                    switch (dealerPoints)
                    {
                        case 11:
                        case 10:
                        case 7:
                            return BlackjackSmartReaction.Stand;
                        default:
                            return BlackjackSmartReaction.Split;
                    }
                case PlayingCardRank.Seven:
                case PlayingCardRank.Two:
                case PlayingCardRank.Three:
                    switch (dealerPoints)
                    {
                        case 11:
                        case 10:
                        case 9:
                        case 8:
                            return BlackjackSmartReaction.Hit;
                        default:
                            return BlackjackSmartReaction.Split;
                    }
                case PlayingCardRank.Six:
                    switch (dealerPoints)
                    {
                        case 11:
                        case 10:
                        case 9:
                        case 8:
                        case 7:
                            return BlackjackSmartReaction.Hit;
                        default:
                            return BlackjackSmartReaction.Split;
                    }
                case PlayingCardRank.Five:
                    switch (dealerPoints)
                    {
                        case 11:
                        case 10:
                            return BlackjackSmartReaction.Hit;
                        default:
                            return doubleOption ? BlackjackSmartReaction.Double : BlackjackSmartReaction.Hit;
                    }
                case PlayingCardRank.Four:
                    switch (dealerPoints)
                    {
                        case 6:
                        case 5:
                            return BlackjackSmartReaction.Split;
                        default:
                            return BlackjackSmartReaction.Hit;
                    }
                default:
                    return BlackjackSmartReaction.Stand;
            }
        }


        private static BlackjackSmartReaction ReactToSingleAce(PlayingCardRank rank, int dealerPoints, bool doubleOption)
        {
            switch (rank)
            {
                case PlayingCardRank.King:
                case PlayingCardRank.Queen:
                case PlayingCardRank.Jack:
                case PlayingCardRank.Ten:
                case PlayingCardRank.Nine:
                case PlayingCardRank.Eight:
                    return BlackjackSmartReaction.Stand;
                case PlayingCardRank.Seven:
                    switch (dealerPoints)
                    {
                        case 8:
                        case 7:
                        case 2:
                            return BlackjackSmartReaction.Stand;
                        case 11:
                        case 10:
                        case 9:
                            return BlackjackSmartReaction.Hit;
                        default:
                            return doubleOption ? BlackjackSmartReaction.Double : BlackjackSmartReaction.Hit;
                    }
                case PlayingCardRank.Six:
                    switch (dealerPoints)
                    {
                        case 6:
                        case 5:
                        case 4:
                        case 3:
                            return doubleOption ? BlackjackSmartReaction.Double : BlackjackSmartReaction.Hit;
                        default:
                            return BlackjackSmartReaction.Hit;
                    }
                case PlayingCardRank.Five:
                case PlayingCardRank.Four:
                    switch (dealerPoints)
                    {
                        case 6:
                        case 5:
                        case 4:
                            return doubleOption ? BlackjackSmartReaction.Double : BlackjackSmartReaction.Hit;
                        default:
                            return BlackjackSmartReaction.Hit;
                    }
                case PlayingCardRank.Three:
                case PlayingCardRank.Two:
                    switch (dealerPoints)
                    {
                        case 6:
                        case 5:
                            return doubleOption ? BlackjackSmartReaction.Double : BlackjackSmartReaction.Hit;
                        default:
                            return BlackjackSmartReaction.Hit;
                    }
                default:
                    return BlackjackSmartReaction.Stand;
            }
        }

        private static BlackjackSmartReaction ReactToNormalHand(int memberPoints, int dealerPoints, bool doubleOption)
        {

            switch (memberPoints)
            {
                case 16:
                case 15:
                case 14:
                case 13:
                    switch (dealerPoints)
                    {
                        case 6:
                        case 5:
                        case 4:
                        case 3:
                        case 2:
                            return BlackjackSmartReaction.Stand;
                        default:
                            return BlackjackSmartReaction.Hit;
                    }
                case 12:
                    switch (dealerPoints)
                    {
                        case 6:
                        case 5:
                        case 4:
                            return BlackjackSmartReaction.Stand;
                        default:
                            return BlackjackSmartReaction.Hit;
                    }
                case 11:
                    switch (dealerPoints)
                    {
                        case 11:
                            return BlackjackSmartReaction.Hit;
                        default:
                            return doubleOption ? BlackjackSmartReaction.Double : BlackjackSmartReaction.Hit;
                    }
                case 10:
                    switch (dealerPoints)
                    {
                        case 11:
                        case 10:
                            return BlackjackSmartReaction.Hit;
                        default:
                            return doubleOption ? BlackjackSmartReaction.Double : BlackjackSmartReaction.Hit;
                    }
                case 9:
                    switch (dealerPoints)
                    {
                        case 6:
                        case 5:
                        case 4:
                        case 3:
                            return doubleOption ? BlackjackSmartReaction.Double : BlackjackSmartReaction.Hit;
                        default:
                            return BlackjackSmartReaction.Hit;
                    }
                case 8:
                case 7:
                case 6:
                case 5:
                case 4:
                    return BlackjackSmartReaction.Hit;
                default:
                    return BlackjackSmartReaction.Stand;
            }
        }
    }
}
