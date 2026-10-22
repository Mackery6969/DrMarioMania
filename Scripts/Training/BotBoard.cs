using Godot;
using System;

// A simplified copy of a jar's tiles that training bots use to try out placements without touching the real jar
// Follows the jar's rules: lines of MinStreakLength or more of one colour are cleared, then loose pill segments fall (side-by-side halves fall together)
public class BotBoard
{
    public const int Empty = 0;
    public const int Virus = 1;
    public const int Pill = 2;
    // anything else (power-ups, objects) - never falls or matches
    public const int Block = 3;

    public readonly int Width;
    public readonly int Height;

    private readonly int[] kind;
    private readonly int[] colour;
    // +1 = joined to the segment on the right, -1 = joined to the segment on the left, 0 = not joined sideways
    // (top/bottom halves don't need joining, they fall the same as separate segments would)
    private readonly int[] link;

    public BotBoard(int width, int height)
    {
        Width = width;
        Height = height;
        kind = new int[width * height];
        colour = new int[width * height];
        link = new int[width * height];
    }

    public BotBoard Clone()
    {
        BotBoard b = new BotBoard(Width, Height);
        Array.Copy(kind, b.kind, kind.Length);
        Array.Copy(colour, b.colour, colour.Length);
        Array.Copy(link, b.link, link.Length);
        return b;
    }

    public static BotBoard FromJar(JarManager jar)
    {
        BotBoard b = new BotBoard(jar.JarSize.X, jar.JarSize.Y);
        TileMapLayer tiles = jar.JarTiles;

        for (int y = 0; y < b.Height; y++)
        {
            for (int x = 0; x < b.Width; x++)
            {
                Vector2I pos = jar.JarOrigin + new Vector2I(x, y);
                int sourceID = tiles.GetCellSourceId(pos);

                if (sourceID == -1)
                    continue;

                int i = b.Index(x, y);
                b.colour[i] = jar.GetTileColour(pos);

                if (sourceID == GameConstants.virusSourceID)
                    b.kind[i] = Virus;
                else if (sourceID == GameConstants.pillSourceID)
                {
                    b.kind[i] = Pill;

                    int atlasX = tiles.GetCellAtlasCoords(pos).X;
                    if (atlasX == PillConstants.atlasLeft && x + 1 < b.Width)
                        b.link[i] = 1;
                    else if (atlasX == PillConstants.atlasRight && x > 0)
                        b.link[i] = -1;
                }
                else
                    b.kind[i] = Block;
            }
        }

        // only keep sideways links where both halves agree
        for (int i = 0; i < b.link.Length; i++)
        {
            if (b.link[i] == 1 && b.link[i + 1] != -1)
                b.link[i] = 0;
            else if (b.link[i] == -1 && b.link[i - 1] != 1)
                b.link[i] = 0;
        }

        return b;
    }

    private int Index(int x, int y) { return y * Width + x; }

    public int KindAt(int x, int y) { return kind[Index(x, y)]; }
    public int ColourAt(int x, int y) { return colour[Index(x, y)]; }

    // Free = inside the jar's sides and bottom, and empty (anything above the top of the jar counts as free, like in the real jar)
    public bool IsFree(int x, int y)
    {
        if (x < 0 || x >= Width || y >= Height)
            return false;

        return y < 0 || kind[Index(x, y)] == Empty;
    }

    // Adds a pill segment - linkDir joins it sideways to a segment placed next to it (+1 = right, -1 = left)
    public void AddPillSegment(int x, int y, int col, int linkDir)
    {
        int i = Index(x, y);
        kind[i] = Pill;
        colour[i] = col;
        link[i] = linkDir;
    }

    // Clears matching lines and lets things fall until nothing changes
    // chains = no. of times lines were cleared (1 = just the first match, 2+ = cleared again after falling)
    public void Resolve(int minStreak, out int virusesCleared, out int segmentsCleared, out int chains)
    {
        virusesCleared = 0;
        segmentsCleared = 0;
        chains = 0;

        bool[] toClear = new bool[kind.Length];

        while (MarkLines(minStreak, toClear))
        {
            chains++;

            for (int i = 0; i < kind.Length; i++)
            {
                if (!toClear[i])
                    continue;

                if (kind[i] == Virus)
                    virusesCleared++;
                else
                    segmentsCleared++;

                // the other half of a cleared pill is now a single segment
                if (link[i] != 0)
                    link[i + link[i]] = 0;

                kind[i] = Empty;
                colour[i] = 0;
                link[i] = 0;
                toClear[i] = false;
            }

            ApplyGravity();
        }
    }

    private bool IsMatchable(int i)
    {
        return (kind[i] == Virus || kind[i] == Pill) && colour[i] > 0;
    }

    // Marks every tile that's part of a horizontal or vertical line of minStreak or more, returns whether any were found
    private bool MarkLines(int minStreak, bool[] marks)
    {
        bool found = false;

        // horizontal
        for (int y = 0; y < Height; y++)
        {
            int x = 0;
            while (x < Width)
            {
                int start = Index(x, y);
                int end = x + 1;

                if (IsMatchable(start))
                {
                    while (end < Width && IsMatchable(Index(end, y)) && colour[Index(end, y)] == colour[start])
                        end++;

                    if (end - x >= minStreak)
                    {
                        for (int k = x; k < end; k++)
                            marks[Index(k, y)] = true;
                        found = true;
                    }
                }

                x = end;
            }
        }

        // vertical
        for (int x = 0; x < Width; x++)
        {
            int y = 0;
            while (y < Height)
            {
                int start = Index(x, y);
                int end = y + 1;

                if (IsMatchable(start))
                {
                    while (end < Height && IsMatchable(Index(x, end)) && colour[Index(x, end)] == colour[start])
                        end++;

                    if (end - y >= minStreak)
                    {
                        for (int k = y; k < end; k++)
                            marks[Index(x, k)] = true;
                        found = true;
                    }
                }

                y = end;
            }
        }

        return found;
    }

    // Drops unsupported pill segments one row at a time until nothing moves
    private void ApplyGravity()
    {
        bool moved = true;

        while (moved)
        {
            moved = false;

            for (int y = Height - 2; y >= 0; y--)
            {
                for (int x = 0; x < Width; x++)
                {
                    int i = Index(x, y);

                    if (kind[i] != Pill || link[i] == -1)
                        continue;

                    if (link[i] == 1)
                    {
                        // side-by-side halves only fall if both have space below
                        if (kind[Index(x, y + 1)] == Empty && kind[Index(x + 1, y + 1)] == Empty)
                        {
                            MoveDown(x, y);
                            MoveDown(x + 1, y);
                            moved = true;
                        }
                    }
                    else if (kind[Index(x, y + 1)] == Empty)
                    {
                        MoveDown(x, y);
                        moved = true;
                    }
                }
            }
        }
    }

    private void MoveDown(int x, int y)
    {
        int from = Index(x, y);
        int to = Index(x, y + 1);

        kind[to] = kind[from];
        colour[to] = colour[from];
        link[to] = link[from];

        kind[from] = Empty;
        colour[from] = 0;
        link[from] = 0;
    }
}
