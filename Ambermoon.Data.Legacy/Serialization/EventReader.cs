﻿using System;
using System.Collections.Generic;

namespace Ambermoon.Data.Legacy.Serialization
{
    internal class EventReader
    {
        public static void ReadEvents(IDataReader dataReader,
            List<Event> events, List<Event> eventList)
        {
            uint numEvents = dataReader.ReadWord();

            // There are numEvents 16 bit values.
            // Each gives the offset of the event to use.
            // Each event data is 12 bytes in size.

            // After this the total number of events is given.
            // Events can be chained (linked list). Each chain
            // is identified by an event id on some map tiles
            // or inside NPCs etc.

            // The last two bytes of each event data contain the
            // offset of the next event data or 0xFFFF if this is
            // the last event of the chain/list.
            // Note that the linked list can have a non-linear order.

            // E.g. in map 8 the first map event (index 0) references
            // map event 2 and this references map event 1 which is the
            // end chunk of the first map event chain.
            uint[] eventOffsets = new uint[numEvents];

            for (uint i = 0; i < numEvents; ++i)
                eventOffsets[i] = dataReader.ReadWord();

            events.Clear();

            if (numEvents > 0)
            {
                uint numTotalEvents = dataReader.ReadWord();
                var eventInfos = new List<Tuple<Event, int>>();

                // read all events and the next event index
                for (uint i = 0; i < numTotalEvents; ++i)
                {
                    var @event = ParseEvent(dataReader);
                    @event.Index = i + 1;
                    eventInfos.Add(Tuple.Create(@event, (int)dataReader.ReadWord()));
                    events.Add(@event);
                }

                foreach (var @event in eventInfos)
                {
                    @event.Item1.Next = @event.Item2 == 0xffff ? null : eventInfos[@event.Item2].Item1;
                }

                foreach (var eventOffset in eventOffsets)
                    eventList.Add(eventInfos[(int)eventOffset].Item1);
            }
        }

        static Event ParseEvent(IDataReader dataReader)
        {
            Event mapEvent;
            var type = (EventType)dataReader.ReadByte();

            switch (type)
            {
                case EventType.MapChange:
                {
                    // 1. byte is the x coordinate
                    // 2. byte is the y coordinate
                    // Then 3 unknown bytes
                    // Then a word for the map index
                    // Then 2 unknown bytes (seem to be 00 FF)
                    uint x = dataReader.ReadByte();
                    uint y = dataReader.ReadByte();
                    var direction = (CharacterDirection)dataReader.ReadByte();
                    var unknown1 = dataReader.ReadBytes(2);
                    uint mapIndex = dataReader.ReadWord();
                    var unknown2 = dataReader.ReadBytes(2);
                    mapEvent = new MapChangeEvent
                    {
                        MapIndex = mapIndex,
                        X = x,
                        Y = y,
                        Direction = direction,
                        Unknown1 = unknown1,
                        Unknown2 = unknown2
                    };
                    break;
                }
                case EventType.Door:
                {
                    // 1. byte is unknown (maybe the lock flags like for chests?)
                    // 2. byte is unknown
                    // 3. byte is unknown
                    // 4. byte is unknown
                    // 5. byte is unknown
                    // word at position 6 is the key index if a key must unlock it
                    // last word is the event index (0-based) of the event that is called when unlocking fails
                    var unknown = dataReader.ReadBytes(5); // Unknown
                    uint keyIndex = dataReader.ReadWord();
                    var unlockFailEventIndex = dataReader.ReadWord();
                    mapEvent = new DoorEvent
                    {
                        Unknown = unknown,
                        KeyIndex = keyIndex,
                        UnlockFailedEventIndex = unlockFailEventIndex
                    };
                    break;
                }
                case EventType.Chest:
                {
                    // 1. byte are the lock flags
                    // 2. byte is unknown (always 0 except for one chest with 20 blue discs which has 0x32 and lock flags of 0x00)
                    // 3. byte is unknown (0xff for unlocked chests)
                    // 4. byte is the chest index (0-based)
                    // 5. byte (0 = chest, 1 = pile/removable loot or item) or "remove if empty"
                    // word at position 6 is the key index if a key must unlock it
                    // last word is the event index (0-based) of the event that is called when unlocking fails
                    var lockType = (ChestMapEvent.LockFlags)dataReader.ReadByte();
                    var unknown = dataReader.ReadWord(); // Unknown
                    uint chestIndex = dataReader.ReadByte();
                    bool removeWhenEmpty = dataReader.ReadByte() != 0;
                    uint keyIndex = dataReader.ReadWord();
                    var unlockFailEventIndex = dataReader.ReadWord();
                    mapEvent = new ChestMapEvent
                    {
                        Unknown = unknown,
                        Lock = lockType,
                        ChestIndex = chestIndex,
                        RemoveWhenEmpty = removeWhenEmpty,
                        KeyIndex = keyIndex,
                        UnlockFailedEventIndex = unlockFailEventIndex
                    };
                    break;
                }
                case EventType.PopupText:
                {
                    // event image index (0xff = no image)
                    // trigger (1 = move, 2 = cursor, 3 = both)
                    // 2 unknown bytes
                    // 4-5. byte is the map text index
                    // 4 unknown bytes
                    var eventImageIndex = dataReader.ReadByte();
                    var popupTrigger = (PopupTextEvent.Trigger)dataReader.ReadByte();
                    var unknown1 = dataReader.ReadByte();
                    var textIndex = dataReader.ReadWord();
                    var unknown2 = dataReader.ReadBytes(4);
                    mapEvent = new PopupTextEvent
                    {
                        EventImageIndex = eventImageIndex,
                        PopupTrigger = popupTrigger,
                        TextIndex = textIndex,
                        Unknown1 = unknown1,
                        Unknown2 = unknown2
                    };
                    break;
                }
                case EventType.Spinner:
                {
                    var direction = (CharacterDirection)dataReader.ReadByte();
                    var unknown = dataReader.ReadBytes(8);
                    mapEvent = new SpinnerEvent
                    {
                        Direction = direction,
                        Unknown = unknown,
                    };
                    break;
                }
                case EventType.Trap:
                {
                    var trapType = (TrapEvent.TrapType)dataReader.ReadByte();
                    var target = (TrapEvent.TrapTarget)dataReader.ReadByte();
                    var value = dataReader.ReadByte();
                    var unknown = dataReader.ReadByte();
                    dataReader.ReadBytes(5); // unused
                    mapEvent = new TrapEvent
                    {
                        TypeOfTrap = trapType,
                        Target = target,
                        Value = value,
                        Unknown = unknown,
                    };
                    break;
                }
                case EventType.Riddlemouth:
                {
                    var introTextIndex = dataReader.ReadByte();
                    var solutionTextIndex = dataReader.ReadByte();
                    var unknown = dataReader.ReadBytes(5);
                    var correctAnswerTextIndex = dataReader.ReadWord();
                    mapEvent = new RiddlemouthEvent
                    {
                        RiddleTextIndex = introTextIndex,
                        SolutionTextIndex = solutionTextIndex,
                        CorrectAnswerDictionaryIndex = correctAnswerTextIndex,
                        Unknown = unknown
                    };
                    break;
                }
                case EventType.Award:
                {
                    var awardType = (AwardEvent.AwardType)dataReader.ReadByte();
                    var awardOperation = (AwardEvent.AwardOperation)dataReader.ReadByte();
                    var random = dataReader.ReadByte() != 0;
                    var awardTarget = (AwardEvent.AwardTarget)dataReader.ReadByte();
                    var unknown = dataReader.ReadByte();
                    var awardTypeValue = dataReader.ReadWord();
                    var value = dataReader.ReadWord();

                    mapEvent = new AwardEvent
                    {
                        TypeOfAward = awardType,
                        Operation = awardOperation,
                        Random = random,
                        Target = awardTarget,
                        AwardTypeValue = awardTypeValue,
                        Value = value,
                        Unknown = unknown
                    };
                    break;
                }
                case EventType.ChangeTile:
                {
                    var x = dataReader.ReadByte();
                    var y = dataReader.ReadByte();
                    var unknown = dataReader.ReadByte();
                    var tileData = dataReader.ReadBytes(4);
                    var mapIndex = dataReader.ReadWord();
                    mapEvent = new ChangeTileEvent
                    {
                        X = x,
                        Y = y,
                        BackTileIndex = ((uint)(tileData[1] & 0xe0) << 3) | tileData[0],
                        FrontTileIndex = ((uint)(tileData[2] & 0x07) << 8) | tileData[3],
                        MapIndex = mapIndex,
                        Unknown = unknown
                    };
                    break;
                }
                case EventType.StartBattle:
                {
                    var unknown1 = dataReader.ReadBytes(6);
                    var monsterGroupIndex = dataReader.ReadByte();
                    var unknown2 = dataReader.ReadBytes(2);
                    mapEvent = new StartBattleEvent
                    {
                        MonsterGroupIndex = monsterGroupIndex,
                        Unknown1 = unknown1,
                        Unknown2 = unknown2
                    };
                    break;
                }
                case EventType.Condition:
                {
                    var conditionType = (ConditionEvent.ConditionType)dataReader.ReadByte(); // TODO: this needs more research
                    var value = dataReader.ReadByte();
                    var unknown1 = dataReader.ReadBytes(4);
                    var objectIndex = dataReader.ReadByte();
                    var jumpToIfNotFulfilled = dataReader.ReadWord();
                    mapEvent = new ConditionEvent
                    {
                        TypeOfCondition = conditionType,
                        ObjectIndex = objectIndex,
                        Value = value,
                        Unknown1 = unknown1,
                        ContinueIfFalseWithMapEventIndex = jumpToIfNotFulfilled
                    };
                    break;
                }
                case EventType.Action:
                {
                    var actionType = (ActionEvent.ActionType)dataReader.ReadByte();
                    var value = dataReader.ReadByte();
                    var unknown1 = dataReader.ReadBytes(4);
                    var objectIndex = dataReader.ReadByte();
                    var unknown2 = dataReader.ReadBytes(2);
                    mapEvent = new ActionEvent
                    {
                        TypeOfAction = actionType,
                        ObjectIndex = objectIndex,
                        Value = value,
                        Unknown1 = unknown1,
                        Unknown2 = unknown2
                    };
                    break;
                }
                case EventType.Dice100Roll:
                {
                    var chance = dataReader.ReadByte();
                    var unused = dataReader.ReadBytes(6);
                    var jumpToIfNotFulfilled = dataReader.ReadWord();
                    mapEvent = new Dice100RollEvent
                    {
                        Chance = chance,
                        Unused = unused,
                        ContinueIfFalseWithMapEventIndex = jumpToIfNotFulfilled
                    };
                    break;
                }
                case EventType.Conversation:
                {
                    var interaction = (ConversationEvent.InteractionType)dataReader.ReadByte();
                    dataReader.Position += 4; // unused
                    var value = dataReader.ReadWord();
                    dataReader.Position += 2; // unused
                    mapEvent = new ConversationEvent
                    {
                        Interaction = interaction,
                        Value = value
                    };
                    break;
                }
                case EventType.PrintText:
                {
                    var npcTextIndex = dataReader.ReadByte();
                    dataReader.Position += 8; // unused
                    mapEvent = new PrintTextEvent
                    {
                        NPCTextIndex = npcTextIndex
                    };
                    break;
                }
                case EventType.Decision:
                {
                    var textIndex = dataReader.ReadByte();
                    var unknown1 = dataReader.ReadBytes(6);
                    var noEventIndex = dataReader.ReadWord();
                    mapEvent = new DecisionEvent
                    {
                        TextIndex = textIndex,
                        NoEventIndex = noEventIndex,
                        Unknown1 = unknown1
                    };
                    break;
                }
                case EventType.ChangeMusic:
                {
                    var musicIndex = dataReader.ReadWord();
                    var volume = dataReader.ReadByte();
                    var unknown1 = dataReader.ReadBytes(6);
                    mapEvent = new ChangeMusicEvent
                    {
                        MusicIndex = musicIndex,
                        Volume = volume,
                        Unknown1 = unknown1
                    };
                    break;
                }
                default:
                {
                    mapEvent = new DebugMapEvent
                    {
                        Data = dataReader.ReadBytes(9)
                    };
                    break;
                }
            }

            mapEvent.Type = type;

            return mapEvent;
        }
    }
}
