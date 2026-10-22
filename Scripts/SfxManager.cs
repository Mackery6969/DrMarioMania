using Godot;
using System;

public partial class SfxManager : Node
{
    Godot.Collections.Dictionary<string, AudioStreamPlayer> audioPlayers = new Godot.Collections.Dictionary<string, AudioStreamPlayer>();
    // A muted manager plays nothing - training gives these to every jar except the one shown big
    public bool Muted { get; set; } = false;

    // Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
        Godot.Collections.Array<Node> nodes = GetChildren();

		for (int i = 0; i < nodes.Count; i++)
		{
			AudioStreamPlayer audio = (AudioStreamPlayer)nodes[i];

            audioPlayers.Add(audio.Name, audio);
		}
	}

    public void LoadSoundEffects(string sfxPath)
    {
        foreach (string sfxName in audioPlayers.Keys)
        {
            AudioStream strm = ResourceLoader.Load<AudioStream>(sfxPath + "/" + sfxName + ".ogg");
            audioPlayers[sfxName].Stream = strm;
        }
    }

    public void Play(string sound)
    {
        if (Muted)
            return;

        audioPlayers[sound].Play();
    }
}
