﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Ambermoon.Data.Legacy.ExecutableData
{
    /// <summary>
    /// This contains all relevant data from the executable.
    /// 
    /// The last RELOC32 hunk contains some offset mappings for the last
    /// data hunk which contains the relevant data. With the help of
    /// these offsets, we can safely locate the data.
    /// 
    /// There should be 15 relocation offsets for hunk 5 (which should
    /// be the last data hunk 0-based without END hunks).
    /// 
    /// If the first 12 offsets are added together the resulting
    /// offset (in bytes) points to the beginning of the section
    /// with codepage mappings which should be the first important
    /// data section.
    /// 
    /// The 13th and 15th offsets are longer ones. The 14th should
    /// be a small 4 byte offset.
    /// 
    /// If the 13th offset is added this points right after the
    /// rune code mapping and those additional 10 bytes:
    /// 03 00 1C 1C 1A 1A 19 1B 18 18
    /// 
    /// The next section starts with two identical dwords and then
    /// the first text section starts. The mentioned 14th offset
    /// points to the second dword.
    /// 
    /// After adding all 15 offsets together and adding 262 we
    /// are at the glyph data section. But if this address is not
    /// dword-aligned we have to do so (offset += (4 - offset % 4)).
    /// 
    /// TODO: This is work in progress!
    /// </summary>
    public class ExecutableData
    {
        public Glyphs Glyphs { get; }
        public Cursors Cursors { get; }
        public FileList FileList { get; }
        public WorldNames WorldNames { get; }
        public Messages Messages { get; }
        public AutomapNames AutomapNames { get; }
        public OptionNames OptionNames { get; }
        public SongNames SongNames { get; }
        public SpellTypeNames SpellTypeNames { get; }
        public SpellNames SpellNames { get; }
        public LanguageNames LanguageNames { get; }
        public ClassNames ClassNames { get; }
        public RaceNames RaceNames { get; }
        public AbilityNames AbilityNames { get; }
        public AttributeNames AttributeNames { get; }
        public ItemTypeNames ItemTypeNames { get; }
        public AilmentNames AilmentNames { get; }
        public UITexts UITexts { get; }

        static T Read<T>(IDataReader[] dataReaders, ref int readerIndex)
        {
            var type = typeof(T);
            var dataReader = dataReaders[readerIndex];

            return (T)Activator.CreateInstance(type, BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { dataReader }, null);
        }

        /* Some interesting offsets:
         * 
         * 2nd data hunk
         * =============
         * 
         * Offsets are for German 1.05.
         * 
         * 0x070e (4): Offset to text for male
         * 0x0712 (4): Offset to text for female
         * 0x0716 (4): Offset to text for both
         * 0x7AA0: Palette indices for event pix (only 8 of 9)
         * 
         * 0x8085: Name of the dictionary file. From here on the relative
         *         text offsets will differ between german and english version!
         */

        public ExecutableData(List<AmigaExecutable.IHunk> hunks)
        {
            var reloc32Hunk = (AmigaExecutable.Reloc32Hunk?)hunks.LastOrDefault(h => h.Type == AmigaExecutable.HunkType.RELOC32);
            var dataHunkReaders = hunks.Where(h => h.Type == AmigaExecutable.HunkType.Data)
                .Select(h => new DataReader(((AmigaExecutable.Hunk)h).Data)).ToArray();
            int dataHunkIndex = 0;

            if (reloc32Hunk == null || !reloc32Hunk.Value.Entries.ContainsKey(5) || reloc32Hunk.Value.Entries[5].Count != 15)
                throw new AmbermoonException(ExceptionScope.Data, "Unexpected executable format.");

            var relocOffsets = reloc32Hunk.Value.Entries[5];
            uint codepageOffset = relocOffsets.Take(12).Aggregate((a, b) => a + b);
            uint textOffset = codepageOffset + relocOffsets.Skip(12).Take(2).Aggregate((a, b) => a + b) + 4;
            uint glyphOffset = codepageOffset + relocOffsets.Skip(12).Aggregate((a, b) => a + b) + 262;
            glyphOffset += 4 - glyphOffset % 4;

            dataHunkReaders[1].Position = (int)glyphOffset;
            dataHunkIndex = 1;

            // TODO ...

            Glyphs = Read<Glyphs>(dataHunkReaders, ref dataHunkIndex);
            Cursors = Read<Cursors>(dataHunkReaders, ref dataHunkIndex);

            // TODO ...

            const string search = "Amberfiles/";
            dataHunkReaders[1].Position = (int)dataHunkReaders[1].FindString(search, dataHunkReaders[1].Position) + search.Length + 50;

            FileList = Read<FileList>(dataHunkReaders, ref dataHunkIndex);
            WorldNames = Read<WorldNames>(dataHunkReaders, ref dataHunkIndex);
            Messages = Read<Messages>(dataHunkReaders, ref dataHunkIndex);
            AutomapNames = Read<AutomapNames>(dataHunkReaders, ref dataHunkIndex);
            OptionNames = Read<OptionNames>(dataHunkReaders, ref dataHunkIndex);
            SongNames = Read<SongNames>(dataHunkReaders, ref dataHunkIndex);
            SpellTypeNames = Read<SpellTypeNames>(dataHunkReaders, ref dataHunkIndex);
            SpellNames = Read<SpellNames>(dataHunkReaders, ref dataHunkIndex);
            LanguageNames = Read<LanguageNames>(dataHunkReaders, ref dataHunkIndex);
            ClassNames = Read<ClassNames>(dataHunkReaders, ref dataHunkIndex);
            RaceNames = Read<RaceNames>(dataHunkReaders, ref dataHunkIndex);
            AbilityNames = Read<AbilityNames>(dataHunkReaders, ref dataHunkIndex);
            AttributeNames = Read<AttributeNames>(dataHunkReaders, ref dataHunkIndex);
            AbilityNames.AddShortNames(dataHunkReaders[dataHunkIndex]);
            AttributeNames.AddShortNames(dataHunkReaders[dataHunkIndex]);
            ItemTypeNames = Read<ItemTypeNames>(dataHunkReaders, ref dataHunkIndex);
            AilmentNames = Read<AilmentNames>(dataHunkReaders, ref dataHunkIndex);
            UITexts = Read<UITexts>(dataHunkReaders, ref dataHunkIndex);

            // TODO: There is a bunch of binary data (gfx maybe?)

            // TODO: Then finally the item data comes ...
            // They start with these 6 bytes: 01 92 01 92 82 15

            // TODO ...
        }
    }
}