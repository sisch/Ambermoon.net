﻿using Ambermoon.Data;
using Ambermoon.Data.Enumerations;
using Ambermoon.Render;
using Ambermoon.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ambermoon
{
    public class Game
    {
        class NameProvider : ITextNameProvider
        {
            readonly Game game;

            public NameProvider(Game game)
            {
                this.game = game;
            }

            /// <inheritdoc />
            public string LeadName => game.currentSavegame.PartyMembers.First(p => p.Alive).Name;
            /// <inheritdoc />
            public string SelfName => game.CurrentPartyMember.Name;
            /// <inheritdoc />
            public string CastName => game.CurrentCaster?.Name;
            /// <inheritdoc />
            public string InvnName => game.CurrentInventory?.Name;
            /// <inheritdoc />
            public string SubjName => game.CurrentPartyMember?.Name; // TODO
            /// <inheritdoc />
            public string Sex1Name => game.CurrentPartyMember.Gender == Gender.Male ? "he" : "she"; // TODO
            /// <inheritdoc />
            public string Sex2Name => game.CurrentPartyMember.Gender == Gender.Male ? "his" : "her"; // TODO
        }

        class Movement
        {
            readonly uint[] tickDivider;

            public uint TickDivider(bool is3D) => tickDivider[is3D ? 1 : 0];
            public uint MovementTicks(bool is3D) => TicksPerSecond / TickDivider(is3D);
            public float MoveSpeed3D { get; }
            public float TurnSpeed3D { get; }

            public Movement(bool legacyMode)
            {
                tickDivider = new uint[] { 8u, GetTickDivider3D(legacyMode) };
                MoveSpeed3D = GetMoveSpeed3D(legacyMode);
                TurnSpeed3D = GetTurnSpeed3D(legacyMode);
            }

            static uint GetTickDivider3D(bool legacyMode) => legacyMode ? 8u : 60u;
            static float GetMoveSpeed3D(bool legacyMode) => legacyMode ? 0.25f : 0.0625f;
            static float GetTurnSpeed3D(bool legacyMode) => legacyMode ? 15.0f : 3.00f;
        }

        class TimedGameEvent
        {
            public DateTime ExecutionTime;
            public Action Action;
        }

        // TODO: cleanup members
        public const int MaxPartyMembers = 6;
        const uint TicksPerSecond = 60;
        readonly bool legacyMode = false;
        public event Action QuitRequested;
        bool ingame = false;
        bool is3D = false;
        internal bool WindowActive => currentWindow.Window != Window.MapView;
        static readonly WindowInfo DefaultWindow = new WindowInfo { Window = Window.MapView };
        WindowInfo currentWindow = DefaultWindow;
        WindowInfo lastWindow = DefaultWindow;
        // Note: These are not meant for ingame stuff but for fade effects etc that use real time.
        readonly List<TimedGameEvent> timedEvents = new List<TimedGameEvent>();
        readonly Movement movement;
        internal uint CurrentTicks { get; private set; } = 0;
        uint lastMapTicksReset = 0;
        uint lastMoveTicksReset = 0;
        readonly NameProvider nameProvider;
        internal IDataNameProvider DataNameProvider { get; }
        readonly Layout layout;
        readonly IMapManager mapManager;
        readonly IItemManager itemManager;
        readonly IRenderView renderView;
        internal ISavegameManager SavegameManager { get; }
        readonly ISavegameSerializer savegameSerializer;
        Player player;
        PartyMember CurrentPartyMember { get; set; } = null;
        PartyMember CurrentInventory { get; set; } = null;
        internal int? CurrentInventoryIndex { get; private set; } = null;
        PartyMember CurrentCaster { get; set; } = null;
        public Map Map => !ingame ? null : is3D ? renderMap3D?.Map : renderMap2D?.Map;
        readonly bool[] keys = new bool[Enum.GetValues<Key>().Length];
        bool inputEnable = true;
        /// <summary>
        /// The 3x3 buttons will always be enabled!
        /// </summary>
        public bool InputEnable
        {
            get => inputEnable;
            set
            {
                if (inputEnable == value)
                    return;

                inputEnable = value;
                clickMoveActive = false;
                UntrapMouse();

                if (!inputEnable)
                    ResetMoveKeys();
            }
        }
        bool leftMouseDown = false;
        bool clickMoveActive = false;
        Rect trapMouseArea = null;
        Position lastMousePosition = new Position();
        readonly Position trappedMousePositionOffset = new Position();
        bool trapped => trapMouseArea != null;
        public event Action<bool> MouseTrappedChanged;
        /// <summary>
        /// All words you have heard about in conversations.
        /// </summary>
        readonly List<string> dictionary = new List<string>(); // TODO: read from savegame?
        public GameVariablePool GlobalVariables { get; } = new GameVariablePool(); // TODO: read from savegame?
        readonly Dictionary<uint, GameVariablePool> mapVariables = new Dictionary<uint, GameVariablePool>(); // TODO: read from savegame?
        public GameVariablePool GetMapVariables(Map map)
        {
            if (!mapVariables.ContainsKey(map.Index))
                return mapVariables[map.Index] = new GameVariablePool();

            return mapVariables[map.Index];
        }
        Savegame currentSavegame;

        // Rendering
        readonly Cursor cursor = null;
        RenderMap2D renderMap2D = null;
        Player2D player2D = null;
        RenderMap3D renderMap3D = null;
        Player3D player3D = null;
        readonly ICamera3D camera3D = null;
        readonly IRenderText messageText = null;
        readonly IRenderText windowTitle = null;
        /// <summary>
        /// Open chest which can be used to store items.
        /// </summary>
        internal bool StorageOpen { get; private set; }
        Rect mapViewArea = map2DViewArea;
        static readonly Rect map2DViewArea = new Rect(Global.Map2DViewX, Global.Map2DViewY,
            Global.Map2DViewWidth, Global.Map2DViewHeight);
        static readonly Rect map3DViewArea = new Rect(Global.Map3DViewX, Global.Map3DViewY,
            Global.Map3DViewWidth, Global.Map3DViewHeight);
        internal CursorType CursorType
        {
            get => cursor.Type;
            set
            {
                if (cursor.Type == value)
                    return;

                cursor.Type = value;

                if (!is3D && !WindowActive &&
                    (cursor.Type == CursorType.Eye ||
                    cursor.Type == CursorType.Hand))
                {
                    TrapMouse(new Rect(player2D.DisplayArea.X - 9, player2D.DisplayArea.Y - 9, 33, 49));
                }
                else if (!is3D && !WindowActive &&
                    cursor.Type == CursorType.Mouth)
                {
                    TrapMouse(new Rect(player2D.DisplayArea.X - 25, player2D.DisplayArea.Y - 25, 65, 65));
                }
                else
                {
                    UntrapMouse();
                }
            }
        }

        public Game(IRenderView renderView, IMapManager mapManager, IItemManager itemManager,
            ISavegameManager savegameManager, ISavegameSerializer savegameSerializer,
            IDataNameProvider dataNameProvider, Cursor cursor, bool legacyMode)
        {
            this.cursor = cursor;
            this.legacyMode = legacyMode;
            movement = new Movement(legacyMode);
            nameProvider = new NameProvider(this);
            this.renderView = renderView;
            this.mapManager = mapManager;
            this.itemManager = itemManager;
            this.SavegameManager = savegameManager;
            this.savegameSerializer = savegameSerializer;
            this.DataNameProvider = dataNameProvider;
            camera3D = renderView.Camera3D;
            messageText = renderView.RenderTextFactory.Create();
            messageText.Layer = renderView.GetLayer(Layer.Text);
            windowTitle = renderView.RenderTextFactory.Create(renderView.GetLayer(Layer.Text),
                renderView.TextProcessor.CreateText(""), TextColor.Gray, true,
                new Rect(8, 40, 192, 10), TextAlign.Center);
            layout = new Layout(this, renderView);
        }

        /// <summary>
        /// This is called when the game starts.
        /// This includes intro, main menu, etc.
        /// </summary>
        public void Run()
        {
            // TODO: For now we just start a new game.
            var initialSavegame = SavegameManager.LoadInitial(renderView.GameData, savegameSerializer);
            Start(initialSavegame);
        }

        public void Quit()
        {
            QuitRequested?.Invoke();
        }

        public void Update(double deltaTime)
        {
            for (int i = timedEvents.Count - 1; i >= 0; --i)
            {
                if (DateTime.Now >= timedEvents[i].ExecutionTime)
                {
                    timedEvents[i].Action?.Invoke();
                    timedEvents.RemoveAt(i);
                }
            }

            uint add = (uint)Util.Round(TicksPerSecond * (float)deltaTime);

            if (CurrentTicks <= uint.MaxValue - add)
                CurrentTicks += add;
            else
                CurrentTicks = (uint)(((long)CurrentTicks + add) % uint.MaxValue);

            if (ingame)
            {
                if (is3D)
                {
                    // TODO
                }
                else // 2D
                {
                    var animationTicks = CurrentTicks >= lastMapTicksReset ? CurrentTicks - lastMapTicksReset : (uint)((long)CurrentTicks + uint.MaxValue - lastMapTicksReset);
                    renderMap2D.UpdateAnimations(animationTicks);
                }
            }

            var moveTicks = CurrentTicks >= lastMoveTicksReset ? CurrentTicks - lastMoveTicksReset : (uint)((long)CurrentTicks + uint.MaxValue - lastMoveTicksReset);

            if (moveTicks >= movement.MovementTicks(is3D))
            {
                if (clickMoveActive)
                    HandleClickMovement();
                else
                    Move();

                lastMoveTicksReset = CurrentTicks;
            }

            layout.Update(CurrentTicks);
        }

        Position GetMousePosition(Position position)
        {
            if (trapMouseArea != null)
                position += trappedMousePositionOffset;

            return position;
        }

        void TrapMouse(Rect area)
        {
            trapMouseArea = renderView.GameToScreen(area);
            trappedMousePositionOffset.X = trapMouseArea.Left - lastMousePosition.X;
            trappedMousePositionOffset.Y = trapMouseArea.Top - lastMousePosition.Y;
            UpdateCursor(trapMouseArea.Position, MouseButtons.None);
            MouseTrappedChanged?.Invoke(true);
        }

        void UntrapMouse()
        {
            trapMouseArea = null;
            MouseTrappedChanged?.Invoke(false);
        }

        void ResetMoveKeys()
        {
            keys[(int)Key.Up] = false;
            keys[(int)Key.Down] = false;
            keys[(int)Key.Left] = false;
            keys[(int)Key.Right] = false;
            lastMoveTicksReset = CurrentTicks;
        }

        public Color GetTextColor(TextColor textColor) => GetPaletteColor(51, (int)textColor);

        public Color GetNamedPaletteColor(NamedPaletteColors namedPaletteColor) => GetPaletteColor(50, (int)namedPaletteColor);

        public Color GetPaletteColor(int paletteIndex, int colorIndex)
        {
            var paletteData = renderView.GraphicProvider.Palettes[paletteIndex].Data;
            return new Color
            (
                paletteData[colorIndex * 4 + 0],
                paletteData[colorIndex * 4 + 1],
                paletteData[colorIndex * 4 + 2],
                paletteData[colorIndex * 4 + 3]
            );
        }

        internal void Start2D(Map map, uint playerX, uint playerY, CharacterDirection direction)
        {
            if (map.Type != MapType.Map2D)
                throw new AmbermoonException(ExceptionScope.Application, "Given map is not 2D.");

            ResetMoveKeys();
            layout.SetLayout(LayoutType.Map2D,  movement.MovementTicks(false));
            is3D = false;

            if (renderMap2D.Map != map)
            {
                renderMap2D.SetMap
                (
                    map,
                    (uint)Util.Limit(0, (int)playerX - RenderMap2D.NUM_VISIBLE_TILES_X / 2, map.Width - RenderMap2D.NUM_VISIBLE_TILES_X),
                    (uint)Util.Limit(0, (int)playerY - RenderMap2D.NUM_VISIBLE_TILES_Y / 2, map.Height - RenderMap2D.NUM_VISIBLE_TILES_Y)
                );
            }
            else
            {
                renderMap2D.ScrollTo
                (
                    (uint)Util.Limit(0, (int)playerX - RenderMap2D.NUM_VISIBLE_TILES_X / 2, map.Width - RenderMap2D.NUM_VISIBLE_TILES_X),
                    (uint)Util.Limit(0, (int)playerY - RenderMap2D.NUM_VISIBLE_TILES_Y / 2, map.Height - RenderMap2D.NUM_VISIBLE_TILES_Y),
                    true
                );
            }

            if (player2D == null)
            {
                player2D = new Player2D(this, renderView.GetLayer(Layer.Characters), player, renderMap2D,
                    renderView.SpriteFactory, renderView.GameData, new Position(0, 0), mapManager);
            }

            player2D.Visible = true;
            player2D.MoveTo(map, playerX, playerY, CurrentTicks, true, direction);

            var mapOffset = map.MapOffset;
            player.Position.X = mapOffset.X + (int)playerX - (int)renderMap2D.ScrollX;
            player.Position.Y = mapOffset.Y + (int)playerY - (int)renderMap2D.ScrollY;
            player.Direction = direction;
            
            renderView.GetLayer(Layer.Map3D).Visible = false;
            renderView.GetLayer(Layer.Billboards3D).Visible = false;
            for (int i = (int)Global.First2DLayer; i <= (int)Global.Last2DLayer; ++i)
                renderView.GetLayer((Layer)i).Visible = true;

            mapViewArea = map2DViewArea;
        }

        internal void Start3D(Map map, uint playerX, uint playerY, CharacterDirection direction)
        {
            if (map.Type != MapType.Map3D)
                throw new AmbermoonException(ExceptionScope.Application, "Given map is not 3D.");

            ResetMoveKeys();
            layout.SetLayout(LayoutType.Map3D, movement.MovementTicks(true));

            is3D = true;
            renderMap2D.Destroy();
            renderMap3D.SetMap(map, playerX, playerY, direction);
            player3D.SetPosition((int)playerX, (int)playerY, CurrentTicks);
            if (player2D != null)
                player2D.Visible = false;
            player.Position.X = (int)playerX;
            player.Position.Y = (int)playerY;
            player.Direction = direction;
            
            renderView.GetLayer(Layer.Map3D).Visible = true;
            renderView.GetLayer(Layer.Billboards3D).Visible = true;
            for (int i = (int)Global.First2DLayer; i <= (int)Global.Last2DLayer; ++i)
                renderView.GetLayer((Layer)i).Visible = false;

            mapViewArea = map3DViewArea;
        }

        void Cleanup()
        {
            layout.Reset();
            renderMap2D?.Destroy();
            renderMap2D = null;
            renderMap3D?.Destroy();
            renderMap3D = null;
            player2D?.Destroy();
            player2D = null;
            player3D = null;
            messageText.Visible = false;

            player = null;
            CurrentPartyMember = null;
            CurrentInventory = null;
            CurrentInventoryIndex = null;
            CurrentCaster = null;
            StorageOpen = false;
            dictionary.Clear();
            GlobalVariables.Clear();
            mapVariables.Clear();

            for (int i = 0; i < keys.Length; ++i)
                keys[i] = false;
            leftMouseDown = false;
            clickMoveActive = false;
            trapMouseArea = null;
            trappedMousePositionOffset.X = 0;
            trappedMousePositionOffset.Y = 0;
            InputEnable = false;
        }

        public void Start(Savegame savegame)
        {
            Cleanup();

            ingame = true;
            currentSavegame = savegame;
            player = new Player();
            var map = mapManager.GetMap(savegame.CurrentMapIndex);
            bool is3D = map.Type == MapType.Map3D;
            renderMap2D = new RenderMap2D(this, null, mapManager, renderView);
            renderMap3D = new RenderMap3D(null, mapManager, renderView, 0, 0, CharacterDirection.Up);
            player3D = new Player3D(this, player, mapManager, camera3D, renderMap3D, 0, 0);
            player.MovementAbility = PlayerMovementAbility.Walking;
            renderMap2D.MapChanged += RenderMap2D_MapChanged;
            renderMap3D.MapChanged += RenderMap3D_MapChanged;
            if (is3D)
                Start3D(map, savegame.CurrentMapX - 1, savegame.CurrentMapY - 1, savegame.CharacterDirection);
            else
                Start2D(map, savegame.CurrentMapX - 1, savegame.CurrentMapY - 1 - (map.IsWorldMap ? 0u : 1u), savegame.CharacterDirection);
            player.Position.X = (int)savegame.CurrentMapX - 1;
            player.Position.Y = (int)savegame.CurrentMapY - 1;

            ShowMap(true);

            for (int i = 0; i < MaxPartyMembers; ++i)
            {
                if (savegame.CurrentPartyMemberIndices[i] != 0)
                {
                    var partyMember = savegame.GetPartyMember(i);
                    layout.SetCharacter(i, partyMember);
                }
                else
                {
                    layout.SetCharacter(i, null);
                }
            }
            CurrentPartyMember = GetPartyMember(currentSavegame.ActivePartyMemberSlot);
            SetActivePartyMember(currentSavegame.ActivePartyMemberSlot);

            InputEnable = true;

            // Trigger events after game load
            TriggerMapEvents(MapEventTrigger.Move, (uint)player.Position.X,
                (uint)player.Position.Y + (Map.IsWorldMap ? 0u : 1u));
        }

        void RunSavegameTileChangeEvents(uint mapIndex)
        {
            if (currentSavegame.TileChangeEvents.ContainsKey(mapIndex))
            {
                var tileChangeEvents = currentSavegame.TileChangeEvents[mapIndex];

                foreach (var tileChangeEvent in tileChangeEvents)
                    UpdateMapTile(tileChangeEvent);
            }
        }

        void RenderMap3D_MapChanged(Map map)
        {
            RunSavegameTileChangeEvents(map.Index);
        }

        void RenderMap2D_MapChanged(Map[] maps)
        {
            foreach (var map in maps)
                RunSavegameTileChangeEvents(map.Index);
        }

        public void LoadGame(int slot)
        {
            var savegame = SavegameManager.Load(renderView.GameData, savegameSerializer, slot);
            Start(savegame);
        }

        public void ContinueGame()
        {
            LoadGame(0);
        }

        public void ShowMessage(Rect bounds, string text, TextColor color, bool shadow, TextAlign textAlign = TextAlign.Left)
        {
            messageText.Text = renderView.TextProcessor.ProcessText(text, nameProvider, dictionary);
            messageText.TextColor = color;
            messageText.Shadow = shadow;
            messageText.Place(bounds, textAlign);
            messageText.Visible = true;
        }

        public void HideMessage()
        {
            messageText.Visible = false;
        }

        void HandleClickMovement()
        {
            if (WindowActive || !InputEnable || !clickMoveActive)
            {
                clickMoveActive = false;
                return;
            }

            lock (cursor)
            {
                Move(cursor.Type);
            }
        }

        internal void Move(CursorType cursorType)
        {
            if (is3D)
            {
                switch (cursorType)
                {
                    case CursorType.ArrowForward:
                        player3D.MoveForward(movement.MoveSpeed3D * Global.DistancePerTile, CurrentTicks);
                        break;
                    case CursorType.ArrowBackward:
                        player3D.MoveBackward(movement.MoveSpeed3D * Global.DistancePerTile, CurrentTicks);
                        break;
                    case CursorType.ArrowStrafeLeft:
                        player3D.MoveLeft(movement.MoveSpeed3D * Global.DistancePerTile, CurrentTicks);
                        break;
                    case CursorType.ArrowStrafeRight:
                        player3D.MoveRight(movement.MoveSpeed3D * Global.DistancePerTile, CurrentTicks);
                        break;
                    case CursorType.ArrowTurnLeft:
                        player3D.TurnLeft(movement.TurnSpeed3D * 0.7f);
                        player3D.MoveForward(movement.MoveSpeed3D * Global.DistancePerTile * 0.75f, CurrentTicks);
                        break;
                    case CursorType.ArrowTurnRight:
                        player3D.TurnRight(movement.TurnSpeed3D * 0.7f);
                        player3D.MoveForward(movement.MoveSpeed3D * Global.DistancePerTile * 0.75f, CurrentTicks);
                        break;
                    case CursorType.ArrowRotateLeft:
                        player3D.TurnLeft(movement.TurnSpeed3D * 0.7f);
                        player3D.MoveBackward(movement.MoveSpeed3D * Global.DistancePerTile * 0.75f, CurrentTicks);
                        break;
                    case CursorType.ArrowRotateRight:
                        player3D.TurnRight(movement.TurnSpeed3D * 0.7f);
                        player3D.MoveBackward(movement.MoveSpeed3D * Global.DistancePerTile * 0.75f, CurrentTicks);
                        break;
                    default:
                        clickMoveActive = false;
                        break;
                }
            }
            else
            {
                switch (cursorType)
                {
                    case CursorType.ArrowUpLeft:
                        Move2D(-1, -1);
                        break;
                    case CursorType.ArrowUp:
                        Move2D(0, -1);
                        break;
                    case CursorType.ArrowUpRight:
                        Move2D(1, -1);
                        break;
                    case CursorType.ArrowLeft:
                        Move2D(-1, 0);
                        break;
                    case CursorType.ArrowRight:
                        Move2D(1, 0);
                        break;
                    case CursorType.ArrowDownLeft:
                        Move2D(-1, 1);
                        break;
                    case CursorType.ArrowDown:
                        Move2D(0, 1);
                        break;
                    case CursorType.ArrowDownRight:
                        Move2D(1, 1);
                        break;
                    default:
                        clickMoveActive = false;
                        break;
                }
            }
        }

        bool Move2D(int x, int y)
        {
            bool diagonal = x != 0 && y != 0;

            if (!player2D.Move(x, y, CurrentTicks))
            {
                if (!diagonal)
                    return false;

                var prevDirection = player2D.Direction;

                if (!player2D.Move(0, y, CurrentTicks, prevDirection))
                    return player2D.Move(x, 0, CurrentTicks, prevDirection);
            }

            return true;
        }

        void Move()
        {
            if (WindowActive || !InputEnable)
                return;

            if (keys[(int)Key.Left] && !keys[(int)Key.Right])
            {
                if (!is3D)
                {
                    // diagonal movement is handled in up/down
                    if (!keys[(int)Key.Up] && !keys[(int)Key.Down])
                        Move2D(-1, 0);
                }
                else
                    player3D.TurnLeft(movement.TurnSpeed3D);
            }
            if (keys[(int)Key.Right] && !keys[(int)Key.Left])
            {
                if (!is3D)
                {
                    // diagonal movement is handled in up/down
                    if (!keys[(int)Key.Up] && !keys[(int)Key.Down])
                        Move2D(1, 0);
                }
                else
                    player3D.TurnRight(movement.TurnSpeed3D);
            }
            if (keys[(int)Key.Up] && !keys[(int)Key.Down])
            {
                if (!is3D)
                {
                    int x = keys[(int)Key.Left] && !keys[(int)Key.Right] ? -1 :
                        keys[(int)Key.Right] && !keys[(int)Key.Left] ? 1 : 0;
                    Move2D(x, -1);
                }
                else
                    player3D.MoveForward(movement.MoveSpeed3D * Global.DistancePerTile, CurrentTicks);
            }
            if (keys[(int)Key.Down] && !keys[(int)Key.Up])
            {
                if (!is3D)
                {
                    int x = keys[(int)Key.Left] && !keys[(int)Key.Right] ? -1 :
                        keys[(int)Key.Right] && !keys[(int)Key.Left] ? 1 : 0;
                    Move2D(x, 1);
                }
                else
                    player3D.MoveBackward(movement.MoveSpeed3D * Global.DistancePerTile, CurrentTicks);
            }
        }

        public void OnKeyDown(Key key, KeyModifiers modifiers)
        {
            if (!InputEnable)
            {
                if (key != Key.Escape && !(key >= Key.Num1 && key <= Key.Num9))
                    return;
            }

            keys[(int)key] = true;

            if (!WindowActive)
                Move();

            switch (key)
            {
                case Key.Escape:
                {
                    if (ingame)
                    {
                        if (layout.PopupActive)
                            layout.ClosePopup();
                        else
                            CloseWindow();
                    }

                    break;
                }
                case Key.F1:
                case Key.F2:
                case Key.F3:
                case Key.F4:
                case Key.F5:
                case Key.F6:
                    if (!layout.PopupActive)
                        OpenPartyMember(key - Key.F1);
                    break;
                case Key.Num1:
                case Key.Num2:
                case Key.Num3:
                case Key.Num4:
                case Key.Num5:
                case Key.Num6:
                case Key.Num7:
                case Key.Num8:
                case Key.Num9:
                {
                    if (layout.PopupDisableButtons)
                        break;

                    int index = key - Key.Num1;
                    int column = index % 3;
                    int row = 2 - index / 3;
                    var newCursorType = layout.PressButton(column + row * 3, CurrentTicks);

                    if (newCursorType != null)
                        CursorType = newCursorType.Value;

                    break;
                }
                default:
                    if (WindowActive)
                        layout.KeyDown(key, modifiers);
                    break;
            }

            lastMoveTicksReset = CurrentTicks;
        }

        public void OnKeyUp(Key key, KeyModifiers modifiers)
        {
            if (!InputEnable)
            {
                if (key != Key.Escape && !(key >= Key.Num1 && key <= Key.Num9))
                    return;
            }

            keys[(int)key] = false;

            switch (key)
            {
                case Key.Num1:
                case Key.Num2:
                case Key.Num3:
                case Key.Num4:
                case Key.Num5:
                case Key.Num6:
                case Key.Num7:
                case Key.Num8:
                case Key.Num9:
                {
                    int index = key - Key.Num1;
                    int column = index % 3;
                    int row = 2 - index / 3;
                    layout.ReleaseButton(column + row * 3);

                    break;
                }
            }
        }

        public void OnKeyChar(char keyChar)
        {
            if (!InputEnable)
                return;

            if (keyChar >= '1' && keyChar <= '6')
            {
                SetActivePartyMember(keyChar - '1');
            }
        }

        public void OnMouseUp(Position position, MouseButtons buttons)
        {
            position = GetMousePosition(position);

            if (buttons.HasFlag(MouseButtons.Right))
            {
                layout.RightMouseUp(renderView.ScreenToGame(position), out CursorType? cursorType, CurrentTicks);

                if (cursorType != null)
                    CursorType = cursorType.Value;
            }
            if (buttons.HasFlag(MouseButtons.Left))
            {
                leftMouseDown = false;
                clickMoveActive = false;

                layout.LeftMouseUp(renderView.ScreenToGame(position), out CursorType? cursorType, CurrentTicks);

                if (cursorType != null && cursorType != CursorType.None)
                    CursorType = cursorType.Value;
                else
                    UpdateCursor(position, MouseButtons.None);
            }
        }

        public void OnMouseDown(Position position, MouseButtons buttons)
        {
            position = GetMousePosition(position);

            if (buttons.HasFlag(MouseButtons.Left))
                leftMouseDown = true;

            if (ingame)
            {
                var relativePosition = renderView.ScreenToGame(position);

                if (!WindowActive && InputEnable && mapViewArea.Contains(relativePosition))
                {
                    // click into the map area
                    if (buttons == MouseButtons.Right)
                        CursorType = CursorType.Sword;
                    if (!buttons.HasFlag(MouseButtons.Left))
                        return;

                    relativePosition.Offset(-mapViewArea.Left, -mapViewArea.Top);

                    if (cursor.Type == CursorType.Eye)
                        TriggerMapEvents(MapEventTrigger.Eye, relativePosition);
                    else if (cursor.Type == CursorType.Hand)
                        TriggerMapEvents(MapEventTrigger.Hand, relativePosition);
                    else if (cursor.Type == CursorType.Mouth)
                        TriggerMapEvents(MapEventTrigger.Mouth, relativePosition);
                    else if (cursor.Type > CursorType.Sword && cursor.Type < CursorType.Wait)
                    {
                        clickMoveActive = true;
                        HandleClickMovement();
                    }

                    if (cursor.Type > CursorType.Wait)
                        CursorType = CursorType.Sword;
                    return;
                }
                else
                {
                    var cursorType = CursorType.Sword;
                    layout.Click(relativePosition, buttons, ref cursorType, CurrentTicks);
                    CursorType = cursorType;

                    if (InputEnable)
                    {
                        layout.Hover(relativePosition, ref cursorType); // Update cursor
                        if (cursor.Type != CursorType.None)
                            CursorType = cursorType;
                    }

                    // TODO: check for other clicks
                }
            }
            else
            {
                CursorType = CursorType.Sword;
            }
        }

        void UpdateCursor(Position cursorPosition, MouseButtons buttons)
        {
            lock (cursor)
            {
                cursor.UpdatePosition(cursorPosition);

                if (!InputEnable)
                {
                    if (layout.PopupActive)
                    {
                        var cursorType = layout.PopupClickCursor ? CursorType.Click : CursorType.Sword;
                        layout.Hover(renderView.ScreenToGame(cursorPosition), ref cursorType);
                        CursorType = cursorType;
                    }
                    else if (layout.Type == LayoutType.Event)
                        CursorType = CursorType.Click;
                    else
                        CursorType = CursorType.Sword;

                    return;
                }

                var relativePosition = renderView.ScreenToGame(cursorPosition);

                if (!WindowActive && (mapViewArea.Contains(relativePosition) || clickMoveActive))
                {
                    // Change arrow cursors when hovering the map
                    if (ingame && cursor.Type >= CursorType.Sword && cursor.Type <= CursorType.Wait)
                    {
                        if (Map.Type == MapType.Map2D)
                        {
                            var playerArea = player2D.DisplayArea;

                            bool left = relativePosition.X < playerArea.Left;
                            bool right = relativePosition.X >= playerArea.Right;
                            bool up = relativePosition.Y < playerArea.Top;
                            bool down = relativePosition.Y >= playerArea.Bottom;

                            if (up)
                            {
                                if (left)
                                    CursorType = CursorType.ArrowUpLeft;
                                else if (right)
                                    CursorType = CursorType.ArrowUpRight;
                                else
                                    CursorType = CursorType.ArrowUp;
                            }
                            else if (down)
                            {
                                if (left)
                                    CursorType = CursorType.ArrowDownLeft;
                                else if (right)
                                    CursorType = CursorType.ArrowDownRight;
                                else
                                    CursorType = CursorType.ArrowDown;
                            }
                            else
                            {
                                if (left)
                                    CursorType = CursorType.ArrowLeft;
                                else if (right)
                                    CursorType = CursorType.ArrowRight;
                                else
                                    CursorType = CursorType.Wait;
                            }
                        }
                        else
                        {
                            relativePosition.Offset(-mapViewArea.Left, -mapViewArea.Top);

                            int horizontal = relativePosition.X / (mapViewArea.Width / 3);
                            int vertical = relativePosition.Y / (mapViewArea.Height / 3);

                            if (vertical <= 0) // up
                            {
                                if (horizontal <= 0) // left
                                    CursorType = CursorType.ArrowTurnLeft;
                                else if (horizontal >= 2) // right
                                    CursorType = CursorType.ArrowTurnRight;
                                else
                                    CursorType = CursorType.ArrowForward;
                            }
                            else if (vertical >= 2) // down
                            {
                                if (horizontal <= 0) // left
                                    CursorType = CursorType.ArrowRotateLeft;
                                else if (horizontal >= 2) // right
                                    CursorType = CursorType.ArrowRotateRight;
                                else
                                    CursorType = CursorType.ArrowBackward;
                            }
                            else
                            {
                                if (horizontal <= 0) // left
                                    CursorType = CursorType.ArrowStrafeLeft;
                                else if (horizontal >= 2) // right
                                    CursorType = CursorType.ArrowStrafeRight;
                                else
                                    CursorType = CursorType.Wait;
                            }
                        }

                        return;
                    }
                }
                else
                {
                    if (buttons == MouseButtons.None)
                    {
                        var cursorType = cursor.Type;
                        layout.Hover(relativePosition, ref cursorType);
                        CursorType = cursorType;
                    }
                    else if (buttons == MouseButtons.Left)
                    {
                        var cursorType = cursor.Type;
                        layout.Drag(relativePosition, ref cursorType);
                        CursorType = cursorType;
                    }
                }

                if (cursor.Type >= CursorType.ArrowUp && cursor.Type <= CursorType.Wait)
                    CursorType = CursorType.Sword;
            }
        }

        public void OnMouseMove(Position position, MouseButtons buttons)
        {
            if (!InputEnable)
                UntrapMouse();

            if (trapped)
            {
                var trappedPosition = position + trappedMousePositionOffset;

                if (trappedPosition.X < trapMouseArea.Left)
                {
                    if (position.X < lastMousePosition.X)
                        trappedMousePositionOffset.X += lastMousePosition.X - position.X;
                }                    
                else if (trappedPosition.X >= trapMouseArea.Right)
                {
                    if (position.X > lastMousePosition.X)
                        trappedMousePositionOffset.X -= position.X - lastMousePosition.X;
                }

                if (trappedPosition.Y < trapMouseArea.Top)
                {
                    if (position.Y < lastMousePosition.Y)
                        trappedMousePositionOffset.Y += lastMousePosition.Y - position.Y;
                }
                else if (trappedPosition.Y >= trapMouseArea.Bottom)
                {
                    if (position.Y > lastMousePosition.Y)
                        trappedMousePositionOffset.Y -= position.Y - lastMousePosition.Y;
                }
            }

            lastMousePosition = new Position(position);
            position = GetMousePosition(position);
            UpdateCursor(position, buttons);
        }

        internal PartyMember GetPartyMember(int slot) => currentSavegame.GetPartyMember(slot);
        internal Chest GetChest(uint index) => currentSavegame.Chests[(int)index];
        internal Merchant GetMerchant(uint index) => currentSavegame.Merchants[(int)index];

        /// <summary>
        /// Triggers map events with the given trigger and position.
        /// </summary>
        /// <param name="trigger">Trigger</param>
        /// <param name="position">Position inside the map view</param>
        void TriggerMapEvents(MapEventTrigger trigger, Position position)
        {
            if (is3D)
            {
                // TODO
                // renderMap3D.Map.TriggerEvents(this, player3D, trigger, x, y, mapManager, currentTicks);
            }
            else // 2D
            {
                var tilePosition = renderMap2D.PositionToTile(position);
                TriggerMapEvents(trigger, (uint)tilePosition.X, (uint)tilePosition.Y);
            }
        }

        void TriggerMapEvents(MapEventTrigger trigger, uint x, uint y)
        {
            if (is3D)
            {
                // TODO
                // renderMap3D.Map.TriggerEvents(this, player3D, trigger, x, y, mapManager, currentTicks);
            }
            else // 2D
            {
                renderMap2D.TriggerEvents(player2D, trigger, x, y, mapManager, CurrentTicks);
            }
        }

        void ShowMap(bool show)
        {
            if (show)
            {
                layout.CancelDrag();
                ResetCursor();
                StorageOpen = false;
                string mapName = Map.IsWorldMap
                    ? DataNameProvider.GetWorldName(Map.World)
                    : Map.Name;
                windowTitle.Text = renderView.TextProcessor.CreateText(mapName);
                windowTitle.TextColor = TextColor.Gray;
            }

            windowTitle.Visible = show;

            if (is3D)
            {
                if (show)
                    layout.SetLayout(LayoutType.Map3D, movement.MovementTicks(true));
                renderView.GetLayer(Layer.Map3D).Visible = show;
                renderView.GetLayer(Layer.Billboards3D).Visible = show;
            }
            else
            {
                if (show)
                    layout.SetLayout(LayoutType.Map2D, movement.MovementTicks(false));
                for (int i = (int)Global.First2DLayer; i <= (int)Global.Last2DLayer; ++i)
                    renderView.GetLayer((Layer)i).Visible = show;
            }

            if (show)
            {
                layout.Reset();
                SetWindow(Window.MapView);
            }
        }

        internal void OpenPartyMember(int slot)
        {
            if (currentSavegame.CurrentPartyMemberIndices[slot] == 0)
                return;

            Action openAction = () =>
            {
                layout.Reset();
                ShowMap(false);
                SetWindow(Window.Inventory, slot);
                layout.SetLayout(LayoutType.Inventory);

                windowTitle.Text = renderView.TextProcessor.CreateText(DataNameProvider.InventoryTitleString);
                windowTitle.TextColor = TextColor.White;
                windowTitle.Visible = true;

                CurrentInventoryIndex = slot;
                var partyMember = GetPartyMember(slot);
                #region Equipment and Inventory
                var equipmentSlotPositions = new List<Position>
                {
                    new Position(20, 72),  new Position(52, 72),  new Position(84, 72),
                    new Position(20, 124), new Position(84, 97), new Position(84, 124),
                    new Position(20, 176), new Position(52, 176), new Position(84, 176),
                };
                var inventorySlotPositions = Enumerable.Range(0, Inventory.VisibleWidth * Inventory.VisibleHeight).Select
                (
                    slot => new Position(109 + (slot % Inventory.Width) * 22, 76 + (slot / Inventory.Width) * 29)
                ).ToList();
                var inventoryGrid = ItemGrid.CreateInventory(layout, slot, renderView, itemManager, inventorySlotPositions);
                layout.AddItemGrid(inventoryGrid);
                for (int i = 0; i < partyMember.Inventory.Slots.Length; ++i)
                {
                    if (!partyMember.Inventory.Slots[i].Empty)
                        inventoryGrid.SetItem(i, partyMember.Inventory.Slots[i]);
                }
                var equipmentGrid = ItemGrid.CreateEquipment(layout, slot, renderView, itemManager, equipmentSlotPositions);
                layout.AddItemGrid(equipmentGrid);
                foreach (var equipmentSlot in Enum.GetValues<EquipmentSlot>().Skip(1))
                {
                    if (!partyMember.Equipment.Slots[equipmentSlot].Empty)
                        equipmentGrid.SetItem((int)equipmentSlot - 1, partyMember.Equipment.Slots[equipmentSlot]);
                }
                equipmentGrid.ItemDragged += (int slot, Item item) =>
                {
                    if (item.NumberOfHands == 2 && slot == (int)EquipmentSlot.RightHand - 1)
                        equipmentGrid.SetItem(slot + 2, null);

                    // TODO: rings/fingers
                };
                equipmentGrid.ItemDropped += (int slot, Item item) =>
                {
                    if (item.NumberOfHands == 2 && slot == (int)EquipmentSlot.RightHand - 1)
                        equipmentGrid.SetItem((int)EquipmentSlot.LeftHand - 1, new ItemSlot { ItemIndex = 0, Amount = 1 });

                    // TODO: rings/fingers
                };
                #endregion
                #region Character info
                layout.FillArea(new Rect(208, 49, 96, 80), Color.LightGray, false);
                layout.AddSprite(new Rect(208, 49, 32, 34), Graphics.UICustomGraphicOffset + (uint)UICustomGraphic.PortraitBackground, 50, 1);
                layout.AddSprite(new Rect(208, 49, 32, 34), Graphics.PortraitOffset + partyMember.PortraitIndex - 1, 49, 2);
                layout.AddText(new Rect(242, 49, 62, 7), DataNameProvider.GetRaceName(partyMember.Race));
                layout.AddText(new Rect(242, 56, 62, 7), DataNameProvider.GetGenderName(partyMember.Gender));
                layout.AddText(new Rect(242, 63, 62, 7), string.Format(DataNameProvider.CharacterInfoAgeString.Replace("000", "0"),
                    partyMember.Attributes[Data.Attribute.Age].CurrentValue));
                layout.AddText(new Rect(242, 70, 62, 7), $"{DataNameProvider.GetClassName(partyMember.Class)} {partyMember.Level}");
                layout.AddText(new Rect(242, 77, 62, 7), string.Format(DataNameProvider.CharacterInfoExperiencePointsString.Replace("0000000000", "0"),
                    partyMember.ExperiencePoints));
                layout.AddText(new Rect(208, 84, 96, 7), partyMember.Name, TextColor.Yellow, TextAlign.Center);
                layout.AddText(new Rect(208, 91, 96, 7), string.Format(DataNameProvider.CharacterInfoHitPointsString,
                    partyMember.HitPoints.CurrentValue, partyMember.HitPoints.MaxValue), TextColor.White, TextAlign.Center);
                layout.AddText(new Rect(208, 98, 96, 7), string.Format(DataNameProvider.CharacterInfoSpellPointsString,
                    partyMember.SpellPoints.CurrentValue, partyMember.SpellPoints.MaxValue), TextColor.White, TextAlign.Center);
                layout.AddText(new Rect(208, 105, 96, 7),
                    string.Format(DataNameProvider.CharacterInfoSpellLearningPointsString, partyMember.SpellLearningPoints) + " " +
                    string.Format(DataNameProvider.CharacterInfoTrainingPointsString, partyMember.TrainingPoints), TextColor.White, TextAlign.Center);
                layout.AddText(new Rect(208, 112, 96, 7), string.Format(DataNameProvider.CharacterInfoGoldAndFoodString, partyMember.Gold, partyMember.Food),
                    TextColor.White, TextAlign.Center);
                #endregion

                // TODO
            };

            if (currentWindow.Window == Window.Inventory)
                openAction();
            else
                Fade(openAction);
        }

        void AddTimedEvent(TimeSpan delay, Action action)
        {
            timedEvents.Add(new TimedGameEvent
            {
                ExecutionTime = DateTime.Now + delay,
                Action = action
            });
        }

        void Fade(Action midFadeAction)
        {
            layout.AddFadeEffect(new Rect(0, 36, Global.VirtualScreenWidth, Global.VirtualScreenHeight - 36), Color.Black, FadeEffectType.FadeInAndOut, 400);
            AddTimedEvent(TimeSpan.FromMilliseconds(200), midFadeAction);
        }

        internal void Teleport(MapChangeEvent mapChangeEvent)
        {
            Fade(() =>
            {
                var newMap = mapManager.GetMap(mapChangeEvent.MapIndex);
                var player = is3D ? (IRenderPlayer)player3D : player2D;

                // The position (x, y) is 1-based in the data so we subtract 1.
                // Moreover the players position is 1 tile below its drawing position
                // in non-world 2D so subtract another 1 from y.
                player.MoveTo(newMap, mapChangeEvent.X - 1,
                    mapChangeEvent.Y - (newMap.Type == MapType.Map2D && !newMap.IsWorldMap ? 2u : 1u),
                    CurrentTicks, true, mapChangeEvent.Direction);
                ShowMap(true);

                // Trigger events after map transition
                TriggerMapEvents(MapEventTrigger.Move, (uint)player.Position.X,
                    (uint)player.Position.Y + (Map.IsWorldMap ? 0u : 1u));
            });
        }

        internal void UpdateMapTile(ChangeTileEvent changeTileEvent)
        {
            bool sameMap = changeTileEvent.MapIndex == 0 || changeTileEvent.MapIndex == Map.Index;
            var map = sameMap ? Map : mapManager.GetMap(changeTileEvent.MapIndex);
            uint x = changeTileEvent.X - 1;
            uint y = changeTileEvent.Y - 1;

            if (is3D)
            {
                map.Blocks[x, y].ObjectIndex = changeTileEvent.FrontTileIndex <= 100 ? changeTileEvent.FrontTileIndex : 0;
                map.Blocks[x, y].WallIndex = changeTileEvent.FrontTileIndex >= 101 && changeTileEvent.FrontTileIndex < 255 ? changeTileEvent.FrontTileIndex - 100 : 0;

                if (sameMap)
                    renderMap3D.UpdateBlock(x, y);
            }
            else // 2D
            {
                map.UpdateTile(x, y, changeTileEvent.BackTileIndex, changeTileEvent.FrontTileIndex, mapManager.GetTilesetForMap(map));

                if (sameMap) // TODO: what if we change an adjacent world map which is visible instead? is there even a use case?
                    renderMap2D.UpdateTile(x, y);
            }
        }

        internal void ShowChest(ChestMapEvent chestMapEvent)
        {
            Fade(() =>
            {
                layout.Reset();
                ShowMap(false);
                SetWindow(Window.Chest, chestMapEvent);
                layout.SetLayout(LayoutType.Items);
                var chest = GetChest(chestMapEvent.ChestIndex);
                var itemSlotPositions = Enumerable.Range(1, 6).Select(index => new Position(index * 22, 139)).ToList();
                itemSlotPositions.AddRange(Enumerable.Range(1, 6).Select(index => new Position(index * 22, 168)));
                var itemGrid = ItemGrid.Create(layout, renderView, itemManager, itemSlotPositions, !chestMapEvent.RemoveWhenEmpty,
                    12, 6, 24, new Rect(7 * 22, 139, 6, 53), new Size(6, 27), ScrollbarType.SmallVertical);
                layout.AddItemGrid(itemGrid);

                if (!chestMapEvent.RemoveWhenEmpty)
                    StorageOpen = true;

                if (currentSavegame.IsChestLocked(chestMapEvent.ChestIndex))
                {
                    layout.Set80x80Picture(Picture80x80.ChestClosed);
                    itemGrid.Disabled = true;
                }
                else
                {
                    if (chest.Empty)
                    {
                        layout.Set80x80Picture(Picture80x80.ChestOpenEmpty);
                    }
                    else
                    {
                        layout.Set80x80Picture(Picture80x80.ChestOpenFull);
                    }

                    for (int y = 0; y < 2; ++y)
                    {
                        for (int x = 0; x < 6; ++x)
                        {
                            var slot = chest.Slots[x, y];

                            if (!slot.Empty)
                                itemGrid.SetItem(x + y * 6, slot);
                        }
                    }

                    // TODO: gold and food
                }
            });
        }

        internal void ShowTextPopup(PopupTextEvent popupTextEvent, Action<PopupTextEvent.Response> responseHandler)
        {
            var text = renderView.TextProcessor.ProcessText(Map.Texts[(int)popupTextEvent.TextIndex], nameProvider, dictionary);

            if (popupTextEvent.HasImage)
            {
                // Those always use a custom layout
                Fade(() =>
                {
                    SetWindow(Window.Event);
                    layout.SetLayout(LayoutType.Event);
                    ShowMap(false);
                    layout.Reset();
                    layout.AddEventPicture(popupTextEvent.EventImageIndex);
                    layout.FillArea(new Rect(16, 138, 288, 55), GetPaletteColor(50, 28), false);

                    // Position = 18,139, max 40 chars per line and 7 lines.
                    var textArea = new Rect(18, 139, 285, 49);
                    //var wrappedText = renderView.TextProcessor.WrapText(text, textArea, new Size(Global.GlyphWidth, Global.GlyphLineHeight));
                    var scrollableText = layout.AddScrollableText(textArea, text, TextColor.Gray);
                    scrollableText.Clicked += scrolledToEnd =>
                    {
                        if (scrolledToEnd)
                            CloseWindow();
                    };
                    CursorType = CursorType.Click;
                    InputEnable = false;

                    // TODO ...
                });
            }
            else
            {
                // Simple text popup
                layout.OpenTextPopup(text, () =>
                {
                    InputEnable = true;
                    ResetCursor();
                    responseHandler?.Invoke(PopupTextEvent.Response.Close);
                }, true, true);
                InputEnable = false;
                CursorType = CursorType.Click;
            }
        }

        internal void ShowDecisionPopup(DecisionEvent decisionEvent, Action<PopupTextEvent.Response> responseHandler)
        {
            var text = renderView.TextProcessor.ProcessText(Map.Texts[(int)decisionEvent.TextIndex], nameProvider, dictionary);
            layout.OpenYesNoPopup
            (
                text,
                () =>
                {
                    layout.ClosePopup(false);
                    InputEnable = true;
                    responseHandler?.Invoke(PopupTextEvent.Response.Yes);
                },
                () =>
                {
                    layout.ClosePopup(false);
                    InputEnable = true;
                    responseHandler?.Invoke(PopupTextEvent.Response.No);
                },
                () =>
                {
                    InputEnable = true;
                    responseHandler?.Invoke(PopupTextEvent.Response.Close);
                }
            );
            InputEnable = false;
            CursorType = CursorType.Sword;
        }

        internal void SetActivePartyMember(int index)
        {
            var partyMember = GetPartyMember(index);

            if (partyMember != null)
            {
                currentSavegame.ActivePartyMemberSlot = index;
                CurrentPartyMember = partyMember;
                layout.SetActiveCharacter(index, currentSavegame.PartyMembers);
            }
        }

        /// <summary>
        /// Returns the remaining amount of items that could not
        /// be dropped or 0 if all items were dropped successfully.
        /// </summary>
        /// <returns></returns>
        internal int DropItem(int partyMemberIndex, int? slotIndex, ItemSlot item, bool allowItemExchange)
        {
            var partyMember = GetPartyMember(partyMemberIndex);

            if (partyMember == null)
                return item.Amount;

            var slots = slotIndex == null
                ? partyMember.Inventory.Slots.Where(s => s.ItemIndex == item.ItemIndex && s.Amount < 99).ToArray()
                : new ItemSlot[1] { partyMember.Inventory.Slots[slotIndex.Value] };

            if (slots.Length == 0) // no slot found -> try any empty slot
            {
                var emptySlot = partyMember.Inventory.Slots.FirstOrDefault(s => s.Empty);

                if (emptySlot == null) // no free slot
                    return item.Amount;

                // This reduces item.Amount internally.
                return emptySlot.Add(item);
            }

            // Special case: Exchange item at a single slot
            if (allowItemExchange && slots.Length == 1 && slots[0].ItemIndex != item.ItemIndex)
            {
                slots[0].Exchange(item);
                return item.Amount;
            }

            foreach (var slot in slots)
            {
                // This reduces item.Amount internally.
                slot.Add(item);

                if (item.Empty)
                    return 0;
            }

            return item.Amount;
        }

        void SetWindow(Window window, object param = null)
        {
            lastWindow = currentWindow;
            currentWindow = new WindowInfo { Window = window, WindowParameter = param };
        }

        void ResetCursor()
        {
            if (CursorType == CursorType.Click ||
                CursorType == CursorType.SmallArrow ||
                CursorType == CursorType.None)
            {
                CursorType = CursorType.Sword;
            }
            UpdateCursor(lastMousePosition, MouseButtons.None);
        }

        internal void CloseWindow()
        {
            if (!WindowActive)
                return;

            if (currentWindow.Window == Window.Event)
            {
                InputEnable = true;
                ResetCursor();
            }

            if (currentWindow.Window == lastWindow.Window)
                currentWindow = DefaultWindow;
            else
                currentWindow = lastWindow;

            switch (currentWindow.Window)
            {
                case Window.MapView:
                    Fade(() => ShowMap(true));
                    break;
                case Window.Inventory:
                {
                    int partyMemberIndex = (int)currentWindow.WindowParameter;
                    currentWindow = DefaultWindow;
                    OpenPartyMember(partyMemberIndex);
                    break;
                }
                case Window.Stats:
                {
                    // TODO
                    break;
                }
                case Window.Chest:
                {
                    var chestEvent = (ChestMapEvent)currentWindow.WindowParameter;
                    currentWindow = DefaultWindow;
                    ShowChest(chestEvent);
                    break;
                }
                case Window.Merchant:
                {
                    // TODO
                    break;
                }
                default:
                    break;
            }
        }
    }
}
