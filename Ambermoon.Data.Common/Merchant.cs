﻿using Ambermoon.Data.Serialization;

namespace Ambermoon.Data
{
    public class Merchant
    {
        public ItemSlot[,] Slots { get; } = new ItemSlot[6, 4];

        private Merchant()
        {

        }

        public static Merchant Load(IMerchantReader merchantReader, IDataReader dataReader)
        {
            var merchant = new Merchant();

            merchantReader.ReadMerchant(merchant, dataReader);

            return merchant;
        }
    }
}
