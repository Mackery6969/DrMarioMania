using Godot;
using System;
using System.Collections.Generic;

public partial class MusicList : Resource
{
    // Stores list of music paths and provides audio streams from these paths
    
    [Export] private Godot.Collections.Array<string> musicPaths;
    [Export] private Godot.Collections.Array<string> musicTitles;
    [Export] private Godot.Collections.Array<string> musicGames;
    [Export] private CommonGameSettings commonGameSettings;
    [Export] private ThemeList themeList;

    private string pathPrefix = "res://Assets/Audio/Music/";
    private int lastRandomMusic = -1;

    private AudioStream LoadCustomMusic(string path)
    {
        if (FileAccess.FileExists(path))
        {
            FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);

            double loopPoint = 0;
            string fileName = path.Split('/')[^1];
            string[] splitFileName = fileName.Split('.');

            // if filename split by '.' causes a split of more than 2 segments, there might be a valid loop point in the file name, set loopPoint to it if possible
            if (splitFileName.Length > 2)
            {
                string possibleLoopPoint;

                // format is name.#.#.ext
                if (splitFileName.Length > 3)
                    possibleLoopPoint = splitFileName[splitFileName.Length - 3] + "." + splitFileName[splitFileName.Length - 2];
                // format is name.#.ext
                else
                    possibleLoopPoint = splitFileName[splitFileName.Length - 2];

                bool validLoopPoint = double.TryParse(possibleLoopPoint, out loopPoint);
            }

            if (path.Contains(".mp3"))
            {
                AudioStreamMP3 audio = new AudioStreamMP3();
                audio.Data = file.GetBuffer((long)file.GetLength());
                audio.Loop = true;
                audio.LoopOffset = loopPoint;
                return audio;
            }
            else if (path.Contains(".ogg"))
            {
                AudioStreamOggVorbis audio = AudioStreamOggVorbis.LoadFromFile(path);
                audio.Loop = true;
                audio.LoopOffset = loopPoint;
                return audio;
            }
            
            return null;
        }
        else
            return null;
    }

    public List<string> GetCustomMusicList()
	{
		List<string> customMusicList = new List<string>();

		DirAccess dir = DirAccess.Open(GameConstants.ExternalFolderPath);

		// return if folder doesnt exist
		if (dir == null || !dir.DirExists(GameConstants.MusicFolder))
			return customMusicList;

		dir.ChangeDir(GameConstants.MusicFolder);

		string[] files = dir.GetFiles();

		for (int i = 0; i < files.Length; i++)
		{
			string file = files[i];
			bool invalid = false;

			foreach (char forbiddenChar in GameConstants.forbiddenLevelNameChars)
			{
				if (file.Contains(forbiddenChar))
				{
					invalid = true;
					break;
				}
			}

			if (invalid)
				continue;

			if (file.Contains(".mp3") || file.Contains(".ogg"))
				customMusicList.Add(file);
		}
		
		return customMusicList;
	}

    public AudioStream GetMusicStream(int id, string customMusicFile = "")
    {
        if (id == 0)
        // mute/nothing
            return null;

        string path;

        // custom music
        if (id == GameConstants.customMusicID)
        {
            if (customMusicFile == "")
                customMusicFile = commonGameSettings.CurrentCustomMusicFile;

            path = GameConstants.MusicFolderPath + customMusicFile;

            AudioStream audio = LoadCustomMusic(path);
            
            // if audio is not null, return
            if (audio != null)
                return audio;

            // if audio is null, try to find a song with the same name but non-matching extension/loop point in file name (e.g. if song.1.mp3 doesn't exist, maybe song.2.ogg does)

            List<string> list = GetCustomMusicList();
            string songName = customMusicFile.Split('.')[0];

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Split('.')[0] == songName)
                {
                    path = GameConstants.MusicFolderPath + list[i];
                    audio = LoadCustomMusic(path);
                    break;
                }
            }

            // if audio is still null, return theme-based fever music as a fall back
            if (audio == null)
                id = -1;
            else
                return audio;
        }

        // fever based on theme
        if (id == GameConstants.themeFeverMusicID)
            path = pathPrefix + themeList.GetFeverMusicPath(commonGameSettings.CurrentTheme, commonGameSettings.IsMultiplayer);
        // chill based on theme
        else if (id == GameConstants.themeChillMusicID)
            path = pathPrefix + themeList.GetChillMusicPath(commonGameSettings.CurrentTheme, commonGameSettings.IsMultiplayer);
        // random
        else if (id == GameConstants.randomMusicID){
            int previousRandomMusic = lastRandomMusic;
            lastRandomMusic = GD.RandRange(1, musicPaths.Count - 1);

            // don't pick the same song twice in a row (if there's more than one to pick from)
            if (lastRandomMusic == previousRandomMusic && musicPaths.Count > 2)
                lastRandomMusic = 1 + (lastRandomMusic - 1 + GD.RandRange(1, musicPaths.Count - 2)) % (musicPaths.Count - 1);

            path = pathPrefix + musicPaths[lastRandomMusic];
        }
        else
            path = pathPrefix + musicPaths[id];
        
        return ResourceLoader.Load<AudioStream>(path);
    }

    public AudioStream GetThemeMusicStream(string name)
    {
        string path;

        path = themeList.GetMusicFolderPath(commonGameSettings.CurrentTheme) + "/" + name + ".ogg";

        return ResourceLoader.Load<AudioStream>(path);
    }

    public string GetCurrentMusicTitle(int id, string customMusicFile = "")
    {
        if (id >= 0)
            return musicTitles[id];
        else if (id == GameConstants.themeFeverMusicID || id == GameConstants.themeChillMusicID)
        {
            string path;
            
            if (id == GameConstants.themeFeverMusicID)
                path = themeList.GetFeverMusicPath(commonGameSettings.CurrentTheme, commonGameSettings.IsMultiplayer);
            else
                path = themeList.GetChillMusicPath(commonGameSettings.CurrentTheme, commonGameSettings.IsMultiplayer);
            
            if (musicPaths.Contains(path))
            {
                int id2 = musicPaths.IndexOf(path);

                return musicTitles[id2];
            }
        }
        else if (id == GameConstants.randomMusicID)
            return musicTitles[lastRandomMusic];
        else if (id == GameConstants.customMusicID)
            return customMusicFile.Split('.')[0];

        return "Unknown";
    }

    public string GetCurrentMusicGame(int id)
    {
        if (id >= 0)
            return musicGames[id];
        else if (id == GameConstants.themeFeverMusicID || id == GameConstants.themeChillMusicID)
        {
            string path;
            
            if (id == GameConstants.themeFeverMusicID)
                path = themeList.GetFeverMusicPath(commonGameSettings.CurrentTheme, commonGameSettings.IsMultiplayer);
            else
                path = themeList.GetChillMusicPath(commonGameSettings.CurrentTheme, commonGameSettings.IsMultiplayer);
            
            if (musicPaths.Contains(path))
            {
                int id2 = musicPaths.IndexOf(path);

                return musicGames[id2];
            }
        }
        else if (id == GameConstants.randomMusicID)
            return musicGames[lastRandomMusic];
        else if (id == GameConstants.customMusicID)
            return "User Music";

        return "???";
    }
}
