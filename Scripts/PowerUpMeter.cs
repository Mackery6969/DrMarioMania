using Godot;
using System;
using System.Collections.Generic;
using static PowerUpEnums;
using static PowerUpConstants;

public partial class PowerUpMeter : Control
{
    [Export] private Material rainbowMat;

    [ExportGroup("Local References")]
    [Export] private Sprite2D nextPowerUpIcon;
    [Export] private NinePatchRect fillRect;
    [Export] private NinePatchRect emptyRect;

    [ExportGroup("External References")]
    [Export] private JarManager jarMan;

    private Texture2D fillTexture;
    public Texture2D FillTexture
    {
        set
        {
            fillTexture = value;
            fillRect.Texture = value;
        }
    }
    private Texture2D fillReadyTexture;
    public Texture2D FillReadyTexture
    {
        set
        {
            fillReadyTexture = value;
        }
    }
    public Texture2D EmptyTexture
    {
        set
        {
            emptyRect.Texture = value;
        }
    }

    // The current level/progress of the meter
    private int currentLevel = 0;
    
    // The next power-up which will be given once the meter is fully filled up
    private PowerUp nextPowerUp;
    public PowerUp NextPowerUp { get { return nextPowerUp; } }
    // The colour of the next power-up - 0 means the power-up has no colour
    private int nextColour;
    public int NextColour { get { return nextColour; } }

    // Whether or not the power-up is ready to be used
    private bool isPowerUpReady = false;
    public bool IsPowerUpReady { get { return isPowerUpReady; } }

    // Player-specific game settings via jar manager
    private PlayerGameSettings PlayerGameSettings { get { return jarMan.PlayerGameSettings; } }

    public Texture2D IconTexture { set { nextPowerUpIcon.Texture = value; } }

    // Whether the power-up h and v frames have been updated
    private bool hasUpdatedIconFrameSize = false;


    // Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
        fillRect.PivotOffset = new Vector2(Size.X / 2, Size.Y / 2);
        fillRect.Size = Vector2.Zero;

        ResetState();
	}

    public void IncrementLevel(int value)
    {
        currentLevel += value;

        // Wrap-around max level
        if (currentLevel >= PlayerGameSettings.PowerUpMeterMaxLevel)
        {
            // If wrapped-around again while a power-up is already ready (would happen if an unrealistically good combo was achieved, cap at just below the max level)
            if (isPowerUpReady)
                currentLevel = PlayerGameSettings.PowerUpMeterMaxLevel - 1;
            // Else, set isPowerUpReady to true
            else
            {
                jarMan.SfxMan.Play("PowerUpMeterFull");
                isPowerUpReady = true;
            }

            currentLevel -= PlayerGameSettings.PowerUpMeterMaxLevel;
        }

        UpdateVisuals();
    }

    // Sets powerUp to a new power up, along with changing powerUpColour and resetting isPowerUpReady
    public void QueueNewPowerUp()
    {
        isPowerUpReady = false;

        List<PowerUp> availablePowerUps = PlayerGameSettings.AvailablePowerUps;
        List<PowerUp> availableSpecialPowerUps = PlayerGameSettings.AvailableSpecialPowerUps;

        bool useSpecialPowerUp;
        
        // if no normal power-ups, but special ones are present, always use special
        if (availablePowerUps.Count == 0)
            useSpecialPowerUp = true;
        // if no special power-ups, but normal ones are present, never use special
        else if (availableSpecialPowerUps.Count == 0)
            useSpecialPowerUp = false;
        // if both normal AND special power-ups exist, do random chance of special
        else
            useSpecialPowerUp = jarMan.PowerUpRng.RandiRange(1, specialChance) == 1;

        // Randomise next special power up, colour always zero (rainbow)
        if (useSpecialPowerUp)
        {
            nextPowerUp = availableSpecialPowerUps[jarMan.PowerUpRng.RandiRange(0, availableSpecialPowerUps.Count - 1)];
            nextColour = 0;
        }
        // Randomise next normal power up and colour
        else
        {
            nextPowerUp = availablePowerUps[jarMan.PowerUpRng.RandiRange(0, availablePowerUps.Count - 1)];
            nextColour = jarMan.PossiblePowerUpColours[jarMan.PowerUpRng.RandiRange(0, jarMan.PossiblePowerUpColours.Count - 1)];
        }

        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (isPowerUpReady)
        {
            fillRect.Size = Size;

            if (fillRect.Texture != fillReadyTexture)
                fillRect.Texture = fillReadyTexture;
        }
        else
        {
            fillRect.Size = new Vector2(Size.X, (int)(Size.Y * currentLevel / PlayerGameSettings.PowerUpMeterMaxLevel));

            if (fillRect.Texture != fillTexture)
                fillRect.Texture = fillTexture;
        }

        if (!hasUpdatedIconFrameSize)
        {
            nextPowerUpIcon.Hframes = GameConstants.NoOfPowerUps;
            nextPowerUpIcon.Vframes = GameConstants.noOfColours + 1;
            hasUpdatedIconFrameSize = true;
        }

        nextPowerUpIcon.Frame = (int)nextPowerUp + nextPowerUpIcon.Hframes * nextColour;
        nextPowerUpIcon.Material = nextColour == 0 ? rainbowMat : null;
    }

    public void SetVisibility(bool b)
    {
        Visible = b;
        nextPowerUpIcon.Visible = b;
    }

    public void ResetState()
    {
        fillRect.Texture = fillTexture;
        currentLevel = 0;
        isPowerUpReady = false;

        SetVisibility(PlayerGameSettings.IsUsingPowerUps);
    }
}
