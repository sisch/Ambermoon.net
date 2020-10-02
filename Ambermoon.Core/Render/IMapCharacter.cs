﻿namespace Ambermoon.Render
{
    internal interface IMapCharacter
    {
        void Move(int x, int y, uint ticks);
        bool Interact(EventTrigger trigger);
        Position Position { get; }
    }
}
