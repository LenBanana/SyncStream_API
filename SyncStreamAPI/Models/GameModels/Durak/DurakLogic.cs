using SyncStreamAPI.Enums.Games.Cards;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models.GameModels.Members;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models.GameModels.Durak
{
    public class DurakLogic
    {
        public List<PlayingCard> cardDeck { get; set; } = new List<PlayingCard>();
        public List<DurakMember> members { get; set; } = new List<DurakMember>();

        public DurakLogic()
        {
            BuildDeck();
        }

        public void BuildDeck()
        {
            foreach (PlayingCardSuit suit in Enum.GetValues(typeof(PlayingCardSuit)))
            {
                foreach (PlayingCardRank rank in Enum.GetValues(typeof(PlayingCardRank)))
                {
                    if ((int)rank > 5) 
                    {
                        PlayingCard nextCard = new PlayingCard(suit, rank);
                        cardDeck.Add(nextCard);
                    }
                }
            }
            cardDeck.Shuffle();
            GiveCardsToMembers();
        }

        public void GiveCardsToMembers()
        {
            foreach (var member in members)
            {
                for (int i = member.cards.Count; i < 6; i++)
                {
                    member.cards.Add(cardDeck[i]);
                    cardDeck.RemoveAt(i);
                }
            }
        }
    }
}
