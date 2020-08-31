﻿namespace Ambermoon.Data
{
    public interface ISavegameManager
    {
        Savegame LoadInitial(IGameData gameData, ISavegameSerializer savegameSerializer);
        Savegame Load(IGameData gameData, ISavegameSerializer savegameSerializer, int saveSlot);
    }
}