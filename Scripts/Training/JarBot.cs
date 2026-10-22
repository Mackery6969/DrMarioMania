using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Plays one jar during training using only the buttons a player has: move left/right, soft drop, hard drop, hold, rotate left/right
// Whenever it gets a new pill (or the pill falls a row) it scores every placement it could still steer to and picks the best,
// then presses buttons to get there while the pill keeps falling like normal
// The genome is just one weight per feature below - the training manager evolves these weights
public class JarBot
{
    // What the bot looks at on the board after trying a placement (and letting any matches clear)
    public static readonly string[] FeatureNames =
    {
        "VirusesCleared",       // viruses this placement clears
        "SegmentsCleared",      // pill segments this placement clears
        "ExtraChains",          // clears caused by things falling after the first clear
        "VirusesRemaining",
        "MaxHeight",            // tallest column (fraction of jar height)
        "TotalHeight",          // all column heights added up (in jar heights)
        "Bumpiness",            // height differences between neighbouring columns (in jar heights)
        "LandingHeight",        // how high up the pill ends up (fraction of jar height)
        "Holes",                // empty cells with something above them
        "BuriedVirusDepth",     // tiles of a different colour stacked on top of viruses
        "ColourBreaks",         // pill segments sitting on a different colour
        "SameColourNeighbours", // touching tiles of the same colour
        "TopRowsFilled",        // tiles in the top 3 rows
        "PushUpDanger",         // tallest column + push-up rows, only when a push-up is about to happen
        "Deadly",               // 1 if this placement tops out (blocks the spawn, goes over the top or gets pushed out)
    };
    public static int FeatureCount { get { return FeatureNames.Length; } }

    // A sensible hand-made starting genome so the first round isn't all random bots
    public static readonly float[] DefaultGenome =
    {
        1.0f, 0.3f, 0.5f, -0.2f,
        -1.0f, -0.3f, -0.2f, -0.3f, -0.5f,
        -0.3f, -0.4f, 0.4f, -0.5f,
        -1.0f, -5.0f,
    };

    private const int topRows = 3;
    // Bots get one input step per 1/60 s of game time however fast the game is running, like a player pressing buttons on a 60fps game
    private const double inputStepTime = 1.0 / 60.0;

    private struct Placement
    {
        public bool found;
        public float score;
        public int rotation;
        // jar column of the pill's grid position
        public int x;
    }

    private readonly TrainingManager trainer;

    public JarManager Jar { get; private set; }
    public JarGroup Group { get { return Jar.GetParent() as JarGroup; } }
    public int Index { get; private set; }
    public float[] Genome { get; set; }
    public bool IsAlive { get; set; }
    public int PillsPlaced { get { return Jar.PillMan.PillsUsed; } }

    // Buttons held this frame and last frame - a button is "just pressed" when it's held now but wasn't last frame
    private readonly HashSet<string> heldButtons = new HashSet<string>();
    private readonly HashSet<string> lastHeldButtons = new HashSet<string>();
    private double inputStepTimer = 0;

    // Current plan, worked out for pill no. plannedPillNo when it was on row plannedRow
    private int plannedPillNo = -1;
    private int plannedRow;
    private bool planToHold;
    private Placement plan;

    public JarBot(TrainingManager trainer, JarManager jar, int index)
    {
        this.trainer = trainer;
        Jar = jar;
        Index = index;
        jar.Bot = this;
    }

    // Game time since this jar last got a new pill - used to spot a jar that's stuck
    private double timeWithoutNewPill = 0;
    private int lastPillsUsed = -1;

    public void Reset()
    {
        heldButtons.Clear();
        lastHeldButtons.Clear();
        inputStepTimer = 0;
        plannedPillNo = -1;
        timeWithoutNewPill = 0;
        lastPillsUsed = -1;
    }

    // Whether this jar has gone longer than maxTime (game time) without getting a new pill
    public bool IsStuck(double delta, double maxTime)
    {
        int pillsUsed = Jar.PillMan.PillsUsed;

        if (pillsUsed != lastPillsUsed)
        {
            lastPillsUsed = pillsUsed;
            timeWithoutNewPill = 0;
            return false;
        }

        timeWithoutNewPill += delta;
        return timeWithoutNewPill > maxTime;
    }

    public bool IsActionPressed(string action)
    {
        return heldButtons.Contains(action);
    }

    public bool IsActionJustPressed(string action)
    {
        return heldButtons.Contains(action) && !lastHeldButtons.Contains(action);
    }

    public void OnJarFilled()
    {
        Jar.StartGame();
    }

    public void OnGameOver()
    {
        trainer.OnBotDied(this);
    }

    // Called every frame the pill can be controlled, before the pill reads its input
    public void UpdateControls(double delta)
    {
        lastHeldButtons.Clear();
        lastHeldButtons.UnionWith(heldButtons);

        // between input steps, keep holding the same buttons (so nothing counts as newly pressed)
        inputStepTimer -= delta;
        if (inputStepTimer > inputStepTime * 0.25)
            return;
        inputStepTimer = Math.Max(inputStepTimer, 0) + inputStepTime;

        PillManager pillMan = Jar.PillMan;
        Pill pill = pillMan.ActivePill;

        heldButtons.Clear();

        if (pillMan.PillsUsed != plannedPillNo)
        {
            // optional pill limit, counts as topping out
            if (trainer.MaxPills > 0 && pillMan.PillsUsed > trainer.MaxPills)
            {
                Jar.GameOver();
                return;
            }

            Plan(true);
        }
        else if (pill.GridPos.Y != plannedRow)
            Plan(false);

        PressButtonsTowardsPlan(pill);
    }

    private void Plan(bool isNewPill)
    {
        PillManager pillMan = Jar.PillMan;
        Pill pill = pillMan.ActivePill;
        BotBoard board = BotBoard.FromJar(Jar);
        Vector2I spawn = pillMan.StartGridPos - Jar.JarOrigin;

        plan = FindBestPlacement(board, pill, pill.GridPos - Jar.JarOrigin, spawn);
        planToHold = false;

        // holding swaps in the held pill (or the next one if nothing's held yet), which then starts from the top
        if (isNewPill && pillMan.CanHold)
        {
            Pill otherPill = pillMan.IsHoldFilled ? pillMan.HoldPill : pillMan.NextPill;
            Placement otherPlan = FindBestPlacement(board, otherPill, spawn, spawn);

            if (otherPlan.found && (!plan.found || otherPlan.score > plan.score))
                planToHold = true;
        }

        plannedPillNo = pillMan.PillsUsed;
        plannedRow = pill.GridPos.Y;
    }

    private void PressButtonsTowardsPlan(Pill pill)
    {
        if (planToHold)
        {
            Tap("Hold");
            return;
        }

        if (!plan.found)
            return;

        int turns = (plan.rotation - pill.PillRotation + 4) % 4;
        if (turns != 0)
        {
            Tap(turns == 3 ? "RotateLeft" : "RotateRight");
            return;
        }

        int x = pill.GridPos.X - Jar.JarOrigin.X;
        if (x != plan.x)
        {
            Tap(x > plan.x ? "MoveLeft" : "MoveRight");
            return;
        }

        Tap("HardDrop");
    }

    // Holds a button for one input step - it has to be let go for a step before it can be pressed again
    private void Tap(string action)
    {
        if (!lastHeldButtons.Contains(action))
            heldButtons.Add(action);
    }

    // Tries every rotation (turning on the spot) and every column the pill can slide to along its current row, dropping straight down from each
    private Placement FindBestPlacement(BotBoard board, Pill pill, Vector2I from, Vector2I spawn)
    {
        Placement best = new Placement();
        Vector2I[] tileKeys = pill.UnrotatedTiles.Keys.ToArray();
        Vector2I[] spawnOffsets = pill.GetRotatedTilePositions(0);

        for (int r = 0; r < 4; r++)
        {
            Vector2I[] offsets = pill.GetRotatedTilePositions(r);

            if (!Fits(board, offsets, from))
                continue;

            int minX = from.X;
            int maxX = from.X;
            while (Fits(board, offsets, new Vector2I(minX - 1, from.Y)))
                minX--;
            while (Fits(board, offsets, new Vector2I(maxX + 1, from.Y)))
                maxX++;

            for (int x = minX; x <= maxX; x++)
            {
                Vector2I pos = new Vector2I(x, from.Y);
                while (Fits(board, offsets, pos + Vector2I.Down))
                    pos += Vector2I.Down;

                float score = ScorePlacement(board, pill, tileKeys, offsets, pos, spawn, spawnOffsets);

                if (!best.found || score > best.score)
                {
                    best.found = true;
                    best.score = score;
                    best.rotation = r;
                    best.x = x;
                }
            }
        }

        return best;
    }

    private static bool Fits(BotBoard board, Vector2I[] offsets, Vector2I pos)
    {
        foreach (Vector2I offset in offsets)
        {
            if (!board.IsFree(pos.X + offset.X, pos.Y + offset.Y))
                return false;
        }

        return true;
    }

    private float ScorePlacement(BotBoard board, Pill pill, Vector2I[] tileKeys, Vector2I[] offsets, Vector2I pos, Vector2I spawn, Vector2I[] spawnOffsets)
    {
        BotBoard b = board.Clone();
        bool overTheTop = false;
        int landingY = b.Height;

        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2I cell = pos + offsets[i];

            if (cell.Y < 0)
            {
                overTheTop = true;
                continue;
            }

            // join side-by-side halves so they fall together
            int linkDir = 0;
            for (int j = 0; j < offsets.Length; j++)
            {
                if (offsets[j] - offsets[i] == Vector2I.Right)
                    linkDir = 1;
                else if (offsets[j] - offsets[i] == Vector2I.Left)
                    linkDir = -1;
            }

            b.AddPillSegment(cell.X, cell.Y, pill.UnrotatedTiles[tileKeys[i]].colour, linkDir);
            landingY = Math.Min(landingY, cell.Y);
        }

        b.Resolve(Jar.PlayerGameSettings.MinStreakLength, out int virusesCleared, out int segmentsCleared, out int chains);

        float[] features = new float[FeatureCount];
        features[0] = virusesCleared;
        features[1] = segmentsCleared;
        features[2] = Math.Max(0, chains - 1);
        features[7] = (float)(b.Height - landingY) / b.Height;

        AddBoardFeatures(b, features, out int maxHeight);

        // a push-up happens once this pill is done if the countdown is on its last throw
        bool pushUpNext = Jar.PushUpCountdown <= 1;
        features[13] = pushUpNext ? (float)(maxHeight + Jar.PushUpRows) / b.Height : 0;

        bool spawnBlocked = !Fits(b, spawnOffsets, spawn);
        bool pushedOut = pushUpNext && maxHeight + Jar.PushUpRows > b.Height;
        features[14] = (overTheTop || spawnBlocked || pushedOut) ? 1 : 0;

        float score = 0;
        for (int i = 0; i < FeatureCount; i++)
            score += Genome[i] * features[i];

        return score;
    }

    private static void AddBoardFeatures(BotBoard b, float[] features, out int maxHeight)
    {
        int width = b.Width;
        int height = b.Height;

        maxHeight = 0;
        int totalHeight = 0;
        int bumpiness = 0;
        int holes = 0;
        int virusesRemaining = 0;
        int buriedVirusDepth = 0;
        int colourBreaks = 0;
        int sameColourNeighbours = 0;
        int topRowsFilled = 0;
        int lastHeight = 0;

        for (int x = 0; x < width; x++)
        {
            int top = height;
            for (int y = 0; y < height; y++)
            {
                if (b.KindAt(x, y) != BotBoard.Empty)
                {
                    top = y;
                    break;
                }
            }

            int columnHeight = height - top;
            maxHeight = Math.Max(maxHeight, columnHeight);
            totalHeight += columnHeight;
            if (x > 0)
                bumpiness += Math.Abs(columnHeight - lastHeight);
            lastHeight = columnHeight;

            for (int y = top + 1; y < height; y++)
            {
                if (b.KindAt(x, y) == BotBoard.Empty)
                    holes++;
            }
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int kind = b.KindAt(x, y);
                if (kind == BotBoard.Empty)
                    continue;

                int colour = b.ColourAt(x, y);

                if (y < topRows)
                    topRowsFilled++;

                if (kind == BotBoard.Virus)
                {
                    virusesRemaining++;

                    for (int above = y - 1; above >= 0; above--)
                    {
                        if (b.KindAt(x, above) != BotBoard.Empty && b.ColourAt(x, above) != colour)
                            buriedVirusDepth++;
                    }
                }

                if (colour <= 0)
                    continue;

                if (y + 1 < height && b.KindAt(x, y + 1) != BotBoard.Empty && b.ColourAt(x, y + 1) > 0)
                {
                    if (b.ColourAt(x, y + 1) == colour)
                        sameColourNeighbours++;
                    else if (kind == BotBoard.Pill)
                        colourBreaks++;
                }

                if (x + 1 < width && b.KindAt(x + 1, y) != BotBoard.Empty && b.ColourAt(x + 1, y) == colour)
                    sameColourNeighbours++;
            }
        }

        features[3] = virusesRemaining;
        features[4] = (float)maxHeight / height;
        features[5] = (float)totalHeight / height;
        features[6] = (float)bumpiness / height;
        features[8] = holes;
        features[9] = buriedVirusDepth;
        features[10] = colourBreaks;
        features[11] = sameColourNeighbours;
        features[12] = topRowsFilled;
    }
}
