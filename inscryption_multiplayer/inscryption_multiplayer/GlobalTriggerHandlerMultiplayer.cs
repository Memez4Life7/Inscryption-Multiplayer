﻿using DiskCardGame;
using inscryption_multiplayer;
using inscryption_multiplayer.Networking;
using inscryption_multiplayer.Patches;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static inscryption_multiplayer.Utils;

namespace inscryption_multiplayer
{
    public class GlobalTriggerHandlerMultiplayer : NonCardTriggerReceiver
    {
        private static readonly CardInfoMultiplayer _TestCardInfo = new()
        {
            name = "Boulder",
            mods = new List<CardModificationInfo>
            {
                new CardModificationInfo(Ability.Brittle),
                new CardModificationInfo(1, 0)
            },
            slot = new CardSlotMultiplayer
            {
                isPlayerSlot = false
            }
        };

        public static CardInfoMultiplayer TestCardInfo
        {
            get
            {
                _TestCardInfo.slot.index = Random.Range(0, 4);
                return _TestCardInfo;
            }
        }

        public static CardInfoMultiplayer TestCardInfoWithSpecificSlot(int index)
        {
            _TestCardInfo.slot.index = index;
            return _TestCardInfo;
        }

        public override bool RespondsToOtherCardResolve(PlayableCard otherCard)
        {
            return true;
        }

        public override IEnumerator OnOtherCardResolve(PlayableCard otherCard)
        {
            if (otherCard.HasAbility(Ability.CorpseEater) && !otherCard.OpponentCard)
            {
                InscryptionNetworking.Connection.SendJson(Player_Backline.IsPlayerQueueSlot(otherCard.Slot) ?
                                                                      NetworkingMessage.CardQueuedByOpponent :
                                                                      NetworkingMessage.CardPlacedByOpponent,
                                                                      CardToMPInfo(otherCard));
            }
            yield break;
        }

        public override bool RespondsToOtherCardDie(PlayableCard card, CardSlot deathSlot, bool fromCombat, PlayableCard killer)
        {
            return !fromCombat;
        }

        public override IEnumerator OnOtherCardDie(PlayableCard card, CardSlot deathSlot, bool fromCombat, PlayableCard killer)
        {
            InscryptionNetworking.Connection.SendJson(NetworkingMessage.CardSacrificedByOpponent, SlotToMPInfo(deathSlot));
            yield break;
        }

        public override bool RespondsToTurnEnd(bool playerTurnEnd)
        {
            return true;
        }

        public override IEnumerator OnTurnEnd(bool playerTurnEnd)
        {
            if (GameSettings.Current.AllowBackrows)
            {
                if (playerTurnEnd)
                {
                    PlayCardsInPlayerQueue();
                }
                else
                {
                    InscryptionNetworking.Connection.Send(NetworkingMessage.CardsInOpponentQueueMoved);
                }
            }
            yield break;
        }
    }
}
