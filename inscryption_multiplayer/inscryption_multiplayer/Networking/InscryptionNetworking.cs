using DiskCardGame;
using Infiniscryption.PackManagement;
using InscryptionAPI.Saves;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using static inscryption_multiplayer.Ascension_Patches;
using static inscryption_multiplayer.Utils;

namespace inscryption_multiplayer.Networking
{
    public static class NetworkingMessage
    {
        public const string StartGame = "start_game";
        public const string OpponentReady = "OpponentReady";
        public const string InitiateCombat = "InitiateCombat";
        public const string OpponentCardPlacePhaseEnded = "OpponentCardPlacePhaseEnded";
        public const string CardPlacedByOpponent = "CardPlacedByOpponent";
        public const string CardQueuedByOpponent = "CardPlacedByOpponentInQueue";
        public const string CardSacrificedByOpponent = "CardSacrificedByOpponent";
        public const string CardsInOpponentQueueMoved = "CardsInOpponentQueueMoved";
        public const string SigilData = "SigilData";
        public const string ChangeOpponentTotem = "ChangeOpponentTotem";
        public const string ItemUsed = "ItemUsed";
        public const string ChangeSettings = "ChangeSettings";
        public const string ChallengesChosen = "ChallengesChosen";
        public const string PacksChosen = "PacksChosen";
    }

    public class NetworkingError
    {
        public static NetworkingError OtherPlayerLeft = new("Other player left", false);
        public static NetworkingError ConnectionLost = new("Connection lost", true);
        public static NetworkingError GaveUp = new("Gave up", true);

        public readonly string Message;
        public readonly bool OwnFault;

        public NetworkingError(string message, bool ownFault)
        {
            Message = message;
            OwnFault = ownFault;
        }
    }

    internal abstract class InscryptionNetworking : IDisposable
    {
        public const bool START_ALONE = false;
        private static readonly byte[] SpaceByte;

        internal static InscryptionNetworking Connection =
#if NETWORKING_STEAM
            new SteamNetworking();
#else
            new SocketNetworking();
#endif

        internal bool AutoStart;
        internal string OtherPlayerName;
        internal bool TransferredHost;

        internal bool PlayAgainstBot;

        internal abstract bool Connected { get; }
        internal abstract bool IsHost { get; }
        internal abstract void Host();
        internal abstract void Join();
        internal virtual void Invite() { }
        internal abstract void Leave();
        internal abstract void Send(byte[] message);
        internal virtual void UpdateSettings() { }
        internal abstract void Update();
        public virtual void Dispose() { }

        internal void SendJson(string message, object serializedClass, bool bypassCheck = false)
        {
            if (bypassCheck)
                message = "bypasscheck " + message;

            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            byte[] jsonUtf8Bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(serializedClass));

            //combines the three byte arrays
            byte[] fullMessageBytes = new byte[messageBytes.Length + SpaceByte.Length + jsonUtf8Bytes.Length];
            Buffer.BlockCopy(messageBytes, 0, fullMessageBytes, 0, messageBytes.Length);
            Buffer.BlockCopy(SpaceByte, 0, fullMessageBytes, messageBytes.Length, SpaceByte.Length);
            Buffer.BlockCopy(jsonUtf8Bytes, 0, fullMessageBytes, messageBytes.Length + SpaceByte.Length, jsonUtf8Bytes.Length);

            Send(fullMessageBytes);
        }

        internal void Send(string message, bool bypassCheck = false)
        {
            if (bypassCheck)
                message = "bypasscheck " + message;

            Send(Encoding.UTF8.GetBytes(message));
        }

        internal void Receive(bool selfMessage, string message)
        {
            if (message.StartsWith("bypasscheck "))
            {
                message = message.Substring(12);
                if (PlayAgainstBot
#if NETWORKING_STEAM
                    || SteamNetworking.START_ALONE
#endif
                    )
                    selfMessage = false;
            }
            string jsonString = null;
            List<string> wordList = message.Split(' ').ToList();
            if (wordList.Count > 1)
            {
                if (wordList[1][0] == '{')
                {
                    jsonString = string.Join(" ", message.Split(' ').Skip(1));
                    message = message.Split(' ')[0];
                }
            }

            Debug.Log($"Received message: {message}");
            if (jsonString != null)
            {
                Plugin.Log.LogInfo($"Json string: {jsonString}");
            }

            if (DebugUI.debugEnabled && DebugUI.receiveAllMessages)
            {
                selfMessage = false;
            }

            //messages here should be received by all users in the lobby
            switch (message)
            {
                case NetworkingMessage.StartGame:
                    MultiplayerRunState.Run = new();
                    MultiplayerRunState.Run.InitCommunicators();
                    Game_Patches.OpponentReady = false;
                    //starts a new run
                    //Singleton<AscensionMenuScreens>.Instance.TransitionToGame(true);

                    Ascension_Patches.DeckSelection = true;
                    Time.timeScale = 1;
                    SceneLoader.Load("Ascension_Configure");

                    break;
            }

            //messages here should be received by all users in the lobby except by the one who sent it
            if (!selfMessage)
            {
                switch (message)
                {
                    case NetworkingMessage.OpponentReady:
                        Game_Patches.OpponentReady = true;
                        break;

                    case NetworkingMessage.InitiateCombat:
                        Singleton<TurnManager>.Instance.playerInitiatedCombat = true;
                        break;

                    case NetworkingMessage.OpponentCardPlacePhaseEnded:
                        Multiplayer_Battle_Sequencer.Current.OpponentEvents.Enqueue(ToggleCardPlacePhase());
                        break;

                    case NetworkingMessage.CardPlacedByOpponent:
                        CardInfoMultiplayer cardInfo = JsonConvert.DeserializeObject<CardInfoMultiplayer>(jsonString);
                        CardSlot placedSlot = MPInfoToSlot(cardInfo.slot);
                        if (placedSlot.Card != null)
                        {
                            placedSlot.Card.ExitBoard(0, new Vector3(0, 0, 0));
                        }

                        Singleton<BoardManager>.Instance.StartCoroutine(
                            Singleton<BoardManager>.Instance.ResolveCardOnBoard(
                                MPInfoToCard(cardInfo),
                                placedSlot,
                                0.1f,
                                null,
                                true
                            ));
                        break;

                    case NetworkingMessage.CardQueuedByOpponent:
                        CardInfoMultiplayer cardInfoQueue = JsonConvert.DeserializeObject<CardInfoMultiplayer>(jsonString);
                        CardSlot slotToQueueFor = MPInfoToSlot(cardInfoQueue.slot);

                        if (slotToQueueFor == null)
                        {
                            break;
                        }

                        PlayableCard cardInQueue = Singleton<TurnManager>.Instance.Opponent.Queue.FirstOrDefault(x => x.Slot.Index == cardInfoQueue.slot.index);

                        if (cardInQueue != null)
                        {
                            cardInQueue.ExitBoard(0, new Vector3(0, 0, 0));
                        }

                        Singleton<ViewManager>.Instance.SwitchToView(Singleton<BoardManager>.Instance.QueueView, false, false);

                        PlayableCard playableCardQueue = MPInfoToCard(cardInfoQueue);
                        Singleton<BoardManager>.Instance.QueueCardForSlot(playableCardQueue, slotToQueueFor, 0.25f);
                        Singleton<Opponent>.Instance.Queue.Add(playableCardQueue);
                        break;

                    case NetworkingMessage.CardSacrificedByOpponent:
                        CardSlotMultiplayer cardSlot = JsonConvert.DeserializeObject<CardSlotMultiplayer>(jsonString);
                        PlayableCard sacrificedCard;
                        if (cardSlot.isQueueSlot)
                        {
                            sacrificedCard = Singleton<TurnManager>.Instance.Opponent.Queue.FirstOrDefault(x => x.Slot.Index == cardSlot.index);
                        }
                        else
                        {
                            CardSlot sacrificedSlot = MPInfoToSlot(cardSlot);
                            if (sacrificedSlot.Card == null)
                            {
                                break;
                            }
                            sacrificedCard = sacrificedSlot.Card;
                        }
                        if (sacrificedCard != null)
                        {
                            sacrificedCard.Anim.PlaySacrificeSound();
                            if (sacrificedCard.HasAbility(Ability.Sacrificial))
                            {
                                sacrificedCard.Anim.SetSacrificeHoverMarkerShown(false);
                                sacrificedCard.Anim.SetMarkedForSacrifice(false);
                                sacrificedCard.Anim.PlaySacrificeParticles();
                                ProgressionData.SetAbilityLearned(Ability.Sacrificial);
                                if (sacrificedCard.TriggerHandler.RespondsToTrigger(Trigger.Sacrifice, Array.Empty<object>()))
                                {
                                    Singleton<BoardManager>.Instance.StartCoroutine(sacrificedCard.TriggerHandler.OnTrigger(Trigger.Sacrifice, Array.Empty<object>()));
                                }
                            }
                            else
                            {
                                sacrificedCard.Anim.DeactivateSacrificeHoverMarker();
                                if (sacrificedCard.TriggerHandler.RespondsToTrigger(Trigger.Sacrifice, Array.Empty<object>()))
                                {
                                    Singleton<BoardManager>.Instance.StartCoroutine(sacrificedCard.TriggerHandler.OnTrigger(Trigger.Sacrifice, Array.Empty<object>()));
                                }
                                sacrificedCard.ExitBoard(0, new Vector3(0, 0, 0));
                            }
                        }
                        break;

                    case NetworkingMessage.SigilData:
                        var sigilData = JsonConvert.DeserializeObject<SigilEvent>(jsonString);
                        if (!MultiplayerRunState.Run.SigilCommunicators.TryGetValue(sigilData.ID, out var communicator))
                        {
                            communicator = new SigilCommunicatorDummy(sigilData.ID);
                            MultiplayerRunState.Run.SigilCommunicators.Add(sigilData.ID, communicator);
                        }
                        communicator.Process(sigilData.Data);
                        break;

                    case NetworkingMessage.ChangeOpponentTotem:
                        MultiplayerRunState.Run.OpponentTotem =
                            JsonConvert.DeserializeObject<TotemDefinition>(jsonString);
                        break;

                    case NetworkingMessage.ItemUsed:
                        Multiplayer_Battle_Sequencer.Current.OpponentEvents.Enqueue(
                            Item_Sync.HandleOpponentItem(JsonConvert.DeserializeObject<MultiplayerItemData>(jsonString)));
                        break;

                    case NetworkingMessage.ChangeSettings:
                        GameSettings.Current = JsonConvert.DeserializeObject<GameSettings>(jsonString);
                        break;

                    case NetworkingMessage.CardsInOpponentQueueMoved:
                        Singleton<BoardManager>.Instance.StartCoroutine(PlayCardsInOpponentQueue());
                        break;

                    case NetworkingMessage.ChallengesChosen:
                        if (jsonString != null)
                        {
                            ChosenChallenges chosenChallenges = JsonConvert.DeserializeObject<ChosenChallenges>(jsonString);
                            AscensionSaveData.Data.activeChallenges = chosenChallenges.challenges;
                        }

                        Ascension_Patches.HostSelection = false;
                        break;

                    case NetworkingMessage.PacksChosen:
                        ChosenPacks chosenPacks = JsonConvert.DeserializeObject<ChosenPacks>(jsonString);
                        PacksChosen(chosenPacks);
                        break;
                }

                foreach (KeyValuePair<string, Action<string, string>> messageData in API.messageList)
                {
                    if (message == messageData.Key)
                    {
                        messageData.Value.Invoke(message, jsonString);
                    }
                }
            }
        }

        public static void PacksChosen(ChosenPacks chosenPacks)
        {
            ModdedSaveManager.SaveData.SetValue("zorro.inscryption.infiniscryption.packmanager", chosenPacks.activePackKey, chosenPacks.activePackString);
            ModdedSaveManager.SaveData.SetValue("zorro.inscryption.infiniscryption.packmanager", chosenPacks.inactivePackKey, chosenPacks.inactivePackString);

            MethodInfo FilterCardAndAbilityListMethod = typeof(PackManager).GetMethod("FilterCardAndAbilityList", BindingFlags.NonPublic | BindingFlags.Static);
            FilterCardAndAbilityListMethod.Invoke(null, null);
        }

        private IEnumerator ToggleCardPlacePhase()
        {
            Multiplayer_Battle_Sequencer.Current.OpponentCardPlacePhase = false;
            yield return null;
        }

        internal void Receive(bool selfMessage, byte[] message)
        {
            Receive(selfMessage, Encoding.UTF8.GetString(message).TrimEnd('\0'));
        }

        internal virtual void StartGame(bool resetBot = true)
        {
            if (resetBot)
                PlayAgainstBot = false;
            Send(NetworkingMessage.StartGame);
        }

        internal virtual void StartGameWithBot()
        {
            PlayAgainstBot = true;
            StartGame(false);
        }

        internal void SendSettings()
        {
            SendJson(NetworkingMessage.ChangeSettings, GameSettings.Current);
        }

        private static IEnumerator CallbackRoutine(IEnumerator coroutine, Action callback)
        {
            yield return coroutine;
            callback();
        }

        ~InscryptionNetworking()
        {
            Dispose();
        }

        static InscryptionNetworking()
        {
            SpaceByte = Encoding.UTF8.GetBytes(" ");
        }
    }
}
