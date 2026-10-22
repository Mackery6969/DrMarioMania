using Godot;
using System;

public partial class GameThemer : Node
{
	[ExportGroup("Audio Player References")]
	[Export] private SfxManager sfxMan;
	[Export] private MusicManager musicMan;

	[ExportGroup("Visual References")]
	[Export] private TextureRect bgTexture;
	
	[ExportGroup("Other References")]
	[Export] private GameManager gameMan;

	[ExportGroup("Optional References")]
	[Export] private EditorCursor editorCursor;
	[Export] private EditorTileTypeSelector tileTypeSelector;
	[Export] private EditorColourSelector colourSelector;

	[ExportGroup("Resources")]
	[Export] private CommonGameSettings commonGameSettings;
	[Export] private ThemeList themeList;
	[Export] private MusicList musicList;
	public MusicList MusicList { get { return musicList; } }

	public void UpdateAllVisualsAndSfx()
	{
        commonGameSettings.UpdateBgTintShader();

        UpdateCommonVisuals();

		foreach (JarManager jarMan in gameMan.Jars)
		{
			jarMan.UIMan.UpdateJarVisuals(commonGameSettings.CurrentTheme, themeList);
		}

		UpdateEditorVisuals();
		UpdateMusic();
		UpdateSoundEffects();
	}

	public void UpdateHoldGroup()
	{
		foreach (JarManager jarMan in gameMan.Jars)
		{
			jarMan.UIMan.UpdateHoldGroup(commonGameSettings.CurrentTheme, themeList);
		}
	}
	
	public void UpdateBackground()
	{
        bgTexture.Texture = themeList.GetBgTilesTexture(commonGameSettings.CurrentPlayerGameSettings.SpeedLevel, commonGameSettings.CurrentTheme, commonGameSettings.CurrentIsUsingCustomBgColour);
	}
	
	public void UpdateJarTexture()
	{
		foreach (JarManager jarMan in gameMan.Jars)
		{
			jarMan.UIMan.UpdateJarTexture(commonGameSettings.CurrentTheme, themeList);
		}
	}
	public void UpdateMarioSprite()
	{
		foreach (JarManager jarMan in gameMan.Jars)
		{
			jarMan.UIMan.UpdateMarioSprite(commonGameSettings.CurrentTheme, themeList);
		}
	}

	private void UpdateCommonVisuals()
	{
		UpdateBackground();
        UpdateJarTexture();
    }

	private void UpdateSoundEffects()
	{
		sfxMan.LoadSoundEffects(themeList.GetSfxFolderPath(commonGameSettings.CurrentTheme));
	}

	public void SetMusic(int music)
	{
		AudioStream strm = musicList.GetMusicStream(music);

		musicMan.GameMusic = strm;
	}

	private void UpdateMusic()
	{
		SetMusic(commonGameSettings.CurrentMusic);

		musicMan.WinMusic = musicList.GetThemeMusicStream("Win");
		musicMan.MultiWinMusic = musicList.GetThemeMusicStream("MultiWin");
		musicMan.LoseMusic = musicList.GetThemeMusicStream("Lose");
		musicMan.HurryUpJingle = musicList.GetThemeMusicStream("HurryUp");
	}

	private void UpdateEditorVisuals()
	{
		if (editorCursor != null)
		{
			editorCursor.SetTileTypeTextures(0, themeList.GetPillTileTexture(commonGameSettings.CurrentTheme));
			editorCursor.SetTileTypeTextures(1, themeList.GetVirusTileTexture(commonGameSettings.CurrentTheme));
			editorCursor.SetTileTypeTextures(2, themeList.GetPowerUpTileTexture(commonGameSettings.CurrentTheme));
			editorCursor.SetTileTypeTextures(3, themeList.GetObjectTileTexture(commonGameSettings.CurrentTheme));
		}

		if (tileTypeSelector != null)
		{
			tileTypeSelector.PillTexture = themeList.GetPillTileTexture(commonGameSettings.CurrentTheme);
			tileTypeSelector.VirusTexture = themeList.GetVirusTileTexture(commonGameSettings.CurrentTheme);
			tileTypeSelector.PowerUpTexture = themeList.GetPowerUpTileTexture(commonGameSettings.CurrentTheme);
			tileTypeSelector.ObjectTexture = themeList.GetObjectTileTexture(commonGameSettings.CurrentTheme);
		}
		
		if (colourSelector != null)
		{
			colourSelector.PillTexture = themeList.GetPillTileTexture(commonGameSettings.CurrentTheme);
			colourSelector.VirusTexture = themeList.GetVirusTileTexture(commonGameSettings.CurrentTheme);
			colourSelector.PowerUpTexture = themeList.GetPowerUpTileTexture(commonGameSettings.CurrentTheme);
			colourSelector.ObjectTexture = themeList.GetObjectTileTexture(commonGameSettings.CurrentTheme);
		}
	}
}
