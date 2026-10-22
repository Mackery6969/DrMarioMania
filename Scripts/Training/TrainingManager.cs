using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

// Trains marathon bots with an evolutionary algorithm - started with the "--train" command line argument
// Every jar is one bot with its own genome (see JarBot), and every jar plays the same seed at the same time
// A round ends once every jar has topped out - the top scorers carry over and are bred to make the next round's genomes
// Shown Tetris 99 style: the highest-scoring jar that's still alive is big in the middle, the rest are small either side
public partial class TrainingManager : Node
{
    public static bool IsRequested { get { return CommandLineArgs.Has("train"); } }

    // the "MANIA" theme
    private const int maniaTheme = 0;
    // default rules (regular pills, no power-ups) - loaded fresh so the player's own saved settings aren't used or changed
    private const string defaultRulesPath = "res://Assets/Resources/P1GameSettings.tres";

    // Options - each can be set with a "--name=value" command line argument
    private int jarCount;           // --jars, also the population size
    private int eliteCount;         // --elites, best genomes copied unchanged into the next round
    private int tournamentSize;     // --tournament, genomes compared when picking each parent
    private float mutationRate;     // --mutation-rate, chance of each weight being changed
    private float mutationSize;     // --mutation-size, how much a changed weight moves (standard deviation)
    private float smallJarScale;    // --small-scale
    private string runName;         // --run, save folder name (user://training/<run>/)
    // --max-pills, ends a bot's game after this many pills (0 = no limit, the round only ends when every jar tops out)
    public int MaxPills { get; private set; }

    private GameManager gameMan;
    private PlayerGameSettings rules;
    private List<JarBot> bots = new List<JarBot>();
    private List<float[]> population = new List<float[]>();
    private float[] fitness;
    // does all the evolution randomness (and picks the game seed when there isn't one yet) - seeded by --seed to make a whole run repeatable
    private RandomNumberGenerator rng = new RandomNumberGenerator();

    private int round = 0;
    private ulong roundSeed;
    // the seed the whole run plays on (see LoadOrCreatePopulation)
    private ulong gameSeed;
    // --new-seeds, plays every round on a different seed instead (harder to build up on, but bots have to get good at any board)
    private bool newSeedEachRound;
    private int aliveCount = 0;
    private float bestScore = 0;
    private float[] bestGenome;
    private float lastRoundBest = 0;
    private float lastRoundMean = 0;

    // Speed
    private const double minSpeed = 0.25;
    private const double maxSpeed = 32;
    private bool isHeadless;
    private bool isPaused = false;
    // speed chosen with the speed keys (or --time-scale)
    private double baseSpeed = 1;
    // Auto speed: doubles the speed each time no jar dies for a while, back to baseSpeed as soon as one does
    private double autoSpeedTime;   // --auto-speed, real seconds without a jar dying before the speed doubles (0 = off)
    private double autoSpeedMax;    // --auto-speed-max, auto speed won't go above this
    private double autoSpeedTimer;
    private int autoSpeedLevel = 0; // no. of times the speed has been doubled since the last jar died

    // A jar that hasn't had a new pill for this long (in game time) is treated as stuck and ended, so a broken jar can't hold up the round forever
    private const double stuckJarTime = 60;

    // Audio
    private SfxManager mutedSfxMan;
    private double musicShuffleTime; // --music-shuffle, minutes between switching to a new random song (0 = keep playing the chosen music)
    private double musicShuffleTimer;
    private int musicID;
    private string musicTitle = "";
    private const float musicFadeTime = 1.5f;
    // the music player's normal volume, which fades go back to
    private float musicVolume;
    private Tween musicFade;

    // Display
    private int focusIndex = 0;
    private bool isLayoutDirty = true;
    private double focusCheckTimer = 0;
    private Label infoLabel;

    private string SaveFolder { get { return "user://training/" + runName + "/"; } }

    public void Start(GameManager gameMan)
    {
        this.gameMan = gameMan;

        jarCount = Math.Max(2, CommandLineArgs.GetInt("jars", 50));
        eliteCount = Mathf.Clamp(CommandLineArgs.GetInt("elites", 4), 1, jarCount - 1);
        tournamentSize = Math.Max(1, CommandLineArgs.GetInt("tournament", 3));
        mutationRate = CommandLineArgs.GetFloat("mutation-rate", 0.2f);
        // weights are mostly around +/-0.25, so bigger moves than this tend to wreck a good genome
        mutationSize = CommandLineArgs.GetFloat("mutation-size", 0.1f);
        newSeedEachRound = CommandLineArgs.Has("new-seeds");
        smallJarScale = CommandLineArgs.GetFloat("small-scale", 0.3f);
        runName = CommandLineArgs.Get("run") ?? "default";
        MaxPills = Math.Max(0, CommandLineArgs.GetInt("max-pills", 0));

        // vsync would stop the game running more than one frame per screen refresh when sped up
        isHeadless = DisplayServer.GetName() == "headless";
        if (!isHeadless)
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);

        baseSpeed = Math.Clamp(CommandLineArgs.GetFloat("time-scale", 1), minSpeed, maxSpeed);
        // headless runs already go as fast as possible, speeding them up more just means bots get fewer input steps
        autoSpeedTime = isHeadless ? 0 : Math.Max(0, CommandLineArgs.GetFloat("auto-speed", 30));
        autoSpeedMax = Math.Min(CommandLineArgs.GetFloat("auto-speed-max", 4), maxSpeed);
        musicShuffleTime = Math.Max(0, CommandLineArgs.GetFloat("music-shuffle", 5)) * 60;
        ResetAutoSpeed();

        CommonGameSettings settings = gameMan.CommonGameSettings;

        if (settings.FixedSeed.HasValue)
            rng.Seed = settings.FixedSeed.Value;
        else
            rng.Randomize();

        // none of these are saved to the config file
        settings.GameMode = 1;
        settings.CustomLevelID = -2;
        settings.ThemeOverride = maniaTheme;
        // training jars use the multiplayer layout (no Mario or virus ring), so the game has to treat them as multiplayer
        settings.PlayerCount = jarCount;

        rules = ResourceLoader.Load<PlayerGameSettings>(defaultRulesPath, null, ResourceLoader.CacheMode.Ignore);
        rules.VirusDifficulty = CommandLineArgs.GetInt("difficulty", rules.VirusDifficulty);
        rules.SpeedLevel = CommandLineArgs.GetInt("speed-level", rules.SpeedLevel);

        // only the jar shown big plays sound effects - every other jar gets a muted sound effect manager
        mutedSfxMan = new SfxManager();
        mutedSfxMan.Muted = true;
        AddChild(mutedSfxMan);

        for (int i = 0; i < jarCount; i++)
        {
            JarBot bot = new JarBot(this, gameMan.CreateTrainingJar(rules), i);
            bot.Jar.SfxMan = mutedSfxMan;
            bot.Jar.UIMan.HideWinIcons();
            bots.Add(bot);
        }

        // also loads the sound effects
        gameMan.GameThemer.UpdateAllVisualsAndSfx();

        // with music shuffle on, every song is a random one (including the first)
        musicID = musicShuffleTime > 0 ? GameConstants.randomMusicID : settings.CurrentMusic;
        musicShuffleTimer = musicShuffleTime;
        musicVolume = gameMan.MusicMan.VolumeDb;
        PlayMusic();

        CreateInfoLabel();
        LoadOrCreatePopulation();

        GetViewport().SizeChanged += () => isLayoutDirty = true;

        // start once everything has finished entering the tree
        Callable.From(StartRound).CallDeferred();
    }

    private void StartRound()
    {
        round++;
        roundSeed = newSeedEachRound ? rng.Randi() : gameSeed;
        aliveCount = jarCount;
        SetFocus(0);
        ResetAutoSpeed();

        for (int i = 0; i < jarCount; i++)
        {
            JarBot bot = bots[i];
            bot.Genome = population[i];
            bot.IsAlive = true;
            bot.Reset();
            bot.Group.Modulate = Colors.White;
            fitness[i] = 0;

            bot.Jar.ResetState(true, true);
            bot.Jar.PrepareLevel(roundSeed);
        }

        GD.Print("Round " + round + " started (seed " + roundSeed + ")");
    }

    public void OnBotDied(JarBot bot)
    {
        if (!bot.IsAlive)
            return;

        bot.IsAlive = false;
        fitness[bot.Index] = bot.Jar.Score;
        bot.Group.Modulate = new Color(1, 1, 1, 0.35f);
        aliveCount--;
        ResetAutoSpeed();

        if (aliveCount == 0)
            Callable.From(EndRound).CallDeferred();
    }

    private void EndRound()
    {
        int[] order = Enumerable.Range(0, jarCount).OrderByDescending(i => fitness[i]).ToArray();

        lastRoundBest = fitness[order[0]];
        lastRoundMean = fitness.Average();
        float worst = fitness[order[jarCount - 1]];
        float meanPills = (float)bots.Average(b => b.PillsPlaced);

        if (bestGenome == null || lastRoundBest > bestScore)
        {
            bestScore = lastRoundBest;
            bestGenome = (float[])population[order[0]].Clone();
        }

        GD.Print("Round " + round + ": best " + lastRoundBest + ", mean " + lastRoundMean.ToString("0") + ", worst " + worst + ", all-time best " + bestScore);
        AppendLog(string.Join(",", round, roundSeed, Format(lastRoundBest), Format(lastRoundMean), Format(worst), Format(bestScore), Format(meanPills)));

        population = NextGeneration(order);
        Save();

        StartRound();
    }

    // Evolution ===========================================================

    private List<float[]> NextGeneration(int[] order)
    {
        List<float[]> next = new List<float[]>();

        // the best few carry over unchanged - the very best goes first, so it starts the round in the big slot
        for (int i = 0; i < eliteCount; i++)
            next.Add(population[order[i]]);

        while (next.Count < jarCount)
        {
            float[] mum = population[TournamentPick()];
            float[] dad = population[TournamentPick()];
            next.Add(Mutated(Crossover(mum, dad)));
        }

        return next;
    }

    // Picks a few genomes at random and returns the index of the highest scoring one
    private int TournamentPick()
    {
        int best = rng.RandiRange(0, jarCount - 1);

        for (int i = 1; i < tournamentSize; i++)
        {
            int challenger = rng.RandiRange(0, jarCount - 1);
            if (fitness[challenger] > fitness[best])
                best = challenger;
        }

        return best;
    }

    // Each weight comes from one parent or the other
    private float[] Crossover(float[] mum, float[] dad)
    {
        float[] child = new float[mum.Length];

        for (int i = 0; i < child.Length; i++)
            child[i] = rng.Randf() < 0.5f ? mum[i] : dad[i];

        return child;
    }

    private float[] Mutated(float[] genome)
    {
        float[] mutant = (float[])genome.Clone();

        for (int i = 0; i < mutant.Length; i++)
        {
            if (rng.Randf() < mutationRate)
                mutant[i] += rng.Randfn(0, mutationSize);
        }

        return Normalised(mutant);
    }

    private float[] RandomGenome()
    {
        float[] genome = new float[JarBot.FeatureCount];

        for (int i = 0; i < genome.Length; i++)
            genome[i] = rng.RandfRange(-1, 1);

        return Normalised(genome);
    }

    // Multiplying every weight by the same amount doesn't change which placement wins, so genomes are kept at length 1 to stop them drifting
    private static float[] Normalised(float[] genome)
    {
        float length = Mathf.Sqrt(genome.Sum(w => w * w));

        if (length > 0)
        {
            for (int i = 0; i < genome.Length; i++)
                genome[i] /= length;
        }

        return genome;
    }

    // Saving/loading ======================================================

    private void LoadOrCreatePopulation()
    {
        TrainingSaveData save = CommandLineArgs.Has("fresh") ? null : LoadSave();

        if (save != null)
        {
            round = save.Round;
            bestScore = save.BestScore;
            bestGenome = save.BestGenome;
            population = save.Population.Take(jarCount).ToList();

            GD.Print("Resuming training run \"" + runName + "\" from round " + round);
        }

        // the seed every round is played on, kept for the whole run so the bots can build up on it
        // --seed picks it, otherwise a resumed run keeps its own and a new run gets a random one
        CommonGameSettings settings = gameMan.CommonGameSettings;

        if (settings.FixedSeed.HasValue)
            gameSeed = settings.FixedSeed.Value;
        else if (save != null && save.Seed.HasValue)
            gameSeed = save.Seed.Value;
        else
            gameSeed = rng.Randi();

        // scores from a different seed can't be compared, so the all-time best starts again (the genomes are kept)
        if (save != null && save.Seed != gameSeed && !newSeedEachRound)
        {
            bestScore = 0;
            GD.Print("Training run \"" + runName + "\" is now on seed " + gameSeed + ", so its all-time best starts again");
        }

        // new run: the hand-made genome, then a mix of variations on it and completely random ones
        // resuming with more jars than before: variations on the best genome so far
        float[] baseGenome = bestGenome ?? Normalised((float[])JarBot.DefaultGenome.Clone());

        if (population.Count == 0)
            population.Add(baseGenome);

        while (population.Count < jarCount)
            population.Add(population.Count % 2 == 0 && save == null ? RandomGenome() : Mutated(baseGenome));

        fitness = new float[jarCount];
    }

    private TrainingSaveData LoadSave()
    {
        string path = SaveFolder + "population.json";

        if (!FileAccess.FileExists(path))
            return null;

        try
        {
            using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            TrainingSaveData save = JsonSerializer.Deserialize<TrainingSaveData>(file.GetAsText());

            if (save.FeatureNames == null || !save.FeatureNames.SequenceEqual(JarBot.FeatureNames))
            {
                GD.PrintErr("Saved training run \"" + runName + "\" uses different bot features, starting a new run");
                return null;
            }

            return save;
        }
        catch (Exception e)
        {
            GD.PrintErr("Couldn't load training run \"" + runName + "\", starting a new run: " + e.Message);
            return null;
        }
    }

    private void Save()
    {
        DirAccess.MakeDirRecursiveAbsolute(SaveFolder);

        TrainingSaveData save = new TrainingSaveData
        {
            Round = round,
            BestScore = bestScore,
            Seed = gameSeed,
            BestGenome = bestGenome,
            FeatureNames = JarBot.FeatureNames,
            Population = population,
        };

        using FileAccess file = FileAccess.Open(SaveFolder + "population.json", FileAccess.ModeFlags.Write);

        if (file == null)
        {
            GD.PrintErr("Couldn't save training run: " + FileAccess.GetOpenError());
            return;
        }

        file.StoreString(JsonSerializer.Serialize(save, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void AppendLog(string line)
    {
        DirAccess.MakeDirRecursiveAbsolute(SaveFolder);

        string path = SaveFolder + "log.csv";
        bool isNewFile = !FileAccess.FileExists(path);

        using FileAccess file = FileAccess.Open(path, isNewFile ? FileAccess.ModeFlags.Write : FileAccess.ModeFlags.ReadWrite);

        if (file == null)
            return;

        if (isNewFile)
            file.StoreLine("round,seed,best,mean,worst,all_time_best,mean_pills");
        else
            file.SeekEnd();

        file.StoreLine(line);
    }

    private static string Format(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    // Display =============================================================

    public override void _Process(double delta)
    {
        if (isLayoutDirty)
        {
            isLayoutDirty = false;
            UpdateLayout();
        }

        // delta is sped up by the time scale, so undo that for things that go by real time
        double realDelta = delta / Engine.TimeScale;

        // check twice a second
        focusCheckTimer -= realDelta;

        if (focusCheckTimer <= 0)
        {
            focusCheckTimer = 0.5;
            UpdateFocus();
        }

        if (!isPaused)
        {
            UpdateAutoSpeed(realDelta);
            UpdateMusicShuffle(realDelta);
            EndStuckJars(delta);
        }

        UpdateInfoLabel();
    }

    private void UpdateAutoSpeed(double realDelta)
    {
        if (autoSpeedTime <= 0 || aliveCount == 0)
            return;

        autoSpeedTimer -= realDelta;

        if (autoSpeedTimer <= 0)
        {
            autoSpeedTimer = autoSpeedTime;

            if (Engine.TimeScale * 2 <= autoSpeedMax)
            {
                autoSpeedLevel++;
                ApplySpeed();
            }
        }
    }

    // Back to the chosen speed, and restarts the wait before speeding up again
    private void ResetAutoSpeed()
    {
        autoSpeedTimer = autoSpeedTime;

        if (autoSpeedLevel != 0 || Engine.TimeScale != baseSpeed)
        {
            autoSpeedLevel = 0;
            ApplySpeed();
        }
    }

    private void EndStuckJars(double delta)
    {
        foreach (JarBot bot in bots)
        {
            if (bot.IsAlive && bot.IsStuck(delta, stuckJarTime))
            {
                GD.PrintErr("Jar " + bot.Index + " hasn't had a new pill for " + stuckJarTime + "s, ending its game");
                bot.Jar.GameOver();
            }
        }
    }

    // Music ===============================================================

    private void PlayMusic()
    {
        gameMan.GameThemer.SetMusic(musicID);
        gameMan.MusicMan.PlayGameMusic();
        gameMan.MusicMan.StreamPaused = isPaused;

        MusicList musicList = gameMan.GameThemer.MusicList;
        string customFile = gameMan.CommonGameSettings.CurrentCustomMusicFile;
        musicTitle = (musicList.GetCurrentMusicTitle(musicID, customFile) + " (" + musicList.GetCurrentMusicGame(musicID) + ")").ToUpper();
    }

    private void UpdateMusicShuffle(double realDelta)
    {
        if (musicShuffleTime <= 0)
            return;

        musicShuffleTimer -= realDelta;

        if (musicShuffleTimer <= 0)
        {
            musicShuffleTimer = musicShuffleTime;
            ShuffleMusic();
        }
    }

    // Fades the current song out, then fades a new random one in
    private void ShuffleMusic()
    {
        MusicManager musicMan = gameMan.MusicMan;
        float silent = musicVolume - 40;

        // a fade that's still going is replaced
        if (musicFade != null && musicFade.IsValid())
            musicFade.Kill();

        musicFade = CreateTween();
        // fades take the same real time whatever speed the game is running at
        musicFade.SetIgnoreTimeScale(true);
        musicFade.TweenProperty(musicMan, "volume_db", silent, musicFadeTime);
        musicFade.TweenCallback(Callable.From(PlayMusic));
        musicFade.TweenProperty(musicMan, "volume_db", musicVolume, musicFadeTime).From(silent);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // the speed keys go from the speed currently shown, including any auto speed up
        if (e.IsActionPressed("TrainingSpeedUp"))
            SetBaseSpeed(Engine.TimeScale * 2);
        else if (e.IsActionPressed("TrainingSlowDown"))
            SetBaseSpeed(Engine.TimeScale / 2);
        else if (e.IsActionPressed("TrainingSpeedReset"))
            SetBaseSpeed(1);
        else if (e.IsActionPressed("Pause"))
            SetPaused(!isPaused);
    }

    // Freezes every jar the same way the normal pause menu does (the info text and speed keys keep working)
    private void SetPaused(bool paused)
    {
        isPaused = paused;
        ProcessModeEnum mode = paused ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;

        foreach (JarBot bot in bots)
        {
            bot.Jar.ProcessMode = mode;
            bot.Jar.PillMan.ProcessMode = mode;
        }

        gameMan.MusicMan.StreamPaused = paused;
    }

    // Puts a jar in the big slot and gives it the real sound effects
    private void SetFocus(int index)
    {
        bots[focusIndex].Jar.SfxMan = mutedSfxMan;
        focusIndex = index;
        bots[focusIndex].Jar.SfxMan = gameMan.SfxMan;
        isLayoutDirty = true;
    }

    // Bots get one input step per 1/60 s of game time, so a sped up game also has to run more frames per second to give them every step
    // (if the computer can't keep up, the bots get fewer steps and play a bit worse)
    private void SetBaseSpeed(double speed)
    {
        baseSpeed = Math.Clamp(speed, minSpeed, maxSpeed);
        autoSpeedLevel = 0;
        autoSpeedTimer = autoSpeedTime;
        ApplySpeed();
    }

    private void ApplySpeed()
    {
        double speed = Math.Min(baseSpeed * Math.Pow(2, autoSpeedLevel), maxSpeed);
        Engine.TimeScale = speed;

        // headless runs use --fixed-fps 60 instead, which already runs as fast as possible
        if (!isHeadless)
            Engine.MaxFps = (int)Math.Round(60 * Math.Max(speed, 1));
    }

    // Moves the highest-scoring jar that's still alive into the big slot
    private void UpdateFocus()
    {
        JarBot leader = null;

        foreach (JarBot bot in bots)
        {
            if (bot.IsAlive && (leader == null || bot.Jar.Score > leader.Jar.Score))
                leader = bot;
        }

        if (leader == null)
            return;

        JarBot focused = bots[focusIndex];

        if (!focused.IsAlive || leader.Jar.Score > focused.Jar.Score)
            SetFocus(leader.Index);
    }

    private void UpdateLayout()
    {
        const float gap = 6;
        // room at the top of the screen for the info text (in screen pixels)
        const float infoSpace = 44;

        Vector2 view = GetViewport().GetVisibleRect().Size;

        // size of a jar at normal scale, from its UI corner markers
        JarGroup firstGroup = bots[0].Group;
        Vector2 jarTopLeft = firstGroup.ToLocal(bots[0].Jar.UIMan.TopLeftPos);
        Vector2 jarSize = firstGroup.ToLocal(bots[0].Jar.UIMan.BottomRightPos) - jarTopLeft;
        Vector2 smallSize = jarSize * smallJarScale;

        int smallCount = jarCount - 1;
        int leftCount = (smallCount + 1) / 2;
        int rightCount = smallCount / 2;

        // pick how many columns of small jars each side gets so everything fits on screen as big as possible
        int columns = 1;
        float zoom = 0;
        for (int c = 1; c <= leftCount; c++)
        {
            int r = (leftCount + c - 1) / c;
            float blockWidth = c * smallSize.X + (c - 1) * gap;
            float blockHeight = r * smallSize.Y + (r - 1) * gap;
            float totalWidth = jarSize.X + 2 * (blockWidth + gap * 2);
            float totalHeight = Math.Max(jarSize.Y, blockHeight);
            float z = Math.Min(view.X / totalWidth, (view.Y - infoSpace) / totalHeight);

            if (z > zoom)
            {
                zoom = z;
                columns = c;
            }
        }

        // the camera is centred on (0, 0), so nudge everything down to leave room for the info text
        float yShift = infoSpace / 2 / zoom;
        float smallBlockWidth = columns * smallSize.X + (columns - 1) * gap;

        PlaceJar(bots[focusIndex], 1, jarTopLeft, new Vector2(-jarSize.X / 2, -jarSize.Y / 2 + yShift));

        int n = 0;
        foreach (JarBot bot in bots)
        {
            if (bot.Index == focusIndex)
                continue;

            bool isLeft = n < leftCount;
            int sideIndex = isLeft ? n : n - leftCount;
            int sideRows = ((isLeft ? leftCount : rightCount) + columns - 1) / columns;
            float blockHeight = sideRows * smallSize.Y + (sideRows - 1) * gap;
            float blockX = isLeft ? -jarSize.X / 2 - gap * 2 - smallBlockWidth : jarSize.X / 2 + gap * 2;

            Vector2 topLeft = new Vector2(
                blockX + (sideIndex % columns) * (smallSize.X + gap),
                -blockHeight / 2 + (sideIndex / columns) * (smallSize.Y + gap) + yShift);

            PlaceJar(bot, smallJarScale, jarTopLeft, topLeft);
            n++;
        }

        GetViewport().GetCamera2D().Zoom = Vector2.One * zoom;
    }

    private static void PlaceJar(JarBot bot, float scale, Vector2 jarTopLeft, Vector2 topLeft)
    {
        JarGroup group = bot.Group;
        group.Scale = Vector2.One * scale;
        group.Position = topLeft - jarTopLeft * scale;
    }

    private void CreateInfoLabel()
    {
        CanvasLayer layer = new CanvasLayer();
        layer.Layer = 10;
        AddChild(layer);

        infoLabel = new Label();
        infoLabel.Position = new Vector2(4, 2);
        infoLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        infoLabel.AddThemeConstantOverride("outline_size", 4);
        layer.AddChild(infoLabel);
    }

    private void UpdateInfoLabel()
    {
        JarBot focused = bots[focusIndex];

        infoLabel.Text = "ROUND " + round + "  ALIVE " + aliveCount + "/" + jarCount + "  x" + Engine.TimeScale
            + (autoSpeedLevel > 0 ? " AUTO" : "")
            + (isHeadless ? "" : "  " + Engine.GetFramesPerSecond() + "FPS")
            + (isPaused ? "  PAUSED" : "")
            + "\nLEADER " + focused.Jar.Score + "  LAST BEST " + lastRoundBest + "  ALL-TIME " + bestScore
            + "\nMUSIC: " + musicTitle;
    }
}

// What gets saved to user://training/<run>/population.json after every round
public class TrainingSaveData
{
    public int Round { get; set; }
    public float BestScore { get; set; }
    // the seed the run plays on (null in runs saved before runs kept one seed)
    public ulong? Seed { get; set; }
    public float[] BestGenome { get; set; }
    public string[] FeatureNames { get; set; }
    public List<float[]> Population { get; set; }
}
