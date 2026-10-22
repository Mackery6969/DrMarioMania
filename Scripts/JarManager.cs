using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using static PowerUpEnums;

public partial class JarManager : Node
{
    // handles an individual player's jar, including checking for matching colours, destroying segments, causing tiles to fall, etc AND scoring
    // basically a player-spefific game manager rather than the entire game

    [ExportGroup("Tilemap")]
    [Export] private TileMapLayer jarTiles;
    public TileMapLayer JarTiles { get { return jarTiles; } }
    [Export] private TileMapLayer previewTiles;
    public TileMapLayer PreviewTiles { get { return previewTiles; } }
    [Export] private TileMapLayer foregroundTiles;
    public TileMapLayer ForegroundTiles { get { return foregroundTiles; } }

    // Top-left tile of jar
    [Export] private Vector2I jarOrigin;
    public Vector2I JarOrigin { get { return jarOrigin; } }
    public Vector2I JarSize { get { return PlayerGameSettings.JarSize; } }
    private Vector2I baseJarSize = new Vector2I(8, 16);
    public Vector2I BaseJarSize { get { return baseJarSize; } }

    [ExportGroup("Speeds")]
    [Export] private float destroyRowSpeed;
    [Export] private float destroyDisappearSpeed;
    public float DestroyDisappearSpeed { get { return destroyDisappearSpeed; } }
    [Export] private int virusGenerationSpeed;

    [ExportGroup("References")]
    [Export] private PowerUpPrefabs powerUpPrefabs;
    private PowerUpMeter PowerUpMeter { get { return uiMan.PowerUpMeter; } }
    private VirusRing VirusRing { get { return uiMan.VirusRing; } }
    [Export] private JarUIManager uiMan;
    public JarUIManager UIMan { get { return uiMan; } }
    [Export] private PillManager pillMan;
    public PillManager PillMan { get { return pillMan; } }

    // common/global settings shared between all players
    public CommonGameSettings CommonGameSettings { get; set; }
    // player-specific game settings, this will either be the player's own chosen settings or the one provided by the custom level
    public PlayerGameSettings PlayerGameSettings { get; set; }
    public PlayerMultiInputSettings PlayerMultiInputSettings { get; set; }
    public HighScoreList HighScoreList { get; set; }
    public int PlayerID { get; set; }

    private SfxManager sfxMan;
    public SfxManager SfxMan
    {
        get
        {
            return sfxMan;
        }
        set
        {
            sfxMan = value;
            PillMan.SfxMan = value;
            if (VirusRing != null)
                VirusRing.SfxMan = value;
        }
    }
    public GameManager GameMan { get; set; }
    public GameThemer GameThemer { get; set; }

    public Vector2 TilemapGlobalPos { get { return jarTiles.GlobalPosition; } }
    public Vector2I JarCellSize { get { return jarTiles.TileSet.TileSize; } }

    // Jar edge positions
    public float JarTopPos { get { return jarTiles.GlobalPosition.Y + JarCellSize.Y * (-JarSize.Y / 2.0f + 0.5f); } }
    public float JarBottomPos { get { return jarTiles.GlobalPosition.Y + JarCellSize.Y * (JarSize.Y / 2.0f - 0.5f); } }
    public float JarLeftPos { get { return jarTiles.GlobalPosition.X + JarCellSize.X * (-JarSize.X / 2.0f + 0.5f); } }
    public float JarRightPos { get { return jarTiles.GlobalPosition.X + JarCellSize.X * (JarSize.X / 2.0f - 0.5f); } }


    // key = y pos
    // value = x pos list
    // Tiles to be destroyed (turned into "popped" tiles)
    private Dictionary<int, List<int>> tilesToDestroy = new Dictionary<int, List<int>>();
    // Destroyed/popped tiles that need to be cleared
    private Dictionary<int, List<int>> destroyedTiles = new Dictionary<int, List<int>>();
    // Tiles to be shifted downwards
    private Dictionary<int, List<int>> tilesToFall = new Dictionary<int, List<int>>();
    public bool DoTilesToFallExist { get { return tilesToFall.Count != 0; } }

    // Tiles that have landed after auto-falling that will be checked if any matches are present at them
    private List<Vector2I> uncheckedLandedTiles = new List<Vector2I>();

    private bool destructionContainsVirus = false;
    private double destroyTimer = 0;
    private double autoFallTimer = 0;
    private int destroyRow;
    // key = pos, value = original virus colour (if painted, value should still be its original colour)
    private Dictionary<Vector2I, int> virusesRemaining = new Dictionary<Vector2I, int>();


    // all the colours of the initial jar tiles (excluding -1 and 0 as they corrispond to non-colour and rainbow)
    private List<int> jarColours = new List<int>();
    public List<int> JarColours { get { return jarColours; } }

    // possible colours pill/power-ups can be
    // these are set to jarColours's colours unless specified in advanced settings
    private List<int> possiblePillColours = new List<int>();
    public List<int> PossiblePillColours { get { return possiblePillColours; } }
    private List<int> possiblePowerUpColours = new List<int>();
    public List<int> PossiblePowerUpColours { get { return possiblePowerUpColours; } }

    // if overrideCustomLevelColours exist, the original colours prior to being replaced are added here
    private List<int> originalJarColours = new List<int>();
    private bool HaveColoursBeenReplaced { get { return originalJarColours.Count > 0; } }


    // power-up tiles which are forced to not fall - used for power-ups place in the level editor, which shouldn't fall unlike obtained power-ups
    private List<Vector2I> frozenPowerUps = new List<Vector2I>();

    private int score = 0;
    public int Score { get { return score; } }
    private int virusLevel = -1;
    public int VirusLevel { get { return virusLevel; } set { virusLevel = value; } }
    public int SpeedLevel { get { return PlayerGameSettings.SpeedLevel; } }

    // viruses killed in one move
    private int virusCombo = 0;
    // lines cleared (columns and/or rows) in one move
    private int lineCombo = 0;
    // times a match is made in one move - this differs from line combos as if multiple lines are cleared at a time, lineCombo will go up two or more times, whereas comboMatches goes up once
    private int matchCombo = 0;

    private bool isPlayerOut = false;
    public bool IsPlayerOut { get { return isPlayerOut; } }

    // List of colours destroyed per line in a single turn/move
    private List<int> lineComboColours = new List<int>();
    // List of colour(s) destroyed on the first match in a single turn/move (before any pills fall to create another match)
    private List<int> initialLineComboColours = new List<int>();

    // List of groups of junk segments that will spawn at the top of the screen on the next turn (if list isn't empty)
    // Each turn will spawn the oldest group of junk segments in this list and remove it from the list
    // NOTE: The int IDs don't represent colour IDs, but rather the index of this player's specific colours (e.g. if player has yellow, green, purple, 0 = yellow, 1 = green, etc)
    private List<List<int>> queuedJunkSegments = new List<List<int>>();
    // List of power-ups that have been matched-up and activated
    private List<BasePowerUp> activePowerUps = new List<BasePowerUp>();

    // Whether or not junk segments are falling down
    private bool isJunkFalling = false;
    // The maximum no. of junk segments a player can send to another in one batch
    private const int maxJunkSegments = 4;

    // The X position for the popped state atlas of each tile type
    private int pillPoppedAtlasX;
    private int virusPoppedAtlasX;
    private int powerUpPoppedAtlasX;
    private Dictionary<Vector2I, JarTileData> customLevelTiles;
    public bool IsInEditorScene { get; set; } = false;
    public bool DisablePowerUpSpawning { get; set; } = false;
    // Set when this jar is played by a training bot - the bot places pills and is told when the game ends instead of GameMan
    public JarBot Bot { get; set; }

    // Jar-specific random number generators, all seeded from the round's shared seed
    // Each has its own stream so that e.g. push-up viruses or power-ups can't change the order of upcoming pills
    private RandomNumberGenerator pillRng = new RandomNumberGenerator();
    public RandomNumberGenerator PillRng { get { return pillRng; } }
    private RandomNumberGenerator virusRng = new RandomNumberGenerator();
    private RandomNumberGenerator powerUpRng = new RandomNumberGenerator();
    public RandomNumberGenerator PowerUpRng { get { return powerUpRng; } }
    private RandomNumberGenerator junkRng = new RandomNumberGenerator();

    // Whether or not the player won or not on their last round
    private bool lastWinState = false;
    public bool LastWinState { get { return lastWinState; } }
    // No. of rounds won against other players in multiplayer
    private int roundWins = 0;
    // Whether or not this player has won enough rounds to be declared the winner of the overall game
    public bool HasWonEnoughRounds { get { return roundWins == CommonGameSettings.MultiplayerRequiredWinCount; } }


    // marathon/endless mode parameters =========================

    // no. of times push-ups (of any line amount) have been done
    private int pushUpCount = 0;
    // push-up countdown, decreases with every throw and resets when a push-up occurs
    private int pushUpCountdown;
    public int PushUpCountdown { get { return pushUpCountdown; } }
	// if all viruses are cleared during marathon/endless mode, this score bonus will be added on to the player's score
    private int PushUpClearBonus { get { return 2000 + (PlayerGameSettings.VirusDifficulty * 250); } }


    // like virusLevel, but specifically for viruses created due to push-ups. determines how many viruses spawn from push-ups
    private int pushUpVirusLevel = 0;
    private int InitialPushUpVirusLevel { get { return 2 + PlayerGameSettings.VirusDifficulty; } }
    private const int pushUpVirusLevelMax = 6;

	// no. of throws before next push up occurs
    private int throwsBeforePushUp;
	// same as above, but scaled to jar width (larger jar = more throws before push-up)
    private int ScaledThrowsBeforePushUp { get { return throwsBeforePushUp * (JarSize.X / baseJarSize.X); } }
    // initial possible value of pushUpRows
    private int InitialThrowsBeforePushUp { get { return 12 - PlayerGameSettings.VirusDifficulty; } }
	// same as above scaled to jar width (larger jar = more throws before push-up)
    private int ScaledInitialThrowsBeforePushUp { get { return InitialThrowsBeforePushUp * (JarSize.X / baseJarSize.X); } }
	// min possible value of pushUpRows (unused)
    //private const int minThrowsBeforePushUp = 4;
	// no. of push ups that have to occur before the no. of throws per push up decreases
    private const int noOfPushUpsForThrowDecrease = 8;

	// no. of rows thst will get pushed up once the push up occurs
    private int pushUpRows = initialPushUpRows;
    public int PushUpRows { get { return pushUpRows; } }
	// initial value of above
    private const int initialPushUpRows = 1;
    // max possible value of pushUpRows
    private const int maxRegularPushUpRows = 2;
    // no. of push ups that have to occur before the no. of rows per push up increases
    private const int noOfPushUpsForRowIncrease = 6;
	// no. of rows to push-up upon clearing all viruses
    private const int clearPushUpRows = 6;

    // no. of push-ups (of any line amount) before pushUpVirusLevel increases
    private const int noOfPushUpsForLvlIncrease = 3;


    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
	{
		throwsBeforePushUp = InitialThrowsBeforePushUp;
        pushUpCountdown = ScaledInitialThrowsBeforePushUp;
        pushUpVirusLevel = InitialPushUpVirusLevel;

        UpdatePoppedAtlasPositions();
		if (CommonGameSettings.PlayerCount == 1)
			uiMan.SetHighScoreLabel(HighScoreList.GetCurrentHighScore());
		SetProcess(false);

		SetVirusTileAnimationState(IsInEditorScene ? false : CommonGameSettings.EnableVirusTileAnimation);

        uiMan.PushUpWarningGroup.SetPushUpRows(pushUpRows);
        uiMan.PushUpWarningGroup.Visible = false;
	}
 
	// Enables/disables the animation of the virus tiles AND animated object tiles
	public void SetVirusTileAnimationState(bool enabled)
	{
		TileSetAtlasSource virusSource = (TileSetAtlasSource)jarTiles.TileSet.GetSource(GameConstants.virusSourceID);
		TileSetAtlasSource objectSource = (TileSetAtlasSource)jarTiles.TileSet.GetSource(GameConstants.objectSourceID);
		
		for (int i = 0; i < GameConstants.noOfColours; i++)
		{
			virusSource.SetTileAnimationFramesCount(Vector2I.Zero + Vector2I.Down * i, enabled ? 2 : 1);
		}

		for (int i = 0; i < GameConstants.noOfObjects; i++)
		{
			Vector2I atlas = Vector2I.Zero + Vector2I.Down * i;

			if (objectSource.GetTileAnimationSpeed(atlas) > 1)
				objectSource.SetTileAnimationFramesCount(atlas, enabled ? 3 : 1);
		}
	}

	// Whether the tile at pos is the same as the target source and atlas (must be an exact match, NOT just the colour)
	public bool DoesTileMatch(Vector2I pos, int targetSourceID, Vector2I targetAtlas)
    {
        return jarTiles.GetCellSourceId(pos) == targetSourceID && jarTiles.GetCellAtlasCoords(pos) == targetAtlas;
    }

    public string ExportLevelToString()
    {
		string sectionDivider = "/";
		string itemDivider = ";";
        string subItemDivider = ",";

		string code = "";

		int lastDifferentSourceID = -2;
		Vector2I lastDifferentAtlas = -Vector2I.One;
		int sameStreak = -1;
		int savedStreaks = 0;

		// Basic settings ==================================================================================================
		// level format version
		code += GameConstants.levelCodeVer + itemDivider;
		code += CommonGameSettings.CustomLevelName + itemDivider;
		code += CommonGameSettings.CustomLevelTheme + itemDivider;
		code += CommonGameSettings.CustomLevelMusic + itemDivider;
		code += JarSize.X + subItemDivider + JarSize.Y + itemDivider;
		code += (CommonGameSettings.CustomLevelMusic == GameConstants.customMusicID ? CommonGameSettings.CustomLevelCustomMusicFile : "") + itemDivider;

        code += (CommonGameSettings.IsCustomLevelUsingCustomBgColour ? "1" : "0") + itemDivider;

		if (CommonGameSettings.IsCustomLevelUsingCustomBgColour)
		{
			code += CommonGameSettings.CustomLevelCustomBgColour.R + subItemDivider;
			code += CommonGameSettings.CustomLevelCustomBgColour.G + subItemDivider;
			code += CommonGameSettings.CustomLevelCustomBgColour.B;
		}

		code += sectionDivider;


        // Jar tile data ==================================================================================================
        for (int y = 0; y < JarSize.Y; y++)
        {
            for (int x = 0; x < JarSize.X; x++)
            {
                Vector2I pos = new Vector2I(x + jarOrigin.X, y + jarOrigin.Y);
                
                int sourceID = jarTiles.GetCellSourceId(pos);
                Vector2I atlas = jarTiles.GetCellAtlasCoords(pos);

				// if matching previous...
				if (DoesTileMatch(pos, lastDifferentSourceID, lastDifferentAtlas))
				{
					sameStreak++;
				}
				// if NOT matching previous...
				else
				{
					if (sameStreak != -1)
					{
						if (savedStreaks != 0)
							code += itemDivider;
						
						code += lastDifferentSourceID + subItemDivider + ((lastDifferentSourceID == -1) ? sameStreak : (lastDifferentAtlas.X + subItemDivider + lastDifferentAtlas.Y + subItemDivider + sameStreak));
						savedStreaks++;
					}

					lastDifferentSourceID = sourceID;
					lastDifferentAtlas = atlas;
					sameStreak = 1;
				}
            }
        }

		if (lastDifferentSourceID != -1)
		{
			if (savedStreaks != 0)
				code += itemDivider;
			
			code += lastDifferentSourceID + subItemDivider + ((lastDifferentSourceID == -1) ? sameStreak : (lastDifferentAtlas.X + subItemDivider + lastDifferentAtlas.Y + subItemDivider + sameStreak));
			savedStreaks++;
		}

		// Player game settings ==================================================================================================
		code += sectionDivider + PlayerGameSettings.ExportToString();

		return code;
    }

	public bool ImportLevelFromString(string code)
    {
        string backUpCode = ExportLevelToString();

        try
        {
			string sectionDivider = "/";
			string itemDivider = ";";
			string subItemDivider = ",";

			// if v0 code detected, use : instead of ; (legacy code compatibility)
            if (code[0] == '0')
                itemDivider = ":";

            string[] codeSections = code.Split(sectionDivider);

			// Basic settings ==================================================================================================
            string[] basicSettingChunks = codeSections[0].Split(itemDivider);

			if (PlayerID == 0)
			{
				CommonGameSettings.CustomLevelName = basicSettingChunks[1];
				CommonGameSettings.CustomLevelTheme = int.Parse(basicSettingChunks[2]);
				CommonGameSettings.CustomLevelMusic = int.Parse(basicSettingChunks[3]);

				if (basicSettingChunks.Length > 5 && CommonGameSettings.CustomLevelMusic == GameConstants.customMusicID)
					CommonGameSettings.CustomLevelCustomMusicFile = basicSettingChunks[5];

                if (basicSettingChunks.Length > 6)
				{
                    bool usingCol = basicSettingChunks[6] == "1";
                    CommonGameSettings.IsCustomLevelUsingCustomBgColour = usingCol;
					
					if (usingCol)
					{
						string[] bgColourData = basicSettingChunks[7].Split(subItemDivider);
						CommonGameSettings.CustomLevelCustomBgColour = new Color(float.Parse(bgColourData[0]), float.Parse(bgColourData[1]), float.Parse(bgColourData[2]));
					}
					else
						CommonGameSettings.CustomLevelCustomBgColour = new Color(1,1,1);

				}
				else
				{
                    CommonGameSettings.CustomLevelCustomBgColour = new Color(1,1,1);
                    CommonGameSettings.IsCustomLevelUsingCustomBgColour = false;
				}

				CommonGameSettings.UpdateBgTintShader();
			}

			string[] jarSizeData = basicSettingChunks[4].Split(subItemDivider);
			PlayerGameSettings.JarWidth = int.Parse(jarSizeData[0]);
			//PlayerGameSettings.JarSize.Y = int.Parse(jarSizeData[1]);
			UpdateJarSize();

			// Jar tile data ==================================================================================================
			jarTiles.Clear();
			foregroundTiles.Clear();

			if (codeSections[1] != "")
			{
            	string[] tileData = codeSections[1].Split(itemDivider);

				int streakSourceID = -2;
				Vector2I streakAtlas = -Vector2I.One;

				int streakNoToBuild = 0;
				int streakTilesRemaining = 0;
			
				bool breakLoop = false;
				for (int y = 0; y < JarSize.Y; y++)
				{
					for (int x = 0; x < JarSize.X; x++)
					{
						Vector2I pos = new Vector2I(x + jarOrigin.X, y + jarOrigin.Y);

						if (streakTilesRemaining == 0)
						{
							if (streakSourceID != -2)
								streakNoToBuild++;

							if (streakNoToBuild == tileData.Length)
							{
								breakLoop = true;
								break;
							}

							string[] individualTileData = tileData[streakNoToBuild].Split(subItemDivider);
							streakSourceID = int.Parse(individualTileData[0]);

							if (streakSourceID == -1)
							{
								streakTilesRemaining = int.Parse(individualTileData[1]);
							}
							else
							{
								streakAtlas.X = int.Parse(individualTileData[1]);
								streakAtlas.Y = int.Parse(individualTileData[2]);
								streakTilesRemaining = int.Parse(individualTileData[3]);
							}
						}

						if (streakSourceID != -1)
							jarTiles.SetCell(pos, streakSourceID, streakAtlas);
						streakTilesRemaining--;
					}

					if (breakLoop)
						break;
				}
			}

            // Player game settings ============================================================================
            PlayerGameSettings.ImportFromString(codeSections[2]);

			// Update all visuals
			GameThemer.UpdateAllVisualsAndSfx();
			
            // successful
            return true;
        }
        catch (Exception e)
        {
            ImportLevelFromString(backUpCode);
            GD.PrintErr("Level code caused an error, level was unchanged: " + e.Message);

            // unsuccessful
            return false;
        }
    }

	private void UpdatePoppedAtlasPositions()
	{
		pillPoppedAtlasX = ((TileSetAtlasSource)jarTiles.TileSet.GetSource(GameConstants.pillSourceID)).GetAtlasGridSize().X - 1;
		virusPoppedAtlasX =((TileSetAtlasSource)jarTiles.TileSet.GetSource(GameConstants.virusSourceID)).GetAtlasGridSize().X - 1;
		powerUpPoppedAtlasX = ((TileSetAtlasSource)jarTiles.TileSet.GetSource(GameConstants.powerUpSourceID)).GetAtlasGridSize().X - 1;
	}

	public bool IsActionJustPressed(string action)
	{
		// training bots press their own buttons
		if (Bot != null)
			return Bot.IsActionJustPressed(action);
		else if (CommonGameSettings.PlayerCount > 1)
		{
			string newAction = PlayerMultiInputSettings.MultiplayerInputPrefix + action;

			return InputMap.HasAction(newAction) ? Input.IsActionJustPressed(newAction) : false;
		}
		else
		{
			return (Input.IsActionJustPressed("KeyboardFull" + action) && !Input.IsActionPressed("ControllerSingle" + action))
			|| (Input.IsActionJustPressed("ControllerSingle" + action) && !Input.IsActionPressed("KeyboardFull" + action));
		}
	}

	public bool IsActionPressed(string action)
	{
		if (Bot != null)
			return Bot.IsActionPressed(action);
		else if (CommonGameSettings.PlayerCount > 1)
		{
			string newAction = PlayerMultiInputSettings.MultiplayerInputPrefix + action;
			return InputMap.HasAction(newAction) ? Input.IsActionPressed(newAction) : false;
		}
		else
		{
			return Input.IsActionPressed("KeyboardFull" + action) || Input.IsActionPressed("ControllerSingle" + action);
		}
	}

	private void Continue()
	{
		if (CommonGameSettings.IsCustomLevel || CommonGameSettings.IsMultiplayer)
			GameMan.ReplayLevel();
		else	
			GameMan.NextLevel();
	}

	private void QuitGame()
	{
		GameMan.QuitGame();
	}

	public void Win()
	{
		if (CommonGameSettings.IsMultiplayer)
		{
			roundWins++;
			uiMan.UpdateWinIconContainer(roundWins);
		}
		else
		{
			// only hide the next pill (aka the one mario is holding) in singleplayer, since he's not there in multiplayer
			pillMan.HideNextPill();
		}

		isPlayerOut = true;
		lastWinState = true;
		
		SetProcess(false);
		pillMan.SetProcess(false);
		
		if (!IsInEditorScene)
		{
			uiMan.ShowWinOverlay();

			if (!CommonGameSettings.IsMultiplayer)
				uiMan.Mario.PlayAnimation("Win");
		}
		

		if (pillMan.IsThrowingPill)
			pillMan.HideThrowingPills();

		previewTiles.Clear();
		
		if (IsInEditorScene)
			GameMan.EndPlayTest();

		GameMan.EndGame();

		DisablePowerUpSpawning = true;
		DeleteAllPowerUps();

		if (VirusRing != null)
        	VirusRing.DefeatAllNonDefeatedViruses();
    }

	public void GameOver()
	{		
		isPlayerOut = true;
		lastWinState = false;

		SetProcess(false);
		pillMan.SetProcess(false);

		// training bots move on to their next game themselves, so skip the overlay and don't end the whole game
		if (Bot != null)
		{
			if (pillMan.IsThrowingPill)
				pillMan.HideThrowingPills();

			previewTiles.Clear();
			Bot.OnGameOver();
			return;
		}

		if (!IsInEditorScene)
		{
			uiMan.ShowGameOverOverlay(CommonGameSettings.PlayerCount > 1);

			if (!CommonGameSettings.IsMultiplayer)
			{
				uiMan.Mario.PlayAnimation("Lose");
				VirusRing.VirusLaugh();
			}
		}

		if (pillMan.IsThrowingPill)
			pillMan.HideThrowingPills();

		previewTiles.Clear();

		if (IsInEditorScene)
			GameMan.EndPlayTest();
		else if (CommonGameSettings.PlayerCount > 1)
			GameMan.IndicatePlayerOut();
		else
		{
			GameMan.EndGame();
			// only hide the next pill (aka the one mario is holding) in singleplayer, since he's not there in multiplayer
			pillMan.HideNextPill();
		}

		DisablePowerUpSpawning = true;
		DeleteAllPowerUps();
	}

	public void ResetState(bool resetScore, bool resetWinCount)
	{
		if (resetWinCount)
		{
			roundWins = 0;
			uiMan.UpdateWinIconContainer(0);
		}

		if (resetScore)
		{
			ResetScore();
		}
		
		uiMan.HideOverlay();

		if (!CommonGameSettings.IsMultiplayer)
			uiMan.Mario.ResetFrame();

		uiMan.SetVirusLabel(0);

		tilesToDestroy.Clear();
		destroyedTiles.Clear();
		tilesToFall.Clear();
		uncheckedLandedTiles.Clear();

		destructionContainsVirus = false;

		destroyTimer = 0;
		autoFallTimer = 0;

        throwsBeforePushUp = InitialThrowsBeforePushUp;
        pushUpCountdown = ScaledInitialThrowsBeforePushUp;
        pushUpVirusLevel = InitialPushUpVirusLevel;

        pushUpCount = 0;
        pushUpRows = initialPushUpRows;
        pushUpCountdown = ScaledInitialThrowsBeforePushUp;

        uiMan.PushUpWarningGroup.Visible = false;

		if (CommonGameSettings.GameMode == 1)
        	uiMan.PushUpWarningGroup.SetPushUpRows(pushUpRows);

        ResetCombo();
		isJunkFalling = false;

		isPlayerOut = false;
		
		jarTiles.Clear();
		foregroundTiles.Clear();

		previewTiles.Clear();

		frozenPowerUps.Clear();

		pillMan.ResetState();

		PowerUpMeter.ResetState();

		DeleteAllPowerUps();
	}

	public void ResetScore()
	{
		score = 0;
		if (CommonGameSettings.PlayerCount == 1)
			uiMan.SetScoreLabel(0);
	}
	public void ResetVirusCount()
	{
		uiMan.SetVirusLabel(0);
	}

	public void DeleteAllPowerUps()
	{
		if (activePowerUps.Count != 0)
		{
			foreach (BasePowerUp powerUp in activePowerUps)
			{
				powerUp.QueueFree();
			}
			activePowerUps.Clear();
		}
	}

	private void ResetCombo()
	{
		virusCombo = 0;
		lineCombo = 0;
		matchCombo = 0;

		lineComboColours.Clear();
		initialLineComboColours.Clear();
	}

	// Godot's RNG gives similar streams for similar seeds (e.g. 1 and 2), so each stream's seed is scrambled from the shared seed first
	private static ulong DeriveSeed(ulong seed, ulong stream)
	{
		// splitmix64
		ulong z = seed + (stream + 1) * 0x9E3779B97F4A7C15;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
		return z ^ (z >> 31);
	}

	public void PrepareLevel(ulong seed)
	{
		pillRng.Seed = DeriveSeed(seed, 0);
		virusRng.Seed = DeriveSeed(seed, 1);
		powerUpRng.Seed = DeriveSeed(seed, 2);
		junkRng.Seed = DeriveSeed(seed, 3);
		DisablePowerUpSpawning = false;
		PowerUpMeter.SetVisibility(PlayerGameSettings.IsUsingPowerUps);
		uiMan.SetSpeedLabel(PlayerGameSettings.SpeedLevel);
		
		if (!CommonGameSettings.IsMultiplayer)
			GameThemer.UpdateMarioSprite();

        pillMan.InitialiseNextPillVariables();

        if (customLevelTiles != null)
			GenerateCustomLevel();
		else
		{
            UpdateJarSize();
            GenerateAllViruses();
		}
	}

	// Scans all current jar tiles and adds them to the custom level tiles dictionary
	public void CurrentTilesToCustomLevelTiles()
	{
		if (customLevelTiles == null)
			customLevelTiles = new Dictionary<Vector2I, JarTileData>();

		customLevelTiles.Clear();

		// Scan all tiles
        for (int y = JarOrigin.Y; y < JarOrigin.Y + JarSize.Y; y++)
        {
            for (int x = JarOrigin.X; x < JarOrigin.X + JarSize.X; x++)
            {
                Vector2I pos = new Vector2I(x, y);

				if (jarTiles.GetCellSourceId(pos) != -1)
				{
					JarTileData tileData = new JarTileData(pos, jarTiles);
					customLevelTiles.Add(pos, tileData);
				}
            }

        }
	}

	public void RestoreCustomLevelTilesForEditor()
	{
		jarTiles.Clear();
		foregroundTiles.Clear();
		foreach (Vector2I pos in customLevelTiles.Keys)
		{
			jarTiles.SetCell(pos, customLevelTiles[pos].sourceID, customLevelTiles[pos].atlas);
		}
	}

	// Scan tiles for colours and update PlayerGameSettings' ChosenColours and ColourCount accordingly
	public void SaveLevelColoursToGameSettings()
	{
		List<int> levelColours = new List<int>();

		for (int y = jarOrigin.Y; y < jarOrigin.Y + JarSize.Y; y++)
		{
			for (int x = jarOrigin.X; x < jarOrigin.X + JarSize.X; x++)
			{
				Vector2I pos = new Vector2I(x, y);

				int colour = GetTileColour(pos);

				if (colour > 0 && !levelColours.Contains(colour))
					levelColours.Add(colour);
			}
		}

		// Fallback colour if level is empty
		if (levelColours.Count == 0)
			levelColours.Add(1);

		PlayerGameSettings.ChosenColours = new Godot.Collections.Array<int>(levelColours);
		PlayerGameSettings.ColourCount = levelColours.Count;
	}

	// Replace jarColours with newColours
	public void ReplaceJarColours(Godot.Collections.Array<int> newColours)
	{
		// Backup original colours into originalJarColours
		if (originalJarColours.Count == 0)
			originalJarColours = new List<int>(jarColours);

		// Indexes in originalJarColours/jarColours to skip since they already exist in newColours
		List<int> segColIndexesToSkip = new List<int>();
		// Colours in newColours that don't need to be used as they're already in originalJarColours
		List<int> newColoursToSkip = new List<int>();
		
		// Fill in segColIndexesToSkip and newColoursToSkip
		foreach (var newCol in newColours)
		{
			if (originalJarColours.Contains(newCol))
			{
				//GD.Print("this col already exists:" + newCol);
				segColIndexesToSkip.Add(originalJarColours.IndexOf(newCol));
				newColoursToSkip.Add(newCol);
			}
		}

		// No. of colours to change (smallest of newColours and jarColours count, minus any colours to be skipped)
		int remainingChanges = Mathf.Min(newColours.Count, jarColours.Count) - newColoursToSkip.Count;

		int segColIndex = 0;
		int newColIndex = 0;

		// Replace colours in jarColours with newColours, skipping jarColours that don't need changed (as exist in newColours) and skipping newColours that don't need to be used (as exist in jarColours)
		while (remainingChanges > 0)
		{
			if (!segColIndexesToSkip.Contains(segColIndex))
			{
				if (newColoursToSkip.Contains(newColours[newColIndex]))
				{
					//GD.Print("" + newColours[newColIndex] + " is being skipped");
					newColIndex++;
					continue;
				}
				else
				{
					//GD.Print("replacing " + jarColours[segColIndex] + " with " + newColours[newColIndex]);
					jarColours[segColIndex] = newColours[newColIndex];
					remainingChanges--;
					newColIndex++;
				}
			}

			segColIndex++;
		}
	}

	// updates pill/power-up colours, either based on jarColours OR chosen pill/power colours if any were set in settings, replacing any if colour overrides exist
	private void UpdatePossiblePillAndPowerUpColours()
	{
		possiblePillColours.Clear();
		possiblePowerUpColours.Clear();

        var UpdateList = (List<int> posCols, List<int> chosenSpecCols) =>
        {
			// if empty, use jar colours
			if (chosenSpecCols.Count() == 0)
			{
                posCols.AddRange(jarColours);
            }
			// else, use chosenSpecCols (only using colours which are also in (original)JarColours)
			else
			{
                List<int> jarCols = HaveColoursBeenReplaced ? originalJarColours : jarColours;

                foreach (int col in chosenSpecCols)
				{
					if (jarCols.Contains(col))
                        posCols.Add(col);
                }

				// if posCols is empty (aka chosenSpecCols doesn't share any colours with jar), just use all of jarColours as a fallback
				if (posCols.Count == 0)
				{
                    GD.Print("no common colours");
                    posCols.AddRange(jarColours);
				}
				// else if Have(Jar)ColoursBeenReplaced, replace posCols colours to match jarcolour's overriden colours
				else if (HaveColoursBeenReplaced)
				{
					for (int i = 0; i < posCols.Count; i++)
					{
						if (jarCols.Contains(posCols[i]))
						{
							int index = jarCols.IndexOf(posCols[i]);
							posCols[i] = jarColours[index];
						}
					}
				}
			}
        };

        UpdateList(possiblePillColours, PlayerGameSettings.ChosenPillSpecificColours);
        UpdateList(possiblePowerUpColours, PlayerGameSettings.ChosenPowerUpSpecificColours);
    }

	public async void GenerateCustomLevel()
	{
		virusesRemaining.Clear();
		jarColours.Clear();
		originalJarColours.Clear();

		List<int> virusColours = new List<int>();
		
		// Find possible colours used in the level
		foreach (JarTileData tile in customLevelTiles.Values)
		{
			int colour = tile.colour;
			
			// if any instance of this colour appears, add to jarColours
			if (colour > 0 && !jarColours.Contains(colour))
			{
				jarColours.Add(colour);
			}

			// if a virus of this colour appears, add to virusColours
			if (tile.sourceID == GameConstants.virusSourceID && !virusColours.Contains(colour))
				virusColours.Add(colour);
		}

		// Fallback possible colour if level is empty
		if (jarColours.Count == 0)
			jarColours.Add(1);

		// Fallback virus colour if virusColours is empty
		if (virusColours.Count == 0)
		{
			if (jarColours.Count == 0)
				virusColours.Add(1);
			else
				virusColours.Add(jarColours[0]);
		}

		jarColours.Sort();
		virusColours.Sort();

		// replace virusColours and jarColours values with overrideCustomLevelColours if present
		if (CommonGameSettings.CurrentThemeHasOverrideCustomLevelColours)
		{
			Godot.Collections.Array<int> newColours = CommonGameSettings.CurrentThemeOverrideCustomLevelColours;

			ReplaceJarColours(newColours);

			for (int i = 0; i < virusColours.Count; i++)
			{
				if (originalJarColours.Contains(virusColours[i]))
					virusColours[i] = jarColours[originalJarColours.IndexOf(virusColours[i])];
			}
		}

        // update pill/power-up colours
        UpdatePossiblePillAndPowerUpColours();

        // randomise next pill colours based of possiblePillColours
        pillMan.RandomiseNextPillColours();
		if (PlayerGameSettings.IsUsingPowerUps)
			PowerUpMeter.QueueNewPowerUp();

		// If official level, use #-# (pack-lvl) number format for level label
		if (CommonGameSettings.IsOfficialCustomLevel)
		{
			int packNo = (CommonGameSettings.CustomLevelID / GameConstants.levelPackLevelCount) + 1;
            int lvlNo = (CommonGameSettings.CustomLevelID % GameConstants.levelPackLevelCount) + 1;
            uiMan.SetLevelLabel(packNo, lvlNo);
		}
		// Else, blank out level label number
		else
			uiMan.SetLevelLabel(-1);

		// Create viruses of each possible colour in the virus ring/magnifying glass
		if (VirusRing != null)
		{
			VirusRing.CreateViruses(virusColours);
		}
		
		// Create tiles based of customLevelTiles data in a random order
		List<Vector2I> remainingTilePositions = new List<Vector2I>(customLevelTiles.Keys);

		while (remainingTilePositions.Count > 0)
		{
			int index = virusRng.RandiRange(0, remainingTilePositions.Count - 1);

			Vector2I pos = remainingTilePositions[index];

			int colour = customLevelTiles[pos].colour;
			Vector2I atlas = customLevelTiles[pos].atlas;

			// if possible colours have been replaced, change tile colour
			if (HaveColoursBeenReplaced && colour > 0)
			{
				int newColour = jarColours[originalJarColours.IndexOf(colour)];
				atlas += Vector2I.Down * (newColour - colour);
				colour = newColour;
			}

			jarTiles.SetCell(pos, customLevelTiles[pos].sourceID, atlas);

			int sourceID = jarTiles.GetCellSourceId(pos);

			if (sourceID == GameConstants.virusSourceID)
			{
				virusesRemaining.Add(pos, colour);
				uiMan.SetVirusLabel(virusesRemaining.Count);
			}
			else if (sourceID == GameConstants.powerUpSourceID)
			{
				frozenPowerUps.Add(pos);
			}
			else if (sourceID == GameConstants.objectSourceID && ShouldTileBeForeground(pos))
			{
				jarTiles.SetCell(pos, -1);
				foregroundTiles.SetCell(pos, customLevelTiles[pos].sourceID, atlas);
			}

			remainingTilePositions.RemoveAt(index);
			
			if (!IsInEditorScene)
				await Task.Delay(1000 / virusGenerationSpeed);
		}

		uiMan.SetVirusLabel(virusesRemaining.Count);
		
		GameMan.IndicateFinishedFillingJar();
	}

	// updates the size of the jar based on the jar size set
	public void UpdateJarSize()
	{
        jarOrigin.X = -JarSize.X / 2;
        uiMan.UpdateJarSpriteSize();
    }

	// chooses a random virus from a list of cell positions/colours, places it, and removes it from the lists
	private void PlaceRandomVirusFromList(List<Vector2I> possibleCells, List<int> virusColours)
	{
		// get random position index and virus colour index
		int posIndex = virusRng.RandiRange(0, possibleCells.Count - 1);
		int virusIndex = virusRng.RandiRange(0, virusColours.Count - 1);

		// get pos and colour using the above indexes
		Vector2I pos = possibleCells[posIndex];
		int virusColour = virusColours[virusIndex];

		// if this pos isn't valid, find another valid pos in this row
		if (!ValidVirusPosition(pos, virusColour))
		{
			bool posValid = false;
			Vector2I potentialPos = pos;

			// search left
			while (potentialPos.X > jarOrigin.X)
			{
				potentialPos.X--;

				// skip if virus is already here
				if (virusesRemaining.ContainsKey(potentialPos))
					continue;

				posValid = ValidVirusPosition(potentialPos, virusColour);
				if (posValid)
					break;
			}

			// search right
			if (!posValid)
			{
				potentialPos = pos;

				while (potentialPos.X < jarOrigin.X + JarSize.X - 1)
				{
					potentialPos.X++;

					// skip if virus is already here
					if (virusesRemaining.ContainsKey(potentialPos))
						return;

					posValid = ValidVirusPosition(potentialPos, virusColour);
					if (posValid)
						break;
				}
			}

			// if position found...
			if (posValid)
			{
				// set pos to potentialPos
				pos = potentialPos;
				// if the pos found was also in possibleCells, remove it from there
				if (possibleCells.Contains(pos))
					possibleCells.Remove(pos);
			}
			// if not found, remove pos from possibleCells and the virus colour
			else
			{
				possibleCells.RemoveAt(posIndex);
				virusColours.RemoveAt(virusIndex);
				return;
			}
		}
		// if pos is valid, remove the pos from possibleCells
		else
			possibleCells.RemoveAt(posIndex);

		// remove the virus colour from virus colours list
		virusColours.RemoveAt(virusIndex);
		
		// place virus in jar tilemap
		jarTiles.SetCell(pos, GameConstants.virusSourceID, Vector2I.Down * (virusColour - 1));

		// add virus to viruses remaining dictonary
		virusesRemaining.Add(pos, virusColour);
	}

	// fills jar of all the viruses needed for the current virusLevel/difficulty
	public async void GenerateAllViruses()
	{
		// if gamemode is classic, set virus level to initial virus level if current virusLevel is at a placeholder value (below zero, otherwise continue from prev level)
		if (CommonGameSettings.GameMode == 0)
		{
			if (virusLevel < 0)
				virusLevel = PlayerGameSettings.InitialVirusLevel;
		}
		// else, set virus level based on the difficulty
		else
			virusLevel = PlayerGameSettings.VirusDifficultyLevel;

		// update virus level label
		if (CommonGameSettings.GameMode == 0)
			uiMan.SetLevelLabel(virusLevel);
		else
		{
            int diff = PlayerGameSettings.VirusDifficulty;

            if (diff == 0)
				uiMan.SetLevelLabel("EASY");
			else if (diff == 1)
				uiMan.SetLevelLabel("NORM");
			else if (diff == 2)
				uiMan.SetLevelLabel("HARD");
			
		}

		// set total virus count based on virus level
		int virusCount = (virusLevel + 1) * 4;

		// adjust virus count based on jar size
		float virusCountModifier = JarSize.X / (float)baseJarSize.X;
        virusCount = Mathf.FloorToInt(virusCount * virusCountModifier);

        List<Vector2I> possibleCells = new List<Vector2I>();
		List<int> virusColours = new List<int>();

		virusesRemaining.Clear();
		jarColours.Clear();

		// set height of the virus-less gap at the top of the jar
		int gap = 6;
		if (virusLevel > 18)
			gap = 3;
		else if (virusLevel > 16)
			gap = 4;
		else if (virusLevel > 14)
			gap = 5;
	
		// possible positions the viruses could spawn
		for (int i = gap; i < JarSize.Y; i++)
		{
			for (int j = 0; j < JarSize.X; j++)
			{
				possibleCells.Add(jarOrigin + new Vector2I(j, i));
			}
		}

        bool isGraduallyAddingColours = true;

		// if gamemode is endless/marathon, add ChosenColours to jarColours immediately
        if (CommonGameSettings.GameMode == 1)
		{
			jarColours = new List<int>(PlayerGameSettings.ChosenColours);
            isGraduallyAddingColours = false;
        }

		// create list of each virus colour to use
		// start with a random colour index
		int firstColourIndex = virusRng.RandiRange(0, PlayerGameSettings.ColourCount - 1);
		// add each colour to virus colours list from ChosenColours for each virus, looping back to first colour once reached end of chosen colour list
		// if isGraduallyAddingColours is true, add colour to jar colours if it hasn't already been added
		for (int i = 0; i < virusCount; i++)
		{
			int colour = PlayerGameSettings.ChosenColours[(firstColourIndex + i) % PlayerGameSettings.ColourCount];
			virusColours.Add(colour);

			if (isGraduallyAddingColours && !jarColours.Contains(colour))
				jarColours.Add(colour);
		}

		// update pill/power-up colours
        UpdatePossiblePillAndPowerUpColours();

		// randomise next pill colours based of jarColours
		pillMan.RandomiseNextPillColours();
		if (PlayerGameSettings.IsUsingPowerUps)
			PowerUpMeter.QueueNewPowerUp();

		// Create viruses of each possible colour in the virus ring/magnifying glass
		if (VirusRing != null)
			VirusRing.CreateViruses(jarColours);

		// for each no. of viruses to spawn...
		for (int i = 0; i < virusCount; i++)
		{
			// break early if no possible cells exist
			if (possibleCells.Count == 0)
				break;

			// place randomly virus from possibleCells/virusColours
            PlaceRandomVirusFromList(possibleCells, virusColours);

			// update virus count label
			uiMan.SetVirusLabel(virusesRemaining.Count);

			// delay spawning next virus (skipped for training bots - this delay is real time, so it can't be sped up)
			if (Bot == null)
				await Task.Delay(1000 / virusGenerationSpeed);
        }

		if (Bot != null)
			Bot.OnJarFilled();
		else
			GameMan.IndicateFinishedFillingJar();
	}

	public void FillsBottomRowsWithViruses(int rows)
	{
        // set total virus count based on pushUpVirusLevel and no. of rows
        int virusCount = pushUpVirusLevel * rows;

        // adjust virus count based on jar size AND no. of rows
        float virusCountModifier = JarSize.X / (float)baseJarSize.X; // jar width
        virusCount = Mathf.FloorToInt(virusCount * virusCountModifier);

        List<Vector2I> possibleCells = new List<Vector2I>();
		List<int> virusColours = new List<int>();

		// fill list of possible positions the viruses could spawn
		// also create list of each virus colour to use, starting with a random colour index
		int firstColourIndex = virusRng.RandiRange(0, PlayerGameSettings.ColourCount - 1);

		for (int i = JarSize.Y - rows; i < JarSize.Y; i++)
		{
			for (int j = 0; j < JarSize.X; j++)
			{
				possibleCells.Add(jarOrigin + new Vector2I(j, i));

                // add each colour to virus colours list from ChosenColours for each virus, looping back to first colour once reached end of chosen colour list
                int colour = PlayerGameSettings.ChosenColours[(firstColourIndex + virusColours.Count) % PlayerGameSettings.ColourCount];
				virusColours.Add(colour);
			}
		}

		// for each virus to make...
		for (int i = 0; i < virusCount; i++)
		{
			// break early if no possible cells exist
			if (possibleCells.Count == 0)
				break;

			// place randomly virus from possibleCells/virusColours
            PlaceRandomVirusFromList(possibleCells, virusColours);
        }

		// update virus count label
		uiMan.SetVirusLabel(virusesRemaining.Count);
	}

	public void StartGame()
	{
		pillMan.ThrowNextPill();
	}

	// whether or not this is a valid positon a virus can generate in
	// if pos is in a matchung row/column of 3 or more viruses, its invalid
	private bool ValidVirusPosition(Vector2I pos, int colour)
	{
		int streakLeft = CheckForStreakFromPos(pos, Vector2I.Left, colour);
		int streakRight = CheckForStreakFromPos(pos, Vector2I.Right, colour);

		int streakUp = CheckForStreakFromPos(pos, Vector2I.Up, colour);
		int streakDown = CheckForStreakFromPos(pos, Vector2I.Down, colour);
		
		return streakLeft + streakRight + 1 < 3 && streakUp + streakDown + 1 < 3;
	}

	private bool IsTileSingle(Vector2I pos)
	{
		return jarTiles.GetCellSourceId(pos) != GameConstants.pillSourceID || jarTiles.GetCellAtlasCoords(pos).X == PillConstants.atlasSingle;
	}

	public bool IsTilePresent(Vector2I pos)
	{
		return jarTiles.GetCellSourceId(pos) != -1;
	}

	public int GetTileColour(Vector2I pos)
	{
		TileData data = jarTiles.GetCellTileData(pos);

		if (data == null)
			return -1;

		int colour = (int)data.GetCustomData("Colour");

		if (jarTiles.GetCellSourceId(pos) != GameConstants.powerUpSourceID && colour == 0)
			return -1;

		return colour;
	}

	public bool DoesTileCauseRebound(Vector2I pos) { return IsObjectWithAttribute(pos, "CauseRebound"); }
	public bool IsTileUnbreakable(Vector2I pos) { return IsObjectWithAttribute(pos, "Unbreakable"); }
	public bool ShouldTileBeForeground(Vector2I pos)  { return IsObjectWithAttribute(pos, "Foreground"); }
	public bool IsTileHazard(Vector2I pos)  { return IsObjectWithAttribute(pos, "Hazard"); }
	public bool DoesTileHaveColour(Vector2I pos) 
	{
		if (GetTileColour(pos) > 0)
            return true;
		else
		{
            int atlasX = jarTiles.GetCellAtlasCoords(pos).X;
            int sourceID = jarTiles.GetCellSourceId(pos);

			if (sourceID == GameConstants.pillSourceID)
				return atlasX == pillPoppedAtlasX;
			else if (sourceID == GameConstants.virusSourceID)
				return atlasX == virusPoppedAtlasX;
			else if (sourceID == GameConstants.powerUpSourceID)
				return atlasX == powerUpPoppedAtlasX;

            return false;
        }
    }
	public int GetTileSourceID(Vector2I pos)  { return jarTiles.GetCellSourceId(pos); }
	public Vector2I GetTileAtlas(Vector2I pos)  { return jarTiles.GetCellAtlasCoords(pos); }
	public bool IsTilePill(Vector2I pos)  { return jarTiles.GetCellSourceId(pos) == GameConstants.pillSourceID; }
	public bool IsTileVirus(Vector2I pos)  { return jarTiles.GetCellSourceId(pos) == GameConstants.virusSourceID; }
	public bool IsTilePowerUp(Vector2I pos)  { return jarTiles.GetCellSourceId(pos) == GameConstants.powerUpSourceID; }
	public bool IsTilePowerUp(Vector2I pos, PowerUp powerUp) 
	{
		if (jarTiles.GetCellSourceId(pos) == GameConstants.powerUpSourceID)
		{
            return jarTiles.GetCellAtlasCoords(pos).X == (int)powerUp;
        }
		else
		{
            return false;
        }
	}

	public bool IsObjectWithAttribute(Vector2I pos, string attribute)
	{
		if (foregroundTiles.GetCellSourceId(pos) == GameConstants.objectSourceID)
		{
			return (bool)foregroundTiles.GetCellTileData(pos).GetCustomData(attribute);
		}
		else if (jarTiles.GetCellSourceId(pos) == GameConstants.objectSourceID)
		{
			return (bool)jarTiles.GetCellTileData(pos).GetCustomData(attribute);
		}
		else
		{
			return false;
		}
	}

	private Vector2I GetConnectedSegment(Vector2I pos)
	{
		return GetConnectedSegment(pos, jarTiles.GetCellSourceId(pos), jarTiles.GetCellAtlasCoords(pos).X);
	}

	private Vector2I GetConnectedSegment(Vector2I pos, int sourceID, int atlasX)
	{
		if (sourceID != GameConstants.pillSourceID)
			return pos;
			
		Vector2I connectedPos = pos;
		
		if (atlasX == PillConstants.atlasSingle)
			return connectedPos;
		else if (atlasX == PillConstants.atlasLeft)
			connectedPos += Vector2I.Right;
		else if (atlasX == PillConstants.atlasRight)
			connectedPos += Vector2I.Left;
		else if (atlasX == PillConstants.atlasTop)
			connectedPos += Vector2I.Down;
		else if (atlasX == PillConstants.atlasBottom)
			connectedPos += Vector2I.Up;
		
		return connectedPos;
	}

	public void AddTileToFall(Vector2I pos)
	{
		if (IsTilePowerUp(pos) && frozenPowerUps.Contains(pos))
            frozenPowerUps.Remove(pos);

        if (!tilesToFall.ContainsKey(pos.Y))
			tilesToFall.Add(pos.Y, new List<int>());

		if (!tilesToFall[pos.Y].Contains(pos.X))
			tilesToFall[pos.Y].Add(pos.X);
	}

	// adds tile to tilesToDestroy - returns true or false depending on whether the position given was already present in tilesToDestroy or not
	// (true = was added, didn't already exist)
	// (false = already exists)
	private bool AddTileToDestroy(Vector2I pos)
	{
		if (!tilesToDestroy.ContainsKey(pos.Y))
			tilesToDestroy.Add(pos.Y, new List<int>());

		if (!tilesToDestroy[pos.Y].Contains(pos.X))
		{
			tilesToDestroy[pos.Y].Add(pos.X);

			if (virusesRemaining.ContainsKey(pos))
				destructionContainsVirus = true;
			
			return true;
		}

		return false;
	}

	// adds tile to destroyedTiles - returns true or false depending on whether the position given was already present in destroyedTiles or not
	// (true = was added, didn't already exist)
	// (false = already exists)
	private bool AddDestroyedTile(Vector2I pos)
	{
		if (!destroyedTiles.ContainsKey(pos.Y))
			destroyedTiles.Add(pos.Y, new List<int>());

		if (!destroyedTiles[pos.Y].Contains(pos.X))
		{
			destroyedTiles[pos.Y].Add(pos.X);

			if (virusesRemaining.ContainsKey(pos))
				destructionContainsVirus = true;
			
			return true;
		}

		return false;
	}

	private int CheckForStreakFromPos(Vector2I origPos, Vector2I dir, int origColour)
	{
		int checkColour = origColour;
		
		// if colour is 0, then origPos is a rainbow tile which can be matched with any colour, so set it to match the colour beside it in this streak check
		if (checkColour == 0)
		{
			int neighbourColour = GetTileColour(origPos + dir);
			if (neighbourColour != -1)
				checkColour = neighbourColour;
		}
		
		int streak = 0;
		Vector2I neighbourPos = origPos;

		while (true)
		{
			neighbourPos += dir;

			int neighbourColour = GetTileColour(neighbourPos);

			// If still checking for rainbow colour and neightbouring isn't rainbow, update checkColour to match it
			if (checkColour == 0 && neighbourColour > 0)
				checkColour = neighbourColour;

			if ((neighbourColour == checkColour || neighbourColour == 0) && (tilesToFall.Count == 0 || !TilesToFallContainsPos(neighbourPos)))
				streak++;
			else
				break;
		}

		return streak;
	}

	private void CheckForLinesToDestroy(Vector2I origPos)
	{
        int origColour = GetTileColour(origPos);
		
		int streakLeft = CheckForStreakFromPos(origPos, Vector2I.Left, origColour);
		int streakRight = CheckForStreakFromPos(origPos, Vector2I.Right, origColour);

		int streakUp = CheckForStreakFromPos(origPos, Vector2I.Up, origColour);
		int streakDown = CheckForStreakFromPos(origPos, Vector2I.Down, origColour);

		// row / horizontal streak
		if (streakLeft + streakRight + 1 >= PlayerGameSettings.MinStreakLength)
		{
			// check if streak contains non-virus tiles
			bool streakContainsMatchableTiles = !IsTileVirus(origPos);

			if (!streakContainsMatchableTiles)
			{
                for (int i = -streakLeft; i <= streakRight; i++)
                {
                    Vector2I pos = origPos + Vector2I.Right * i;

					if (!IsTileVirus(pos))
					{
                        streakContainsMatchableTiles = true;
                        break;
                    }
                }
            }

			// add tiles to be destroyed and increase combo
			if (streakContainsMatchableTiles)
			{
				bool isNewLine = false;

				if (!tilesToDestroy.ContainsKey(origPos.Y))
					tilesToDestroy.Add(origPos.Y, new List<int>());

				for (int i = -streakLeft; i <= streakRight; i++)
				{
					Vector2I pos = origPos + Vector2I.Right * i;

					if (!tilesToDestroy[pos.Y].Contains(pos.X))
					{
						tilesToDestroy[pos.Y].Add(pos.X);
						isNewLine = true;

						if (virusesRemaining.ContainsKey(pos))
							destructionContainsVirus = true;
					}
				}

				if (isNewLine)
					IncrementLineCombo(origColour);
			}

		}

		// column / vertical streak
		if (streakUp + streakDown + 1 >= PlayerGameSettings.MinStreakLength)
		{
			// check if streak contains non-virus tiles
			bool streakContainsMatchableTiles = !IsTileVirus(origPos);

			if (!streakContainsMatchableTiles)
			{
                for (int i = -streakUp; i <= streakDown; i++)
                {
                    Vector2I pos = origPos + Vector2I.Down * i;

					if (!IsTileVirus(pos))
					{
                        streakContainsMatchableTiles = true;
                        break;
                    }
                }
            }

			// add tiles to be destroyed and increase combo
            if (streakContainsMatchableTiles)
            {
                bool isNewLine = false;

                for (int i = -streakUp; i <= streakDown; i++)
                {
                    Vector2I pos = origPos + Vector2I.Down * i;

                    if (AddTileToDestroy(pos))
                        isNewLine = true;
                }

                if (isNewLine)
                    IncrementLineCombo(origColour);
            }
        }
	}

	public void AddPillToTilemap(Pill pill)
	{
        pill.Visible = false;
		destroyTimer = 0;

		Vector2I centrePos = pill.GridPos;

        List<Vector2I> positions;

        // If power-up, only place one tile
        if (pill.IsPowerUp)
		{
			// If rainbow and not on a hazard, auto-activate power-up
			if (pill.CentreSegmentColour == 0 && !IsTileHazard(centrePos))
			{
				sfxMan.Play("Land");
				ActivatePowerUp(pill.CurrentPowerUp, pill.CentreSegmentColour, centrePos);

				SetProcess(true);
				return;
			}

			jarTiles.SetCell(centrePos, GameConstants.powerUpSourceID, pill.CentreTextureCoords);

            positions = new List<Vector2I> { centrePos };
        }
		else
		{
            positions = new List<Vector2I>();
            Dictionary<Vector2I, JarTileData> tilesToAdd = new Dictionary<Vector2I, JarTileData>();

			// only add in-bounds tiles to tilesToAdd
            foreach (Vector2I localPos in pill.RotatedTiles.Keys)
            {
                Vector2I realPos = localPos + pill.GridPos;
                // if position is at or below the top of the jar...
                if (realPos.Y >= jarOrigin.Y)
                {
                    JarTileData tileData = pill.RotatedTiles[localPos];

					// if in the top row of jar AND bottom pill segment, change to single pill segment, since connected segment won't exist
					if (realPos.Y == jarOrigin.Y && tileData.atlas.X == PillConstants.atlasBottom)
					{
                        tileData.atlas.X = PillConstants.atlasSingle;
                    }

                    tilesToAdd.Add(realPos, tileData);
                    positions.Add(realPos);
                }
            }

			// place tiles from tilesToAdd
            foreach (Vector2I pos in tilesToAdd.Keys)
            {
				jarTiles.SetCell(pos, tilesToAdd[pos].sourceID, tilesToAdd[pos].atlas);
            }
		}
		
		tilesToDestroy.Clear();
		destroyedTiles.Clear();
		tilesToFall.Clear();

		// check for hazards and destroy segment(s) if needed
		for (int i = positions.Count - 1; i >= 0; i--)
		{
			if (IsTileHazard(positions[i]))
			{
				SfxMan.Play("VirusStunLand");
				DestroyTile(positions[i], true);
                positions.RemoveAt(i);
            }
		}

		// check for lines
		foreach (Vector2I pos in positions)
		{
			if (jarTiles.GetCellTileData(pos) != null)
				CheckForLinesToDestroy(pos);
		}

		// check for segments which should fall (ignoring ones to destroy)
		foreach (Vector2I pos in positions)
		{
			// continue if cell below is solid OR tile already going to fall/destroy
			if (!IsCellFree(pos + Vector2I.Down) || TilesToFallContainsPos(pos) || TilesToDestroyContainsPos(pos))
                continue;

			// get connected segment pos (same as current pos is single)
            Vector2I connectedPos = GetConnectedSegment(pos);

			// will always fall if its a single
			if (pos == connectedPos)
			{
				AddTileToFall(pos);
			}
			// if its a double horizontal segment, only fall if not grounded on the connected segment and the segment below isn't itself
			else
			{
				Vector2I conDownPos = connectedPos + Vector2I.Down;

				if (conDownPos == pos || IsCellFree(connectedPos + Vector2I.Down))
				{
					AddTileToFall(pos);
					
					if (!TilesToDestroyContainsPos(connectedPos))
						AddTileToFall(connectedPos);
				}
			}
		}

		if (tilesToDestroy.Count == 0 && tilesToFall.Count == 0)
		{
			SfxMan.Play("Land");

			if (queuedJunkSegments.Count > 0)
			{
				CreateJunkSegments();
				SetProcess(true);
			}
			else
			{
                CompleteMove(false);
			}
		}
		else
		{
			if (tilesToDestroy.Count != 0)
			{
				destroyRow = tilesToDestroy.Keys.Max();
				
				if (destructionContainsVirus)
					SfxMan.Play("VirusMatch");
				else
					SfxMan.Play("PillMatch");
				
				IncrementMatchCombo();
			}
			else
				autoFallTimer = 1;

			SetProcess(true);
		}
	}

	// complete the current move and go onto the next one
	private void CompleteMove(bool killedViruses)
	{
		// marathon/endless mode stuff
		if (CommonGameSettings.GameMode == 1)
		{
			ReducePushUpCountdown();

			if (isPlayerOut || tilesToFall.Count != 0)
				return;
		}

		// speed up music if 3 or less viruses remain
		if (virusesRemaining.Count <= 3 && killedViruses && CommonGameSettings.GameMode != 1)
			GameMan.PlayHurryUpJingleIfEnabled();

		if (pillMan.IsPillWaitingToFall)
            pillMan.StartFallingAfterWait();
        else
        	pillMan.ThrowNextPill();
	}

	// ONLY USED FOR marathon/endless mode
	public void ReducePushUpCountdown(bool delayFall = false)
	{
		// if no viruses remain, push up [clearPushUpRows] rows more and award a bonus to the player's score
		if (virusesRemaining.Count == 0)
		{
            DoPushUp(clearPushUpRows, delayFall);
			pushUpCountdown = 0;

            score += PushUpClearBonus;

            uiMan.SetScoreLabel(score);

            sfxMan.Play("ObtainBonus");
        }
		// else, reduce pushUpCountdown. if reached zero, push up [pushUpRows] rows
		else
		{
			pushUpCountdown--;

			// do push up
			if (pushUpCountdown <= 0)
			{
				DoPushUp(pushUpRows, delayFall);
			}
			else if (pushUpCountdown == 1)
			{
				sfxMan.Play("PushUpWarning");
			}
		}

		if (pushUpCountdown <= 0)
		{
			if (pushUpCount % noOfPushUpsForLvlIncrease == 0 && pushUpVirusLevel < pushUpVirusLevelMax)
			{
                pushUpVirusLevel++;
            }
			if (pushUpCount % noOfPushUpsForRowIncrease == 0 && pushUpRows < maxRegularPushUpRows)
			{
				pushUpRows++;
			}
			/*
			if (pushUpCount % noOfPushUpsForThrowDecrease == 0 && throwsBeforePushUp > minThrowsBeforePushUp)
			{
                throwsBeforePushUp--;
            }
			*/

			// set pushUpCountdown to jar-scaled version of throwsBeforePushUp
			pushUpCountdown = ScaledThrowsBeforePushUp;

            // set ui push up rows
            uiMan.PushUpWarningGroup.SetPushUpRows(pushUpRows);
		}

		// set ui push up countdown
        uiMan.PushUpWarningGroup.SetPushUpCountdownLabel(pushUpCountdown);
    }

    // push up every tile by ROWS, and generates new viruses in the new bottom rows
    // if any tiles go above the top of the jar, its joever
    private void DoPushUp(int rows, bool delayFall = false)
    {
        bool pushedOutOfJar = false;
        pushUpCount++;

        // Push up existing tiles by ROWS
        for (int y = 0; y < JarSize.Y; y++)
        {
            for (int x = 0; x < JarSize.X; x++)
            {
                Vector2I oldPos = new Vector2I(x + jarOrigin.X, y + jarOrigin.Y);
				
                if (!IsTilePresent(oldPos))
                    continue;

                Vector2I newPos = oldPos + Vector2I.Up * rows;

				// if new pos would be out the top of the jar, set pushedOutOfJar to true to trigger a game over
                if (newPos.Y < jarOrigin.Y)
                    pushedOutOfJar = true;

				// create copy of tile at new pos and erase old one
				int sourceID = jarTiles.GetCellSourceId(oldPos);
				Vector2I atlas = jarTiles.GetCellAtlasCoords(oldPos);
				
				jarTiles.SetCell(newPos, sourceID, atlas);
				jarTiles.EraseCell(oldPos);

				// update lists storing tile positions
				if (virusesRemaining.ContainsKey(oldPos))
				{
                    virusesRemaining.Add(newPos, virusesRemaining[oldPos]);
                    virusesRemaining.Remove(oldPos);
                }
				if (frozenPowerUps.Contains(oldPos))
				{
                    frozenPowerUps.Add(newPos);
                    frozenPowerUps.Remove(oldPos);
                }
            }
        }

        // to-do: generate new row(s) containing viruses
        // ENSURE VIRUSES SPAWNED DON'T MATCH MORE THAN 2 OF THE SAME VIRUSES IN A ROW *OR* HAVE THE SAME COLOUR AS THE PILL/POWE-UP ABOVE
        // IF THE RULES ABOVE CAUSE NO VIRUSES TO SPAWN, FORCE MATCHING VIRUSES TO APPEAR AND DO MATCH CHECKS TO DESTROY (only if a pill/power-up is above)
        FillsBottomRowsWithViruses(rows);

        // if pushedOutOfJar is true (aka tiles have gone out the top of the jar), do a game over
        if (pushedOutOfJar)
		{
			GameOver();
            return;
        }

        // to-do: fall checks for old bottom row
        int oldBottomY = JarSize.Y - 1 - rows;
        for (int x = 0; x < JarSize.X; x++)
        {
			Vector2I pos = new Vector2I(x + jarOrigin.X, oldBottomY + jarOrigin.Y);

			// if NO tile at pos OR tile below pos exists, continue 
			if (!IsTilePresent(pos) || IsTilePresent(pos + Vector2I.Down))
            	continue;
	
            RecursiveFall(pos);
        }

		if (tilesToFall.Count != 0)
		{
            destroyTimer = 0;
			autoFallTimer = 1;

			if (!delayFall)
				SetProcess(true);
        }
    }

    private bool TilesToDestroyContainsPos(Vector2I pos)
	{
		if (tilesToDestroy.ContainsKey(pos.Y) && tilesToDestroy[pos.Y].Contains(pos.X))
			return true;
		else
			return false;
	}

	private bool DestroyedTilesContainsPos(Vector2I pos)
	{
		if (destroyedTiles.ContainsKey(pos.Y) && destroyedTiles[pos.Y].Contains(pos.X))
			return true;
		else
			return false;
	}

	private bool TilesToFallContainsPos(Vector2I pos)
	{
		return tilesToFall.ContainsKey(pos.Y) && tilesToFall[pos.Y].Contains(pos.X);
	}

	// whether the position given is empty, or will be updated (e.g. will fall, be destroyed or is already destroyed/popped) 
	public bool IsPosEmptyOrWillUpdate(Vector2I pos)
	{
		return IsCellFree(pos) || TilesToDestroyContainsPos(pos) || DestroyedTilesContainsPos(pos) || TilesToFallContainsPos(pos);
	}
	
	// recursive function to make the segement at pos fall, as well as any connecting segments (aka is double) and any on top of the segment(s)
	// doesn't affect viruses
	private void RecursiveFall(Vector2I pos)
	{
		int sourceID = jarTiles.GetCellSourceId(pos);

		// if tile is not nothing, not updating (falling, destroyed, to-be-destroyed) and either a pill segment or non-frozen power-up, check if it can fall and make it fall is so
		if (!IsPosEmptyOrWillUpdate(pos) && (sourceID == GameConstants.pillSourceID || (sourceID == GameConstants.powerUpSourceID && !frozenPowerUps.Contains(pos))))
		{
			Vector2I connectedPos = GetConnectedSegment(pos);

			// will always fall if its a single or vertical segment
			if (IsTileSingle(pos) || pos.X == connectedPos.X)
			{
				AddTileToFall(pos);
				RecursiveFall(pos + Vector2I.Up);
			}
			// if its a double horizontal segment, only fall if not grounded on the connected segment
			else
			{
				Vector2I conDownPos = connectedPos + Vector2I.Down;

				if (pos.X != connectedPos.X && IsPosEmptyOrWillUpdate(conDownPos))
				{
					AddTileToFall(pos);
					RecursiveFall(pos + Vector2I.Up);
					AddTileToFall(connectedPos);
					RecursiveFall(connectedPos + Vector2I.Up);
				}
			}
		}
	}

	private void IncrementVirusCombo()
	{
		if (!isJunkFalling)
			virusCombo++;
		score += Math.Clamp(virusCombo, 1, 6) * (PlayerGameSettings.SpeedLevel + 1) * 100;
	}
	private void IncrementMatchCombo()
	{
		if (isJunkFalling)
			return;

		matchCombo++;

		if (PlayerGameSettings.IsUsingPowerUps)
			PowerUpMeter.IncrementLevel(2);
	}

	private void IncrementLineCombo(int colour)
	{
		if (isJunkFalling)
			return;
			
		lineCombo++;

		if (matchCombo == 0)
			initialLineComboColours.Add(colour);

		lineComboColours.Add(colour);
	}

	private void CompleteAutoFallingAndDestruction()
	{
        bool killedViruses = virusCombo != 0;

        if (lineCombo > 3)
			SfxMan.Play("ChainX3");
		else if (lineCombo > 2)
			SfxMan.Play("ChainX3");
		else if (lineCombo > 1)
			SfxMan.Play("ChainX2");

        if (CommonGameSettings.IsMultiplayer && CommonGameSettings.MultiplayerUseJunkPills && lineCombo > 1 && Bot == null)
		{
			// Get random player which isn't this player
			int receivingPlayer = junkRng.RandiRange(0, CommonGameSettings.PlayerCount - 2);

			if (receivingPlayer == PlayerID)
			{
				receivingPlayer = (receivingPlayer + 1) % CommonGameSettings.PlayerCount;
			}

			// convert combo colours to colour indexes (since colours could vary between players)

			List<int> junkColourIndexes = new List<int>();
			
			for (int i = 0; i < Mathf.Min(lineComboColours.Count, maxJunkSegments); i++)
			{
				int colour = lineComboColours[i];
				junkColourIndexes.Add(jarColours.IndexOf(colour));
			}

			// Send junk colours to receiving player
			GameMan.SendJunkSegments(receivingPlayer, junkColourIndexes);
		}

		autoFallTimer = 0;

        ResetCombo();

		if (queuedJunkSegments.Count > 0)
		{
			isJunkFalling = true;
			CreateJunkSegments();
		}
		else
		{
			isJunkFalling = false;
			SetProcess(false);
            CompleteMove(killedViruses);
		}
	}

	private void ActivatePowerUp(PowerUp pwr, int colour, Vector2I pos)
	{
        if (DisablePowerUpSpawning)
			return;
		
		BasePowerUp powerUp = powerUpPrefabs.GetPowerUpPrefab(pwr).Instantiate<BasePowerUp>();

		powerUp.InitialGridPos = pos;
		powerUp.Colour = colour;
		powerUp.JarMan = this;
		powerUp.SfxMan = SfxMan;
		powerUp.Texture = ((TileSetAtlasSource)jarTiles.TileSet.GetSource(GameConstants.powerUpSourceID)).Texture;

		if (pwr == PowerUp.PaintV || pwr == PowerUp.PaintH)
		{
            PowerUpPaint paint = powerUp as PowerUpPaint;
			paint.PillTexture = ((TileSetAtlasSource)jarTiles.TileSet.GetSource(GameConstants.pillSourceID)).Texture;
        }

		jarTiles.GetParent().AddChild(powerUp);
		powerUp.GetParent().MoveChild(powerUp, previewTiles.GetIndex());

        powerUp.GlobalPosition = TilemapGlobalPos + pos * JarCellSize;
		powerUp.GlobalPosition += JarCellSize / 2;

		activePowerUps.Add(powerUp);
	}

	public void SetActivePowerUpsPauseState(bool pause)
	{
		foreach (BasePowerUp powerUp in activePowerUps)
		{
			powerUp.ProcessMode = pause ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
			powerUp.Visible = !pause;
		}
	}

	public void SetVirusRingPauseState(bool pause)
	{
		if (VirusRing != null)
			VirusRing.SetPauseState(pause);
	}

	public void FinishPowerUp(BasePowerUp pwr)
	{
		activePowerUps.Remove(pwr);
	}

	private void CreateJunkSegments()
	{
		isJunkFalling = true;

		autoFallTimer = 0;

		// Get possible spawn locations for junk segments
		List<int> possiblePositions = new List<int>();

		for (int i = 0; i < JarSize.X; i++)
		{
			Vector2I pos = jarOrigin + Vector2I.Right * i;

			if (!IsTilePresent(pos))
				possiblePositions.Add(pos.X);

		}

		// Get list of segments to create
		List<int> segmentsToCreate = queuedJunkSegments[0];

		// Set the amount of segments to create to the smallest between the segmentsToCreate and possiblePositions counts
		int createCount = Mathf.Min(segmentsToCreate.Count, possiblePositions.Count);

		// Create junk segments using randomised positions in possiblePositions 
		for (int i = 0; i < createCount; i++)
		{
			// Get position and remove from segmentsToCreate/queuedJunkSegments
			int posIndex = junkRng.RandiRange(0, possiblePositions.Count - 1);
			Vector2I pos = new Vector2I(possiblePositions[posIndex], jarOrigin.Y);

			possiblePositions.Remove(pos.X);

			// Get index of colour (not the colour itself as colours could vary between players)
			int colourIndex = segmentsToCreate[i];

			// if the colour index is out of range of this player's possiblePillColours, fix it to it is
			if (colourIndex > possiblePillColours.Count - 1)
			{
				colourIndex = colourIndex % possiblePillColours.Count;
			}

			// Set cell at pos to single pill segment of colour based on colourIndex
			jarTiles.SetCell(pos, GameConstants.pillSourceID, new Vector2I(PillConstants.atlasSingle, possiblePillColours[colourIndex] - 1));

			// Add segment to fall list
			AddTileToFall(pos);
		}

		// Remove group of junk segments just created from queuedJunkSegments
		queuedJunkSegments.RemoveAt(0);
	}

	// changes colour of tile
	public bool PaintTile(Vector2I pos, int colour)
	{
        // can only paint if tile exists and has an assigned colour
        if (IsTilePresent(pos) && GetTileColour(pos) > 0)
		{
			// get source id + atlas
            int sourceID = GetTileSourceID(pos);
            Vector2I oldAtlas = GetTileAtlas(pos);
            Vector2I newAtlas = oldAtlas;

            // set atlas to match colour
            newAtlas.Y = colour;
			if (sourceID != GameConstants.powerUpSourceID)
                newAtlas.Y -= 1;

			// if atlas hasn't changed, must already be the given colour so return false
			if (oldAtlas == newAtlas)
                return false;

            // set cell
            jarTiles.SetCell(pos, sourceID, newAtlas);

            return true;
        }
		else
            return false;
    }

	// Destroys tile at given position - returns true if pos is not empty, aka something gets destroyed
	public bool DestroyTile(Vector2I pos, bool forceInsta = false, bool dontActivatePowerUp = false)
	{
		// Add pos to destroyedTiles (if not forceInsta) - return if pos is already in destroyedTiles or pos is empty
		if (jarTiles.GetCellSourceId(pos) == -1 || (forceInsta ? false : !AddDestroyedTile(pos)))
			return false;

		Vector2I atlas = jarTiles.GetCellAtlasCoords(pos);
		int sourceID = jarTiles.GetCellSourceId(pos);
		int colour = GetTileColour(pos);
		Vector2I poppedAtlas;

		// If tile falling, remove it from fall list
		if (TilesToFallContainsPos(pos))
		{
			tilesToFall[pos.Y].Remove(pos.X);
		}

		// if forceInsta, just delete cell right away if object, don't bother with "popping" the tile
		if (forceInsta)
		{
			jarTiles.SetCell(pos, -1);

			// if this a connect pill segment, change the connected segment to a non-connected one
			if (sourceID == GameConstants.pillSourceID)
			{
				SeparateConnectedSegment(pos, sourceID, atlas);
			}
			
			return true;
		}
		else if (sourceID == GameConstants.pillSourceID)
			poppedAtlas.X = pillPoppedAtlasX;
		else if (sourceID == GameConstants.virusSourceID)
			poppedAtlas.X = virusPoppedAtlasX;
		else if (sourceID == GameConstants.powerUpSourceID)
			poppedAtlas.X = powerUpPoppedAtlasX;
		// if other sourceID, just delete cell right away if object, don't bother with "popping" the tile
		else
		{
			jarTiles.SetCell(pos, -1);
			return true;
		}
			
		poppedAtlas.Y = sourceID == GameConstants.powerUpSourceID ? colour : colour - 1;

		if (sourceID == GameConstants.virusSourceID)
		{
			RemoveRemainingVirus(pos, true);
		}
		else if (sourceID == GameConstants.powerUpSourceID)
		{
			if (frozenPowerUps.Contains(pos))
				frozenPowerUps.Remove(pos);
			
			if (!dontActivatePowerUp)
				ActivatePowerUp((PowerUp)atlas.X, colour, pos);
		}
		
		jarTiles.SetCell(pos, sourceID, poppedAtlas);

		// if this a connect pill segment, change the connected segment to a non-connected one
		if (sourceID == GameConstants.pillSourceID)
			SeparateConnectedSegment(pos, sourceID, atlas);

		return true;
	}

    private void RemoveRemainingVirus(Vector2I pos, bool updateHud)
    {
		int colour = virusesRemaining[pos];

		virusesRemaining.Remove(pos);

		if (updateHud)
			uiMan.SetVirusLabel(virusesRemaining.Count);

		if (VirusRing != null)
		{
            VirusRing.StunVirus(colour, !virusesRemaining.ContainsValue(colour) && CommonGameSettings.GameMode != 1);
		}

		IncrementVirusCombo();
    }

    public void SplitPillInTwo(Vector2I pos)
	{
        Vector2I atlas = GetTileAtlas(pos);

		jarTiles.SetCell(pos, GameConstants.pillSourceID, new Vector2I(PillConstants.atlasSingle, atlas.Y));
        SeparateConnectedSegment(pos, GameConstants.pillSourceID, atlas);
	}

	private void SeparateConnectedSegment(Vector2I pos, int sourceID, Vector2I atlas)
	{
		Vector2I connectedPos = GetConnectedSegment(pos, sourceID, atlas.X);

		if (connectedPos != pos)
		{
			jarTiles.SetCell(connectedPos, GameConstants.pillSourceID, new Vector2I(PillConstants.atlasSingle, GetTileColour(connectedPos) - 1));
			
			// if pos below is empty or will update, do RecursiveFall for the connected pos
			if (IsPosEmptyOrWillUpdate(connectedPos + Vector2I.Down))
			{
				RecursiveFall(connectedPos);
			}
		}
	}

	public void DestroyAllPillSegments(int colour)
	{
		bool destroyAll = colour == 0;

		for (int y = jarOrigin.Y; y < jarOrigin.Y + JarSize.Y; y++)
		{
			for (int x = jarOrigin.X; x < jarOrigin.X + JarSize.X; x++)
			{
				Vector2I pos = new Vector2I(x, y);

				if (jarTiles.GetCellSourceId(pos) == GameConstants.pillSourceID && (destroyAll || GetTileColour(pos) == colour))
				{
					DestroyTile(pos);
				}
			}
		}
	}

	public void DestroyAllViruses(int colour)
	{
		// If rainbow, destroy half of the total viruses
		if (colour == 0)
		{
			List<Vector2I> viruses = new List<Vector2I>(virusesRemaining.Keys);

			if (viruses.Count == 0)
				return;

			int halfCount = viruses.Count - viruses.Count / 2;

			if (halfCount == 0)
				halfCount = viruses.Count;

			for (int i = 0; i < halfCount; i++)
			{
				DestroyTile(viruses[i]);
			}
		}
		// Else, destroy all viruses of given colour
		else
		{
			List<Vector2I> viruses = new List<Vector2I>(virusesRemaining.Keys);

			foreach (Vector2I pos in viruses)
			{
				if (GetTileColour(pos) == colour)
				{
					DestroyTile(pos);
				}
			}
		}
	}

	private void UpdateDestruction(double delta)
	{
		// if there were no tiles to be destroyed or they have all been destroyed, remove all the "popped" tiles
		if (tilesToDestroy.Count == 0 || destroyRow < tilesToDestroy.Keys.Min())
		{
			if (activePowerUps.Count != 0 && (virusesRemaining.Count != 0 || CommonGameSettings.GameMode == 1))
			{
				if (destroyTimer > 0)
					destroyTimer -= delta * destroyDisappearSpeed;
				return;
			}
			
			// reset destructionContainsVirus to default/false in prep for next destruction
			destructionContainsVirus = false;

			// Only update score label if in single player
			if (CommonGameSettings.PlayerCount == 1)
			{
				uiMan.SetScoreLabel(score);
				if (score > HighScoreList.GetCurrentHighScore())
					uiMan.SetHighScoreLabel(score);
			}

			if (virusesRemaining.Count() == 0 && virusCombo > 0 && CommonGameSettings.GameMode != 1)
			{
				Win();
				return;
			}

			destroyTimer -= delta * destroyDisappearSpeed;

			if (destroyTimer <= 0 && activePowerUps.Count == 0)
			{
				int minY = destroyedTiles.Keys.Min();
				int maxY = destroyedTiles.Keys.Max();

				for (int y = maxY; y >= minY; y--)
				{
					if (!destroyedTiles.ContainsKey(y))
						continue;

					for (int i = 0; i < destroyedTiles[y].Count; i++)
					{
						Vector2I pos = new Vector2I(destroyedTiles[y][i], y);
						jarTiles.SetCell(pos, -1);
						
						// if pos is in tilesToFall, remove it from there - otherwise an empty tile will fall
						if (TilesToFallContainsPos(pos))
						{
							tilesToFall[pos.Y].Remove(pos.X);

							if (tilesToFall[pos.Y].Count == 0)
								tilesToFall.Remove(pos.Y);
						}

						Vector2I abovePos = pos + Vector2I.Up;
						
						// do RecursiveFall for the position above if isn't destroyed
						if (!DestroyedTilesContainsPos(abovePos))
							RecursiveFall(abovePos);
					}
				}
				tilesToDestroy.Clear();
				destroyedTiles.Clear();
				autoFallTimer = 0;
				
				if (tilesToFall.Count == 0)
					CompleteAutoFallingAndDestruction();
			}
			return;
		}
		// wait if destroy timer hasn't finished
		if (destroyTimer > 0)
		{
			destroyTimer -= delta * destroyRowSpeed;
			return;
		}
		// destroy row specified by destroyRow
		for (int i = 0; i < tilesToDestroy[destroyRow].Count; i++)
		{
			Vector2I pos = new Vector2I(tilesToDestroy[destroyRow][i], destroyRow);
			DestroyTile(pos);
		}
		destroyRow--;
		if (!tilesToDestroy.ContainsKey(destroyRow) && destroyRow >= tilesToDestroy.Keys.Min())
		{
			do
			{
				destroyRow--;
			} while (!tilesToDestroy.ContainsKey(destroyRow)); 
		}
		destroyTimer = 1;
	}

	private bool IsCellFullAndNotFalling(Vector2I pos)
	{
		return !IsCellFree(pos) && !TilesToFallContainsPos(pos);
	}

	private void UpdateTilesToFall(double delta)
	{
		// wait if autoFallTimer hasn't finished
		if (autoFallTimer > 0)
		{
			autoFallTimer -= delta * ((IsActionPressed("SoftDrop") || !CommonGameSettings.ManualAutoFallSpeedUp) ? pillMan.SoftDropSpeed : PlayerGameSettings.AutoFallSpeed);
			return;
		}

		int minY = tilesToFall.Keys.Min();
		int maxY = tilesToFall.Keys.Max();
		
		// tiles to get insta destroyed, any tiles touching a hazard get put here
		List<Vector2I> tilesToInstaDestroy = new List<Vector2I>();

		// find landed tiles AND tiles about to hit hazard (aka to be insta destroyed)
		for (int y = maxY; y >= minY; y--)
		{
			if (!tilesToFall.ContainsKey(y))
				continue;
			
			for (int i = tilesToFall[y].Count - 1; i >= 0; i--)
			{
				Vector2I pos = new Vector2I(tilesToFall[y][i], y);
				Vector2I connectedPos = GetConnectedSegment(pos);
				bool landed = false;
				
				if (connectedPos == pos + Vector2I.Left || connectedPos == pos + Vector2I.Right)
				{
					if (IsCellFullAndNotFalling(pos + Vector2I.Down) || IsCellFullAndNotFalling(connectedPos + Vector2I.Down))
					{
						tilesToFall[y].RemoveAt(i);
						uncheckedLandedTiles.Add(pos);
						landed = true;
					}
				}
				else if (IsCellFullAndNotFalling(pos + Vector2I.Down))
				{
					tilesToFall[y].RemoveAt(i);
					uncheckedLandedTiles.Add(pos);
					landed = true;
				}

				// if tile would still fall but would hit a hazard, insta-destroy it
				if (!landed)
				{
					// insta-destroy tile if hazard
					if (IsTileHazard(pos + Vector2I.Down))
					{
                        tilesToInstaDestroy.Add(pos);
					}
				}
			}

			if (tilesToFall[y].Count == 0)
				tilesToFall.Remove(y);
		}

		// insta destroy any tiles hit by hazard
		if (tilesToInstaDestroy.Count > 0)
		{
			SfxMan.Play("VirusStunLand");
			for (int i = 0; i < tilesToInstaDestroy.Count; i++)
			{
                Vector2I pos = tilesToInstaDestroy[i];

                if (IsTileVirus(pos))
				{
					SfxMan.Play("VirusMatch");
                    RemoveRemainingVirus(pos, true);
				}

				DestroyTile(pos, true);
			}

			if (virusCombo > 0)
			{
				uiMan.SetVirusLabel(virusesRemaining.Count);

				if (virusesRemaining.Count() == 0 && CommonGameSettings.GameMode != 1)
				{
					Win();
					return;
				}
			}
		}


		// do line checks for landed tiles
		if (uncheckedLandedTiles.Count > 0 && PlayerGameSettings.ImpatientMatching || tilesToFall.Count == 0)
		{
			tilesToDestroy.Clear();
			destroyedTiles.Clear();

			foreach (Vector2I pos in uncheckedLandedTiles)
			{
				if (jarTiles.GetCellTileData(pos) != null)
					CheckForLinesToDestroy(pos);
			}

			uncheckedLandedTiles.Clear();

			if (tilesToDestroy.Count > 0)
			{
				autoFallTimer = 0;
				destroyTimer = 0;
				destroyRow = tilesToDestroy.Keys.Max();

				if (destructionContainsVirus)
					SfxMan.Play("VirusMatch");
				else
					SfxMan.Play("PillMatch");
				
				IncrementMatchCombo();

				return;
			}
			else if (tilesToFall.Count == 0)
			{
				CompleteAutoFallingAndDestruction();
				return;
			}

		}

		// do CompleteAutoFallingAndDestruction if no falling/popped tiles present
		if (tilesToDestroy.Count == 0 && tilesToFall.Count == 0)
		{
			CompleteAutoFallingAndDestruction();
			return;
		}

		if (IsActionPressed("SoftDrop") && CommonGameSettings.ManualAutoFallSpeedUp)
			SfxMan.Play("FastFall");
		else
			SfxMan.Play("Fall");

		// move mid-air tiles down
		for (int y = maxY; y >= minY; y--)
		{
			if (!tilesToFall.ContainsKey(y))
				continue;

			for (int i = tilesToFall[y].Count - 1; i >= 0; i--)
			{
				Vector2I pos = new Vector2I(tilesToFall[y][i], y);
				Vector2I atlas = jarTiles.GetCellAtlasCoords(pos);
				int sourceID = jarTiles.GetCellSourceId(pos);

				if (sourceID == GameConstants.virusSourceID)
				{
                    int virusCol = virusesRemaining[pos];
                    virusesRemaining.Remove(pos);
                    virusesRemaining.Add(pos + Vector2I.Down, virusCol);
                }

				jarTiles.SetCell(pos, -1);
				jarTiles.SetCell(pos + Vector2I.Down, sourceID, atlas);
			}
		}

		// shift dictonary y positions
		for (int y = maxY; y >= minY; y--)
		{
			if (!tilesToFall.ContainsKey(y))
				continue;

			tilesToFall.Add(y + 1, tilesToFall[y]);
			tilesToFall.Remove(y);
		}
		
		autoFallTimer = 1;
	}

	public void ClearPreviewTiles()
	{
		previewTiles.Clear();
	}

	public void CreateGhostPill(Pill pill, Vector2I centrePos)
	{
		previewTiles.Clear();

		if (pill.IsPowerUp)
		{
			previewTiles.SetCell(centrePos, GameConstants.powerUpSourceID, pill.CentreTextureCoords);
		}
		else
		{
			// place tiles from tilesToAdd
            foreach (Vector2I pos in pill.RotatedTiles.Keys)
            {
				previewTiles.SetCell(pos + centrePos, pill.RotatedTiles[pos].sourceID, pill.RotatedTiles[pos].atlas);
            }
		}
	}

	// Adds the list of junk segment colour indexes to queuedJunkSegments 
	public void AddJunkSegmentsToQueue(List<int> colourIndexes)
	{
		queuedJunkSegments.Add(colourIndexes);
	}

	public override void _Process(double delta)
	{
		if (tilesToDestroy.Count > 0 || destroyedTiles.Count > 0)
			UpdateDestruction(delta);
		else if (tilesToFall.Count > 0)
			UpdateTilesToFall(delta);
		else if (activePowerUps.Count == 0)
			CompleteAutoFallingAndDestruction();
	}

	// Returns whether a cell is empty AND is not out-of-bounds
	public bool IsCellFree(Vector2I pos)
	{
		bool validX = pos.X - jarOrigin.X == Mathf.Clamp(pos.X - jarOrigin.X, 0, JarSize.X - 1);
		bool validY = pos.Y - jarOrigin.Y < JarSize.Y;
		return validX && validY && jarTiles.GetCellTileData(pos) == null;
	}

	public void SetTileVisibity(bool b)
	{
		jarTiles.Visible = b;
		previewTiles.Visible = b;
	}
}
