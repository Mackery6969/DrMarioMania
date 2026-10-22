using System.Collections.Generic;
using Godot;

public partial class PowerUpPaint : BaseShootPowerUp
{
    [Export] private bool isHorizontal;
    [Export] private Godot.Collections.Array<Sprite2D> projectiles;
    private Vector2[] projectileDirections = { Vector2.Up, Vector2.Down, Vector2.Left, Vector2.Right };
    protected float[] projectileEndPositions;
    protected Vector2I[] gridPositions = new Vector2I[2];
    protected Vector2I[] lastGridPositions = new Vector2I[2];
    protected bool[] finishedProjectiles = { false, false, false, false };
    public Texture2D PillTexture
    {
        set
        {
            foreach (Sprite2D proj in projectiles)
            {
                proj.Texture = value;
            }
        }
    }

    private int remainingProjectiles = 0;

    // Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
        // if rainbow, choose random colour that a pill can be
        if (colour == 0)
            colour = jarMan.PossiblePillColours[jarMan.PowerUpRng.RandiRange(0, jarMan.PossiblePillColours.Count - 1)];

        for (int i = 0; i < projectiles.Count; i++)
        {
		    lastGridPositions[i] = InitialGridPos;
            // update frame to match power up colour
            projectiles[i].Frame += projectiles[i].Hframes * (colour - 1);
        }

        sprite.Visible = false;

        projectileEndPositions = new float[4]{ jarMan.JarTopPos, jarMan.JarBottomPos, jarMan.JarLeftPos, jarMan.JarRightPos };
	}

    // tries to change the colour of a tile unless its unpaintable, returns whethe was success or not
    protected bool AttemptToPaintSegment(Vector2I pos)
    {
        return jarMan.PaintTile(pos, colour);
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
    {
        speed += acceleration * (float)delta;
        if (speed > maxSpeed)
            speed = maxSpeed;

        remainingProjectiles = 0;

        for (int i = 0; i < projectiles.Count; i++)
        {
            if (finishedProjectiles[i])
                continue;

            int dirIndex = isHorizontal ? i + 2 : i;

            remainingProjectiles++;
            
            projectiles[i].Position += projectileDirections[dirIndex] * speed * (float)delta;
            gridPositions[i] = WorldPosToGridPos(projectiles[i].GlobalPosition);

            if (gridPositions[i] != lastGridPositions[i])
            {
                List<Vector2I> positions = GetPositionsBetweenPositions(lastGridPositions[i], gridPositions[i], true);
                bool changedAnything = false;
                bool doRebound = false;

                for (int j = 0; j < positions.Count; j++)
                {
                    // end if tile is present and it isn't paintable
                    if (jarMan.IsTilePresent(positions[j]) && !jarMan.DoesTileHaveColour(positions[j]))
                    {
                        doRebound = true;
                        finishedProjectiles[i] = true;
                    }

                    if (AttemptToPaintSegment(positions[j]))
                        changedAnything = true;

                    if (doRebound)
                        break;
                }

                if (changedAnything)
                    sfxMan.Play("Paint");
            }
            
            lastGridPositions[i] = gridPositions[i];

            float endPos = projectileEndPositions[dirIndex];

            if (projectileDirections[dirIndex].Y == 0)
            {
                float x = projectiles[i].GlobalPosition.X;

                if (projectileDirections[dirIndex].X < 0)
                {
                    if (x <= endPos)
                        finishedProjectiles[i] = true;
                }
                else
                {
                    if (x >= endPos)
                        finishedProjectiles[i] = true;
                }
            }
            else
            {
                float y = projectiles[i].GlobalPosition.Y;

                if (projectileDirections[dirIndex].Y < 0)
                {
                    if (y <= endPos)
                        finishedProjectiles[i] = true;
                }
                else
                {
                    if (y >= endPos)
                        finishedProjectiles[i] = true;
                }
            }

            if (finishedProjectiles[i])
                projectiles[i].Visible = false;
        }

        if (remainingProjectiles == 0)
        {
            FinishPowerUp();
        }
    }
}
