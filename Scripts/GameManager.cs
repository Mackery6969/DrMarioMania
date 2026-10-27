using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class GameManager : Node
{
    // manages the entire game, not individual jar

    // the distance between each jar in multiplayer (EXTRA DISTANCE WILL BE ADDED IF A JAR HAS A NON-DEFAULT WIDTH)
    [Export] private int jarSpacing;

    [ExportGroup("Audio References")]
    [Export] private MusicManager musicMan;
    [Export] private SfxManager sfxMan;

    [ExportGroup("Optional References")]
    [Export] private EditorManager editorMan;

    [ExportGroup("Other References")]
    [Export] private PackedScene jarGroupSinglePrefab;
    [Export] private PackedScene jarGroupMultiPrefab;
    [Export] private GameThemer gameThemer;
    [Export] private CanvasLayer gameLayer;
    [Export] private TouchControlsManager touchControlsMan;
    [Export] private ScreenTransition screenTransition;
    [Export] private PauseManager pauseMan;

    [ExportGroup("Resources")]
    [Export] private CommonGameSettings commonGameSettings;
    [Export] private UserCustomLevelList userCustomLevelList;
    [Export] private OfficialCustomLevelList officialCustomLevelList;
    [Export] private HighScoreList highScoreList;

    private List<JarManager> jars = new List<JarManager>();
    public List<JarManager> Jars { get { return jars; } }
    private bool isPaused = false;
    private bool isGameOngoing = false;
    private int jarsLeftToFill;
    private int remainingPlayers;
    private bool IsInEditorScene { get { return editorMan != null; } }
    private ulong sharedSeed;
    // Whether or not a player has enough round wins to win the whole game (reached best of # rounds)
    private bool hasPlayerWonEnoughRounds = false;
    // Delay in the game starting after all jars are filled in milliseconds
    private const int startDelay = 750;

    private bool failedToLoadCustomLevel = false;

    // Whether or not the score saved since last game
    private bool hasSavedScore = false;

    // Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
        if (!commonGameSettings.HasLoadedSettings)
		{
			commonGameSettings.LoadFromFile();
			userCustomLevelList.LoadFromFile();
            highScoreList.LoadFromFile();
		}

        if (GetTree().CurrentScene.Name == "EditorScene")
        {
            if (commonGameSettings.CustomLevelID == -2)
                commonGameSettings.CustomLevelID = -1;
        }
        
        touchControlsMan.ShowTouchControlsIfAvailable(!IsInEditorScene);

        // if creating new level, reset settings
        if (commonGameSettings.CustomLevelID == -1)
        {
            commonGameSettings.CustomLevelMusic = -1;
            commonGameSettings.CustomLevelTheme = 0;

            commonGameSettings.CustomLevelCustomBgColour = new Color(1,1,1);
            commonGameSettings.IsCustomLevelUsingCustomBgColour = false;
            commonGameSettings.UpdateBgTintShader();

            commonGameSettings.CustomLevelGameSettings.Reset();
        }

        // if multiplayer, generate controller-inputs per controller index
        if (commonGameSettings.IsMultiplayer)
            ControllerInputInitialiser.GenerateIndexSpecificControllerActions();

        CreateJars();

        gameThemer.UpdateAllVisualsAndSfx();
        
        if (!IsInEditorScene)
        {
            musicMan.PlayGameMusic();
            Input.MouseMode = Input.MouseModeEnum.Hidden;
        }
        
        UpdateCameraZoom();

        // calls UpdateCameraZoom when viewport size changes
        GetViewport().SizeChanged += UpdateCameraZoom;
	}

    public void UpdateCameraZoom()
    {
        bool isEditing = IsInEditorScene && (editorMan.IsProcessing() || !editorMan.IsNodeReady());
        
        // only use larger view if its setting is enable, in 1 player AND not in the editor scene while editing
        bool enableEnlargedView = commonGameSettings.EnableLargerView && commonGameSettings.PlayerCount == 1 && !isEditing;

        float zoom = enableEnlargedView ? GameConstants.largerViewZoom : 1;

        Vector2 topLeftPos = jars[0].UIMan.TopLeftPos;
        Vector2 bottomRightPos = jars[jars.Count - 1].UIMan.BottomRightPos;

        // get total width of all the jars
        float totalJarWidth = bottomRightPos.X - topLeftPos.X;

        // if in editing, jar hud will be hidden, so reduce the total width (still keeping enough room for editor ui)
        if (isEditing)
            totalJarWidth -= 30 * 2;

        float viewPortWidth = GetViewport().GetVisibleRect().Size.X;

        // if total width of all the jars is larger than the viewport, scale zoom to fit everything in
        if (totalJarWidth > viewPortWidth / zoom)
            zoom = viewPortWidth / totalJarWidth;

        GetViewport().GetCamera2D().Zoom = Vector2.One * zoom;

        if (IsInEditorScene)
            editorMan.UpdateSidebarPosition(jars[0].JarSize.X);
    }

    private void CreateJars()
    {
        // Generate randomised seed to be given to each jar
        sharedSeed = GD.Randi();

        // Make either single-player or multi-player jar group(s) variant depending on player count
        PackedScene jarGroupPrefab = commonGameSettings.PlayerCount == 1 ? jarGroupSinglePrefab : jarGroupMultiPrefab;

        jarsLeftToFill = commonGameSettings.PlayerCount;

        // value used to offset each jar's x pois
        int jarOffsetX = 0;

        for (int i = 0; i < commonGameSettings.PlayerCount; i++)
        {
            JarGroup group = jarGroupPrefab.Instantiate<JarGroup>();
            gameLayer.AddChild(group);

            JarManager jar = group.JarMan;
            jars.Add(jar);
            jar.CommonGameSettings = commonGameSettings;
            jar.PlayerGameSettings = commonGameSettings.IsCustomLevel ? commonGameSettings.CustomLevelGameSettings : commonGameSettings.GetPlayerGameSettings(i);
            jar.PlayerMultiInputSettings = commonGameSettings.GetPlayerMultiInputSettings(i);
            jar.HighScoreList = highScoreList;

            jar.GameMan = this;
            jar.SfxMan = sfxMan;
            jar.PillMan.SfxMan = sfxMan;

            if (jar.UIMan.VirusRing != null)
                jar.UIMan.VirusRing.SfxMan = sfxMan;
                
            jar.GameThemer = gameThemer;

            if (commonGameSettings.CustomLevelID >= 0)
            {
                string levelCode;

                if (commonGameSettings.IsOfficialCustomLevel)
                    levelCode = officialCustomLevelList.Levels[commonGameSettings.CustomLevelID];
                else
                    levelCode = userCustomLevelList.CustomLevels[commonGameSettings.CustomLevelID].code;

                bool success = jar.ImportLevelFromString(levelCode);

                if (!success)
                    failedToLoadCustomLevel = true;
                jar.CurrentTilesToCustomLevelTiles();
            }

            jar.PlayerID = i;
            
            if (IsInEditorScene)
                jar.IsInEditorScene = true;
            else
            {
                jar.JarTiles.Clear();
                jar.ForegroundTiles.Clear();
                jar.PrepareLevel(sharedSeed);
            }

            if (i != 0)
                // offset further if jar if not of a default width
                jarOffsetX += (jar.JarSize.X - GameConstants.jarWidthMin) * (GameConstants.tileSize / 2);


            //group.Position = new Vector2((i - ((commonGameSettings.PlayerCount - 1) / 2.0f)) * jarSpacing, 0);
            group.Position = new Vector2(jarOffsetX, 0);
            
            if (i != commonGameSettings.PlayerCount - 1)
            {
                jarOffsetX += jarSpacing;
                // offset further if jar if not of a default width
                jarOffsetX += (jar.JarSize.X - GameConstants.jarWidthMin) * (GameConstants.tileSize / 2);
            }
        }

        // centre all jars
        if (commonGameSettings.PlayerCount > 1)
        {
            float leftMostPos = jars[0].UIMan.TopLeftPos.X;
            float rightMostPos = jars[jars.Count - 1].UIMan.BottomRightPos.X;

            // left-most edge of first jar at x:0
            float offset = -leftMostPos;
            // a
            offset -= (rightMostPos - leftMostPos) / 2.0f;

            for (int i = 0; i < commonGameSettings.PlayerCount; i++)
            {
                JarGroup group = jars[i].GetParent() as JarGroup;
                group.Position += new Vector2(offset, 0);
            }
        }


        if (failedToLoadCustomLevel)
        {
            if (!commonGameSettings.IsOfficialCustomLevel)
            {
                userCustomLevelList.MarkAsCorrupted(commonGameSettings.CustomLevelID);
                userCustomLevelList.SaveToFile();
            }
            screenTransition.Cover();
        }
    }

    public async void IndicateFinishedFillingJar()
    {
        if (jarsLeftToFill > 0)
            jarsLeftToFill--;

        if (!IsInEditorScene)
            await Task.Delay(startDelay);

        if (!isGameOngoing && jarsLeftToFill == 0 && !failedToLoadCustomLevel)
        {
            isGameOngoing = true;
            remainingPlayers = commonGameSettings.PlayerCount;

            foreach (JarManager jar in jars)
            {
                jar.StartGame();
            }
        }
    }

    public void IndicatePlayerOut()
    {
        remainingPlayers--;

        if (remainingPlayers == 1)
        {
            foreach (JarManager jar in jars)
            {
                if (!jar.IsPlayerOut)
                {
                    jar.Win();
                    return;
                }
            }
        }
    }

    public void EndPlayTest()
    {
        editorMan.EndPlayTest();
    }

    public void EndGame()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        isGameOngoing = false;
        hasPlayerWonEnoughRounds = false;
        bool didSomeoneWin = false;
        
        foreach (JarManager jar in jars)
        {
            if (jar.HasWonEnoughRounds)
                hasPlayerWonEnoughRounds = true;

            if (!jar.IsPlayerOut)
                jar.GameOver();
            
            if (jar.LastWinState)
                didSomeoneWin = true;
        }
        
        // Return early if in editor scene
        if (IsInEditorScene)
            return;

        // If in single player and NOT in editor...
        if (commonGameSettings.PlayerCount == 1 && !IsInEditorScene)
        {
            if (jars[0].LastWinState)
                highScoreList.AddClearedLevel(false);

            // If game over OR not keeping score between levels OR is playing custom level, add score to highscore list
            if (!jars[0].LastWinState || !commonGameSettings.UseScoreKeep || commonGameSettings.IsCustomLevel)
            {
                highScoreList.AddScore(jars[0].Score, false);
                hasSavedScore = true;
            }

            if (commonGameSettings.IsCustomLevel && !commonGameSettings.IsOfficialCustomLevel)
                userCustomLevelList.SaveToFile();
            else
                highScoreList.SaveToFile();
        }

        if (hasPlayerWonEnoughRounds)
            musicMan.PlayMultiWinMusic();
        else if (didSomeoneWin)
            musicMan.PlayWinMusic();
        else
            musicMan.PlayLoseMusic();
    }

    public void SendJunkSegments(int playerReceiving, List<int> colourIndexes)
    {
        jars[playerReceiving].AddJunkSegmentsToQueue(colourIndexes);
    }

    public void UpdateMusic()
    {
        gameThemer.SetMusic(commonGameSettings.CurrentMusic);
    }

    public void PlayHurryUpJingleIfEnabled()
    {
        if (commonGameSettings.EnableHurryUpJingle && !IsInEditorScene)
            musicMan.PlayHurryUpJingle();
    }

    private void PrepareNextRound()
    {
        // Generate randomised seed to be given to each jar
        sharedSeed = GD.Randi();

        isGameOngoing = false;
        jarsLeftToFill = jars.Count;
        if (!(IsInEditorScene && commonGameSettings.EnableEditorMusic))
        {
            // if music is set to random, randomise music for the next round
            if (commonGameSettings.Music == -4)
                UpdateMusic();
                
            musicMan.PlayGameMusic();
        }
        hasSavedScore = false;
        Input.MouseMode = Input.MouseModeEnum.Hidden;
    }

    public void ReplayLevel()
    {
        PrepareNextRound();

        foreach (JarManager jar in jars)
        {
            jar.ResetState(true, hasPlayerWonEnoughRounds);
            jar.PrepareLevel(sharedSeed);
        }

        hasPlayerWonEnoughRounds = false;
    }

    public void NextLevel()
    {
        PrepareNextRound();

        foreach (JarManager jar in jars)
        {
            jar.VirusLevel++;
            jar.ResetState(!commonGameSettings.UseScoreKeep, hasPlayerWonEnoughRounds);
            jar.PrepareLevel(sharedSeed);
        }

        hasPlayerWonEnoughRounds = false;
    }

    public void QuitGame()
    {
        // If in single player, NOT in editor AND hasSavedScore is false, add score to highscore list
        if (commonGameSettings.PlayerCount == 1 && !IsInEditorScene && !hasSavedScore)
        {
            highScoreList.AddScore(jars[0].Score, true);
        }

        screenTransition.Cover();
    }

    public void SetIsPaused(bool b)
    {
        if (isPaused == b || screenTransition.IsCovering)
            return;

        isPaused = b;
        Input.MouseMode = b ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Hidden;

        musicMan.StreamPaused = b;

        if (b)
        {
            pauseMan.SetScreen(0);
            sfxMan.Play("Pause");
        }
        else
        {
            // if multiplayer, generate controller-inputs per controller index
            if (commonGameSettings.IsMultiplayer)
                ControllerInputInitialiser.GenerateIndexSpecificControllerActions();
        }

        touchControlsMan.ShowTouchControlsIfAvailable(!b);
        UpdateCameraZoom();
        pauseMan.SetPauseMenuVisibility(b);

        foreach (JarManager jar in jars)
        {
            jar.ProcessMode = b ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
            jar.PillMan.ProcessMode = b ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
            jar.SetActivePowerUpsPauseState(b);
            jar.SetVirusRingPauseState(b);

            jar.SetTileVisibity(!b);

            if (!b && !commonGameSettings.IsGhostPillEnabled)
                jar.ClearPreviewTiles();

            if (jar.PillMan.IsProcessing())
                jar.PillMan.ActivePill.Visible = !b && !jar.PillMan.IsThrowingPill;

            jar.SetVirusTileAnimationState(commonGameSettings.EnableVirusTileAnimation);
        }
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
        if (Input.IsActionJustPressed("ui_cancel") && isPaused)
        {
            if (pauseMan.CanUseKeyInput)
                pauseMan.GoBack();
        }
		else if (isGameOngoing && (Input.IsActionJustPressed("Pause") || (IsInEditorScene && Input.IsActionJustPressed("EditorPlayTest"))))
        {
            if (IsInEditorScene)
                EndGame();
            else
                SetIsPaused(!isPaused);
        }
	}
}
