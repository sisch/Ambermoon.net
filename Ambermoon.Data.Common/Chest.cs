﻿using Ambermoon.Data.Serialization;
using System.Linq;

namespace Ambermoon.Data
{
    public enum ChestType
    {        
        Pile, // will disappear after full looting, no items can be put back
        Chest // will stay there and new items can be added by the player
    }

    public class Chest : IItemStorage
    {
        public const int SlotsPerRow = 6;
        public const int SlotRows = 4;

        public ChestType Type { get; set; }
        public ItemSlot[,] Slots { get; } = new ItemSlot[SlotsPerRow, SlotRows];
        public uint Gold { get; set; }
        public uint Food { get; set; }

        public bool Empty => Gold == 0 && Food == 0 && !Slots.Cast<ItemSlot>().Any(s => s.Amount != 0);

        private Chest()
        {

        }

        public static Chest Load(IChestReader chestReader, IDataReader dataReader)
        {
            var chest = new Chest();

            chestReader.ReadChest(chest, dataReader);

            return chest;
        }

        public void ResetItem(int slot, ItemSlot item)
        {
            int column = slot % SlotsPerRow;
            int row = slot / SlotsPerRow;

            if (Slots[column, row].Add(item) != 0)
                throw new AmbermoonException(ExceptionScope.Application, "Unable to reset chest item.");
        }
    }
}
