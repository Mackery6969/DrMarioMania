using Godot;
using System;
using System.Collections.Generic;
using static ColourOrderCategoryEnums;

public partial class CommonGameSettings : Resource
{
    [ExportGroup("Misc References")]
    [Export] private ShaderMaterial bgTintMat;


    [ExportGroup("Player Game Setting Resources")]
    // Player-specific game settings (stored in their own resources)
    [Export] public PlayerGameSettings P1GameSettings { get; set; }
    [Export] public PlayerGameSettings P2GameSettings { get; set; }
    [Export] public PlayerGameSettings P3GameSettings { get; set; }
    [Export] public PlayerGameSettings P4GameSettings { get; set; }
    [Export] public PlayerGameSettings CustomLevelGameSettings { get; set; }

    [ExportGroup("Player Multiplayer Input Resources")]
    [Export] public PlayerMultiInputSettings P1MultiInputSettings { get; set; }
    [Export] public PlayerMultiInputSettings P2MultiInputSettings { get; set; }
    [Export] public PlayerMultiInputSettings P3MultiInputSettings { get; set; }
    [Export] public PlayerMultiInputSettings P4MultiInputSettings { get; set; }


    [ExportGroup("Other Resources")]
    [Export] private CustomisableActionList actionList;
    [Export] public ThemeList themeList { get; set; }

    // The player that is being configured in the menu
    // 0 = player 1 (in single player)
    // 1 = player 1 (in multiplayer)
    // 2 = player 2
    // 3 = player 3
    // 4 = player 4
    public int PlayerConfiguring { get; set; }

    // 0 = normal - regular level (clear all viruses to win - aka classic, versus, pre-made/custom level)
    // 1 = marathon - endless level (new viruses spawn from bottom)
    public int GameMode { get; set; } = 0;

    // -1 = new, unsaved level
    // -2 = not a custom level
    public int CustomLevelID { get; set; } = -2;
    public string CustomLevelName { get; set; } = "";
    public bool IsCustomLevel { get { return CustomLevelID != -2; } }
    
    // Whether or not the custom level to be loaded is a built-in level rather than a user-saved one
    private bool isOfficialCustomLevel = false;
    public bool IsOfficialCustomLevel
    {
        get
        {
            return isOfficialCustomLevel && IsCustomLevel;
        }
    
        set
        {
            isOfficialCustomLevel = value;
        }
    }

    public PlayerGameSettings GetPlayerGameSettings(int playerID)
    {
        switch (playerID)
        {
            case 1:
                return P2GameSettings;
            case 2:
                return P3GameSettings;
            case 3:
                return P4GameSettings;
            default:
                return P1GameSettings;
        }

    }

    public PlayerMultiInputSettings GetPlayerMultiInputSettings(int playerID)
    {
        switch (playerID)
        {
            case 1:
                return P2MultiInputSettings;
            case 2:
                return P3MultiInputSettings;
            case 3:
                return P4MultiInputSettings;
            default:
                return P1MultiInputSettings;
        }

    }

    // Get the player game settings currently being edited
    public PlayerGameSettings CurrentPlayerGameSettings { get { return IsCustomLevel ? CustomLevelGameSettings : GetPlayerGameSettings(PlayerConfiguring - 1); } }

    // Updates the id of the keyboard "arrow" equivalents (arrows, wasd, etc) OR controller id for each player based on which players are using keys or controllers
    // Also sets MultiplayerIsUsingFullKeyboard to true if only one player is using the keyboard, otherwise its false
    public void RefreshMultiplayerInputIndexes()
    {
        int keyboardCount = 0;
        int controllerCount = 0;

        int lastKeyboardPlayerID = -1;
        int lastControllerPlayerID = -1;

        for (int i = 0; i < PlayerCount; i++)
        {
            PlayerMultiInputSettings player = GetPlayerMultiInputSettings(i);

            player.MultiplayerIsControlMethodExclusive = false;

            if (player.MultiplayerIsUsingController)
            {
                player.MultiplayerInputID = controllerCount;
                lastControllerPlayerID = i;
                controllerCount++;
            }
            else
            {
                player.MultiplayerInputID = keyboardCount;
                lastKeyboardPlayerID = i;
                keyboardCount++;
            }
        }

        if (keyboardCount == 1)
            GetPlayerMultiInputSettings(lastKeyboardPlayerID).MultiplayerIsControlMethodExclusive = true;
        
        if (controllerCount == 1)
            GetPlayerMultiInputSettings(lastControllerPlayerID).MultiplayerIsControlMethodExclusive = true;
    }
    
    [ExportGroup("Game Settings")]
	// Game settings that are not player-specific

	// no. of players
    [Export] public int PlayerCount { get; set; }
    public bool IsMultiplayer { get { return PlayerCount > 1; } }
    // game theme chosen by player
    [Export] public int Theme { get; set; }
	// game theme for the current custom level
    public int CustomLevelTheme { get; set; } = 0;
    // background music during gameplay chosen by player
    [Export] public int Music
    // 1, 2, 3, etc = songs defined in MusicList resource
    // 0 = mute
    // -1 = fever-based on selected theme
    // -2 = chill-based on selected theme
    // -3 = "more" button (doesn't set if passed)
    // -4 = random
    // -5 = custom song
    {
        get
        {
            return music;
        }
        set
        {
            if (value != -3)
                music = value;
        }
    }
    private int music;
    // user-stored song
    public string CustomMusicFile { get; set; } = "";
    public string CustomLevelCustomMusicFile { get; set; } = "";

    // background music for the current custom level
    [Export] public int CustomLevelMusic
    {
        get
        {
            return customLevelMusic;
        }
        set
        {
            if (value != -3)
                customLevelMusic = value;
        }
    }
    private int customLevelMusic = -1;

    // bg colour
    public bool IsCustomLevelUsingCustomBgColour { get; set; } = false;
    public bool IsUsingCustomBgColour { get; set; } = false;
    public bool CurrentIsUsingCustomBgColour
    {
        get { return IsCustomLevel ? IsCustomLevelUsingCustomBgColour : IsUsingCustomBgColour; }
        set
        {
            if (IsCustomLevel)
                IsCustomLevelUsingCustomBgColour = value;
            else
                IsUsingCustomBgColour = value;

            UpdateBgTintShader();
        }
    }

    public Color CustomLevelCustomBgColour { get; set; } = new Color(1,1,1);
    public Color CustomBgColour { get; set; } = new Color(1,1,1);
    public Color CurrentCustomBgColour
    {
        get { return IsCustomLevel ? CustomLevelCustomBgColour : CustomBgColour; }
        set
        {
            if (IsCustomLevel)
                CustomLevelCustomBgColour = value;
            else
                CustomBgColour = value;

            UpdateBgTintShader();
        }
    }

    public void UpdateBgTintShader()
    {
        bgTintMat.SetShaderParameter("tint_colour", CurrentCustomBgColour);
        bgTintMat.SetShaderParameter("is_active", CurrentIsUsingCustomBgColour);
    }

    // auto get the correct theme and music depending on whether in a custom level or not
    public int CurrentTheme { get { return IsCustomLevel ? CustomLevelTheme : Theme; } }
	public int CurrentMusic { get { return IsCustomLevel ? CustomLevelMusic : Music; } }
	public string CurrentCustomMusicFile { get { return IsCustomLevel ? CustomLevelCustomMusicFile : CustomMusicFile; } }
    public int MultiplayerRequiredWinCount { get; set; } = 3;
    public bool MultiplayerUseJunkPills { get; set; } = true;
    
    // whether or not "score keep" is enabled, which is when your score carries over to the next level when the previous level is cleared
    public bool UseScoreKeep { get; set; } = false;

    [ExportGroup("Quick Game Settings")]
	// quick game settings ======================================
    // gameplay settings that don't affect difficulty and can be toggled during matches via pause menu
    [Export] public bool IsGhostPillEnabled { get; set; }
    [Export] public bool IsHardDropEnabled { get; set; }

    [ExportGroup("Input Settings")]
	// input settings ===========================================
    [Export] public bool ManualAutoFallSpeedUp { get; set; }
    private bool swapABButtons = false;
    public bool SwapABButtons
    {
        get
        {
            return swapABButtons;
        }
        set
        {
            if (swapABButtons == value)
                return;
            
            swapABButtons = value;

            // Get array of events for the original actions
			Godot.Collections.Array<InputEvent> acceptEvents = InputMap.ActionGetEvents("ui_accept");
			Godot.Collections.Array<InputEvent> cancelEvents = InputMap.ActionGetEvents("ui_cancel");

            // Remove any controller button events
            foreach (InputEvent evnt in acceptEvents)
            {
                if (evnt is InputEventJoypadButton)
                    InputMap.ActionEraseEvent("ui_accept", evnt);
            }
            foreach (InputEvent evnt in cancelEvents)
            {
                if (evnt is InputEventJoypadButton)
                    InputMap.ActionEraseEvent("ui_cancel", evnt);
            }

            // if TRUE, use nintendo layout (a on right)
            // if FALSE, use xbox/ps layout (a on bottom)

            InputEventJoypadButton acceptButton = new InputEventJoypadButton();
            InputEventJoypadButton cancelButton = new InputEventJoypadButton();

            // allow all controllers to use
            acceptButton.Device = -1;
            cancelButton.Device = -1;

            acceptButton.ButtonIndex = value ? JoyButton.B : JoyButton.A;
            cancelButton.ButtonIndex = value ? JoyButton.A : JoyButton.B;

            // add newly defined button input events
            InputMap.ActionAddEvent("ui_accept", acceptButton);
            InputMap.ActionAddEvent("ui_cancel", cancelButton);

        }
    }

    private void CopyActions(string sourceAction, string modifyingAction)
    {
        InputMap.ActionEraseEvents(modifyingAction);

        Godot.Collections.Array<InputEvent> newEvents = InputMap.ActionGetEvents(sourceAction);

        foreach (InputEvent item in newEvents)
        {
            InputMap.ActionAddEvent(modifyingAction, item);
        }
    }

    // audio settings ======================================
    public int MusicVolume
    {
        get
        {
            return musicVolume;
        }
        set
        {
            musicVolume = value;
            SetBusVolume(AudioServer.GetBusIndex("Music"), value);
        }
    }
    private int musicVolume = 100;
    public int SFXVolume
    {
        get
        {
            return sfxVolume;
        }
        set
        {
            sfxVolume = value;
            SetBusVolume(AudioServer.GetBusIndex("SFX"), value);
        }
    }
    private int sfxVolume = 100;
    public bool EnableEditorMusic { get; set; } = true;
    public bool EnableHurryUpJingle { get; set; } = true;

    private void SetBusVolume(int index, int vol)
    {
        float volDb = Mathf.LinearToDb(vol / 100.0f);
        
        AudioServer.SetBusVolumeDb(index, volDb);

        AudioServer.SetBusMute(index, vol == 0);
    }

    // graphics settings ======================================

    // enables/disables the animation of the virus tiles in the jar (not the viruses under the magnifying glass)
    public bool EnableVirusTileAnimation { get; set; } = true;
    public bool EnableLargerView { get; set; } = false;

    // Overrides a custom level's colours with a custom palette while playing them (not used while editing though)
    private Godot.Collections.Dictionary<int, Godot.Collections.Array<int>> overrideCustomLevelColours = new Godot.Collections.Dictionary<int, Godot.Collections.Array<int>>();
    public Godot.Collections.Array<int> CurrentThemeOverrideCustomLevelColours
    {
        get
        {
            int colorOrderIndex = (int)themeList.GetColourOrder(CurrentTheme);

            if (overrideCustomLevelColours.ContainsKey(colorOrderIndex))
            {
                return overrideCustomLevelColours[colorOrderIndex];
            }
            else
            {
                GD.Print("Trying to get saved override colours but they don't exist (null), should be checking whether they exist first");
                return null;
            }
        }
    }
    public bool CurrentThemeHasOverrideCustomLevelColours
    {
        get
        {
            int colorOrderIndex = (int)themeList.GetColourOrder(CurrentTheme);

            return overrideCustomLevelColours.ContainsKey(colorOrderIndex) && overrideCustomLevelColours[colorOrderIndex].Count > 0;
        }
    }
    public Godot.Collections.Array<int> GetOverrideCustomLevelColours(int colourOrder)
    {
        if (overrideCustomLevelColours.ContainsKey(colourOrder))
        {
            return overrideCustomLevelColours[colourOrder];
        }
        else
        {
            return null;
        }
    }
    public void AddOverrideCustomLevelColour(int colourOrder, int colour)
    {
        if (!overrideCustomLevelColours.ContainsKey(colourOrder))
            overrideCustomLevelColours.Add(colourOrder, new Godot.Collections.Array<int>());

        if (overrideCustomLevelColours[colourOrder].Contains(colour))
            GD.Print("!! trying to add an override colour that already exists");
        else
            overrideCustomLevelColours[colourOrder].Add(colour);
    }
    public void RemoveOverrideCustomLevelColour(int colourOrder, int colour)
    {
        if (overrideCustomLevelColours.ContainsKey(colourOrder) && overrideCustomLevelColours[colourOrder].Contains(colour))
            overrideCustomLevelColours[colourOrder].Remove(colour);
        else
            GD.Print("!! trying to remove an override colour that doesn't exist");
    }
    public bool ColourOrderHasOverrideColour(int colourOrder, int colour)
    {
        if (overrideCustomLevelColours.ContainsKey(colourOrder) && overrideCustomLevelColours[colourOrder].Contains(colour))
            return true;
        else
            return false;
    }

    // misc settings ======================================
    
    // Whether or not the disclaimer screen has been seen
    public bool HasSeenDisclaimer { get; set; } = false;
    // Whether or not settings have been loaded from a file already
    public bool HasLoadedSettings { get; set; } = false;
    // The last screen id in the menu scene
    public int LastMenuScreen { get; set; } = -1;
    public List<int> LastMenuScreenHistory { get; set; }
    // Last viewed page of the user/official custom level screens
    public int LastUserCustomLevelPage { get; set; } = 0;
    public int LastOfficialCustomLevelPage { get; set; } = 0;

    private Godot.Collections.Dictionary<StringName, Godot.Collections.Array<InputEvent>> defaultKeybinds = new();

    public Godot.Collections.Array<InputEvent> GetDefaultKeybindsForAction(string actID)
    {
        return defaultKeybinds[actID];
    }

    private void BackUpKeybindsAsDefault()
    {
        defaultKeybinds.Clear();
        
        foreach (StringName actID in InputMap.GetActions())
        {
            defaultKeybinds.Add(actID, new());

            foreach (InputEvent evnt in InputMap.ActionGetEvents(actID))
            {
                defaultKeybinds[actID].Add(evnt);
            }
        }
    }

    // gets dictonary of all customisable actions and their input events
    private Godot.Collections.Dictionary<StringName, Godot.Collections.Array<InputEvent>> GetCustomisableActionEvents()
    {
        Godot.Collections.Dictionary<StringName, Godot.Collections.Array<InputEvent>> actionEvents = new();
        
        foreach (StringName actionID in actionList.ActionIDs)
        {
            var AddEventsToAction = (StringName actID) =>
            {
                actionEvents.Add(actID, new());

                foreach (InputEvent evnt in InputMap.ActionGetEvents(actID))
                {
                    actionEvents[actID].Add(evnt);
                }
            };

            AddEventsToAction(actionList.KeyboardPrefix + actionID);
            AddEventsToAction(actionList.ControllerPrefix + actionID);
        }

        return actionEvents;
    }

    // replaces events for each given action by the given input events
    // dictonary key: action id, value: array of input events
    private void SetActionEvents(Godot.Collections.Dictionary<StringName, Godot.Collections.Array<InputEvent>> actionEvents)
    {
        foreach (StringName actID in actionEvents.Keys)
        {
            InputMap.ActionEraseEvents(actID);

            foreach (InputEvent evnt in actionEvents[actID])
            {
                InputMap.ActionAddEvent(actID, evnt);
            }
        }
    }

    public void SaveToFile()
    {
        GD.Print("About to save config file...");

        ConfigFile config = new ConfigFile();

        config.SetValue("Game Settings", "Theme", Theme);
        config.SetValue("Game Settings", "Music", Music);
        config.SetValue("Game Settings", "UseScoreKeep", UseScoreKeep);
        config.SetValue("Game Settings", "P1Settings", P1GameSettings.ExportToString());
        config.SetValue("Game Settings", "IsUsingCustomBgColour", IsUsingCustomBgColour);
        config.SetValue("Game Settings", "CustomBgColour", CustomBgColour);

        config.SetValue("Multiplayer Settings", "MultiplayerRequiredWinCount", MultiplayerRequiredWinCount);
        config.SetValue("Multiplayer Settings", "MultiplayerUseJunkPills", MultiplayerUseJunkPills);

        config.SetValue("Quick Game Settings", "IsGhostPillEnabled", IsGhostPillEnabled);
        config.SetValue("Quick Game Settings", "IsHardDropEnabled", IsHardDropEnabled);
        config.SetValue("Input Settings", "ManualAutoFallSpeedUp", ManualAutoFallSpeedUp);
        config.SetValue("Input Settings", "SwapABButtons", SwapABButtons);
        config.SetValue("Input Settings", "Keybinds", GetCustomisableActionEvents());

        config.SetValue("Graphics Settings", "LastWindowMode", (int)DisplayServer.WindowGetMode());
        config.SetValue("Graphics Settings", "OverrideCustomLevelColours", overrideCustomLevelColours);
        config.SetValue("Graphics Settings", "EnableVirusTileAnimation", EnableVirusTileAnimation);
        config.SetValue("Graphics Settings", "EnableLargerView", EnableLargerView);

        config.SetValue("Audio Settings", "MusicVolume", MusicVolume);
        config.SetValue("Audio Settings", "SFXVolume", SFXVolume);
        config.SetValue("Audio Settings", "EnableEditorMusic", EnableEditorMusic);
        config.SetValue("Audio Settings", "EnableHurryUpJingle", EnableHurryUpJingle);
        config.SetValue("Audio Settings", "CustomMusicFile", CustomMusicFile);

        config.SetValue("Misc Settings", "HasSeenDisclaimer", HasSeenDisclaimer);

        Error error = config.Save("user://config.cfg");

        if (error == Error.Ok)
            GD.Print("Saved config file!");
        else
            GD.Print("Couldn't save config file...");
    }

    public void LoadFromFile()
    {
        // save default keybinds before loading any data
        if (!HasLoadedSettings)
        {
            BackUpKeybindsAsDefault();
        }

        GD.Print("About to load config file...");

        ConfigFile config = new ConfigFile();
        Error error = config.Load("user://config.cfg");

        if (error != Error.Ok)
        {
            GD.Print("Config file not found");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            return;
        }

        Theme = (int)config.GetValue("Game Settings", "Theme");
        Music = (int)config.GetValue("Game Settings", "Music");
        UseScoreKeep = (bool)config.GetValue("Game Settings", "UseScoreKeep");
        P1GameSettings.ImportFromString((string)config.GetValue("Game Settings", "P1Settings"));

        MultiplayerRequiredWinCount = (int)config.GetValue("Multiplayer Settings", "MultiplayerRequiredWinCount");
        
        if (config.HasSectionKey("Multiplayer Settings", "MultiplayerUseJunkPills"))
            MultiplayerUseJunkPills = (bool)config.GetValue("Multiplayer Settings", "MultiplayerUseJunkPills");

        
        if (config.HasSection("Quick Game Settings"))
        {
            IsGhostPillEnabled = (bool)config.GetValue("Quick Game Settings", "IsGhostPillEnabled");
            IsHardDropEnabled = (bool)config.GetValue("Quick Game Settings", "IsHardDropEnabled");

            ManualAutoFallSpeedUp = (bool)config.GetValue("Input Settings", "ManualAutoFallSpeedUp");
            SwapABButtons = (bool)config.GetValue("Input Settings", "SwapABButtons");
        }

        if (config.HasSectionKey("Input Settings", "Keybinds"))
        {
            Godot.Collections.Dictionary<StringName, Godot.Collections.Array<InputEvent>> actionEvents;

            actionEvents = new Godot.Collections.Dictionary<StringName, Godot.Collections.Array<InputEvent>>((Godot.Collections.Dictionary)config.GetValue("Input Settings", "Keybinds"));

            SetActionEvents(actionEvents);
        }

        // for legacy setting format
        if (config.HasSection("Quick Settings"))
        {
            IsGhostPillEnabled = (bool)config.GetValue("Quick Settings", "IsGhostPillEnabled");
            IsHardDropEnabled = (bool)config.GetValue("Quick Settings", "IsHardDropEnabled");
            ManualAutoFallSpeedUp = (bool)config.GetValue("Quick Settings", "ManualAutoFallSpeedUp");
        }

        if (config.HasSectionKey("Graphics Settings", "LastWindowMode"))
        {
            DisplayServer.WindowMode lastWindowMode = (DisplayServer.WindowMode)(int)config.GetValue("Graphics Settings", "LastWindowMode");
            DisplayServer.WindowSetMode(lastWindowMode);

            // workaround for the windowed mode not getting set properly (godot issue?)
            if (lastWindowMode == DisplayServer.WindowMode.Windowed)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);

                // set window size to default
                Vector2I defaultSize = new Vector2I((int)ProjectSettings.GetSetting("display/window/size/window_width_override"), (int)ProjectSettings.GetSetting("display/window/size/window_height_override"));
                DisplayServer.WindowSetSize(defaultSize);

                // center window
                DisplayServer.WindowSetPosition((DisplayServer.ScreenGetSize() - DisplayServer.WindowGetSize()) / 2);
            }
        }

        if (config.HasSectionKey("Graphics Settings", "OverrideCustomLevelColours"))
        {
            overrideCustomLevelColours = new Godot.Collections.Dictionary<int, Godot.Collections.Array<int>>((Godot.Collections.Dictionary)config.GetValue("Graphics Settings", "OverrideCustomLevelColours"));
        }

        if (config.HasSectionKey("Graphics Settings", "EnableVirusTileAnimation"))
        {
            EnableVirusTileAnimation = (bool)config.GetValue("Graphics Settings", "EnableVirusTileAnimation");
        }

        if (config.HasSectionKey("Graphics Settings", "EnableLargerView"))
        {
            EnableLargerView = (bool)config.GetValue("Graphics Settings", "EnableLargerView");
        }
        else
        {
            EnableLargerView = GameConstants.IsOnMobile ? true : false;
        }

        if (config.HasSectionKey("Misc Settings", "HasSeenDisclaimer"))
            HasSeenDisclaimer = (bool)config.GetValue("Misc Settings", "HasSeenDisclaimer");

        if (config.HasSection("Audio Settings"))
        {
            MusicVolume = (int)config.GetValue("Audio Settings", "MusicVolume");
            SFXVolume = (int)config.GetValue("Audio Settings", "SFXVolume");

            if (config.HasSectionKey("Audio Settings", "EnableEditorMusic"))
                EnableEditorMusic = (bool)config.GetValue("Audio Settings", "EnableEditorMusic");

            if (config.HasSectionKey("Audio Settings", "EnableHurryUpJingle"))
                EnableHurryUpJingle = (bool)config.GetValue("Audio Settings", "EnableHurryUpJingle");

            if (config.HasSectionKey("Audio Settings", "CustomMusicFile"))
                CustomMusicFile = (string)config.GetValue("Audio Settings", "CustomMusicFile");
        }

        if (config.HasSectionKey("Game Settings", "CustomBgColour"))
        {
            IsUsingCustomBgColour = (bool)config.GetValue("Game Settings", "IsUsingCustomBgColour");
            CustomBgColour = (Color)config.GetValue("Game Settings", "CustomBgColour");

            UpdateBgTintShader();
        }

        HasLoadedSettings = true;

        GD.Print("Loaded config file!");
    }
}
