using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using static PowerUpEnums;
using static PillEnums;
using System.Collections.Generic;

// Handles movement, rotation and activation of a player's falling pill
public partial class PillManager : Node
{

	[ExportGroup("Pills")]
	[Export] private PillActive activePill;
	[Export] private Pill nextPill;
	[Export] private Pill holdPill;
	[Export] private Pill powerUpPill;

	[ExportGroup("Movement")]
	[Export] private float initialLowFallSpeed;
	[Export] private float initialMedFallSpeed;
	[Export] private float initialHiFallSpeed;
	private float fallSpeed;
	[Export] private float softDropSpeed;
	[Export] private int pillsToIncreaseFallSpeed;
	[Export] private float fallSpeedIncreaseAmount;

	[ExportGroup("Locking")]
	[Export] private float initialLockSpeed;
	private float lockSpeed;

	[ExportGroup("Resources")]
	[Export] private ThemeList themeList;
	private PlayerGameSettings PlayerGameSettings { get { return jarMan.PlayerGameSettings; } }
	private CommonGameSettings CommonGameSettings { get { return jarMan.CommonGameSettings; } }

	[ExportGroup("References")]
	[Export] private JarManager jarMan;
	[Export] private PowerUpMeter powerUpMeter;
	public SfxManager SfxMan { get; set; }
	private JarMario Mario { get { return jarMan.UIMan.Mario; } }

	private int PlayerID { get { return jarMan.PlayerID; } }

	public float SoftDropSpeed { get { return softDropSpeed; } }
	public Pill ActivePill { get { return activePill; } }

	private enum pillStates { Controlling, Throwing }
	private pillStates currentState = pillStates.Throwing;
	public bool IsThrowingPill { get { return currentState == pillStates.Throwing; } }

	// Throw-related

	// Value between 0 to 1 that represents how far the pill is in the throwing animation (0 = still in mario's hand, 1 = just landed in jar))
	private float throwProgress = 0;
	// Same as above 
	private float throwSpinProgress = 0;

	// The max height offset the pill reaches at its peak when being thrown
	private float throwHeight = 44;

	// Speed of pill throw / how quickly throwProgress reaches 1
	private float throwSpeed;
	// How fast the pill rotates while being thrown
	private float throwRotateSpeed;
	// Start and ending positions of a pill throw
	private Vector2 throwStartPos;
	private Vector2 throwEndPos;
	private Vector2 throwHoldStartPos;
	private Vector2 throwHoldEndPos;
	// The pill being thrown
	private Pill throwingPill;
	// The second pill being thrown
	private Pill throwingPill2 = null;

	// Timers
	private double fallTimer = 1;
	private double moveTimer = 1;
	private double softDropTimer = 0;
	private double lockTimer = 1;
	private int lockResets = 0;

	// Tilemap-related
	private Vector2I tileSize;
	private int jarWidth;

	// Other
	private Vector2I startGridPos;
	public Vector2I StartGridPos { get { return startGridPos; } }
	private bool wasHoldUsed = false;
	private bool debugMode = false;
	private bool canSoftDrop = true;

	// Read by training bots so they can plan around the next and held pills
	public Pill NextPill { get { return nextPill; } }
	public Pill HoldPill { get { return holdPill; } }
	public bool IsHoldFilled { get { return holdPill.Visible; } }
	public bool CanHold { get { return PlayerGameSettings.IsHoldEnabled && !wasHoldUsed; } }
	// Training bots always have hard drop, since it's one of their controls
	private bool IsHardDropEnabled { get { return CommonGameSettings.IsHardDropEnabled || jarMan.Bot != null; } }

	// No. of pills used (including ones used from the hold slot and power-ups)
	private int pillsUsed = 0;
	public int PillsUsed { get { return pillsUsed; } }
	// No. of pills used - ONLY counting ones thrown from the "next" slot
	private int nextPillsUsed = 0;
	private bool canUseDebugMode = false;

	// if the to-be active pill has been thrown but something (avalanche) is holding it up from falling/becoming controllable
    private bool isPillWaitingToFall = false;
    public bool IsPillWaitingToFall { get { return isPillWaitingToFall; } }
	
	private bool IsActionJustPressed(string action)
	{
		return jarMan.IsActionJustPressed(action);
	}

	private bool IsActionPressed(string action)
	{
		return jarMan.IsActionPressed(action);
	}

	private bool readyCalled = false;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		if (readyCalled)
			return;
			
        // Get tile size and jar's width
        tileSize = jarMan.JarCellSize;
		jarWidth = jarMan.JarSize.X;

		// Get the starting position in jar grid tile units (top-middle of jar)
		startGridPos = jarMan.JarOrigin + Vector2I.Right * Mathf.Clamp(jarWidth / 2 - 1, 0, jarWidth);
		// Set activePill's grid position to startGridPos
		activePill.GridPos = startGridPos;

		// Update activePill's actual posiiton based on its GridPos 
		UpdateActivePillPosition();

		// Set throwEndPos to activePill's current position (based on startGridPos)
		throwEndPos = activePill.Position;

		SetProcess(false);

		// Reset various speeds, states, etc
		ResetState();

		if (jarMan.PossiblePillColours.Count == 0)
			nextPill.Visible = false;

		readyCalled = true;
	}

	public void ResetState()
	{
		activePill.Visible = false;
		nextPill.Visible = true;
		holdPill.Visible = false;
		powerUpPill.Visible = false;

		activePill.ResetState();
		nextPill.ResetState();
		holdPill.ResetState();

		pillsUsed = 0;
		nextPillsUsed = 0;
        isPillWaitingToFall = false;

        if (PlayerGameSettings.SpeedLevel == 2)
            fallSpeed = initialHiFallSpeed;
        else if (PlayerGameSettings.SpeedLevel == 0)
            fallSpeed = initialLowFallSpeed;
        else
            fallSpeed = initialMedFallSpeed;

		lockSpeed = initialLockSpeed;

		if (PlayerGameSettings.UseFallSpeedAsLockSpeed || fallSpeed < lockSpeed)
			lockSpeed = fallSpeed;

		softDropSpeed = PlayerGameSettings.FasterSoftDrop ? 30 : 20;

		// If player's fall speed level is LOW, decrease throwSpeed and throwRotateSpeed
		bool slowThrow = PlayerGameSettings.SpeedLevel == 0;

		throwSpeed = 60.0f / (slowThrow ? 42.0f : 21.0f);
		throwRotateSpeed = 60.0f / (slowThrow ? 8.0f : 4.0f);
    }

	public void InitialiseNextPillVariables()
	{
        nextPill.InitialiseVariables();
    }

	public void RandomiseNextPillColours()
	{
		if (!nextPill.Visible)
			nextPill.Visible = true;

		// Get next pill shape using possibleShapes from PlayerGameSettings
        PillShape nextShape;
        List<PillShape> possibleShapes = PlayerGameSettings.AvailablePillShapes;

		// Fall back to double (2x1) if possibleShapes is empty
        if (possibleShapes.Count == 0)
            nextShape = PillShape.Double;
		// Choose pill shape based on how many pills have been thrown from the "next" slot
		else
            nextShape = possibleShapes[nextPillsUsed % possibleShapes.Count];

        // Randomise next pill colours with given shape
        nextPill.SetRandomPillColours(jarMan.PossiblePillColours, PlayerGameSettings.OnlySingleColourPills, PillType.Regular, nextShape, jarMan.PillRng);
	}

	private void ResetAllTimersAndResets()
	{
		fallTimer = 1;
		softDropTimer = 1;
		lockTimer = 1;
		lockResets = 0;
	}

	private void Hold()
	{
		// if marathon/endless mode, do push-up countdown, but don't cause any falling to happen if it needs to immediately
		if (CommonGameSettings.GameMode == 1)
        	jarMan.ReducePushUpCountdown(true);

        ThrowNextPill(holdPill.Visible, true);

		if (activePill.IsPowerUp)
			activePill.SetPowerUpPreviewVisibility(false);

		wasHoldUsed = true;

		activePill.Visible = true;

		jarMan.ClearPreviewTiles();

		SfxMan.Play("Hold");
	}

	public void HideThrowingPills()
	{
		if (throwingPill != null)
			throwingPill.Visible = false;
		if (throwingPill2 != null)
			throwingPill2.Visible = false;
	}

	public void HideNextPill()
	{
		if (nextPill != null)
			nextPill.Visible = false;
	}

	public void HideAllPills()
	{
		if (activePill != null)
			activePill.Visible = false;
			
		if (nextPill != null)
			nextPill.Visible = false;

		if (holdPill != null)
			holdPill.Visible = false;

		if (powerUpPill != null)
			powerUpPill.Visible = false;
	}

	// usingHoldPill = the pill being thrown into the jar was the pill in the hold reserve
	// justHeld = if the player just put a pill into the hold reserve
	public void ThrowNextPill(bool usingHoldPill = false, bool justHeld = false)
	{
		// If calling before _Ready has been performed, do _Ready now
		if (!readyCalled)
			_Ready();

		if (!CommonGameSettings.IsMultiplayer && !usingHoldPill && !powerUpMeter.IsPowerUpReady && !jarMan.IsPlayerOut)
			Mario.PlayAnimation("Throw");

		// If power-up is ready and not using a held pill, throw and set powerUpPill then queue a new power-ups
		if (powerUpMeter.IsPowerUpReady && !usingHoldPill)
		{
			powerUpPill.Visible = true;
            powerUpPill.SetPowerUp(powerUpMeter.NextPowerUp, powerUpMeter.NextColour);
            powerUpMeter.QueueNewPowerUp();

			throwingPill = powerUpPill;
		}
		else
			throwingPill = usingHoldPill ? holdPill : nextPill;

		// If player didn't hold and the player has powerups enabled and isn't their first pill, increment power-up meter level
		if (!justHeld && PlayerGameSettings.IsUsingPowerUps && pillsUsed != 0)
			powerUpMeter.IncrementLevel(1);

		if (justHeld)
		{
            throwHoldEndPos = holdPill.OrigPos + holdPill.GetOrigPosOffset((int)activePill.PillShape);

			throwingPill2 = activePill;
			throwingPill2.SetRotation(0);
		}
		else
		{
			throwingPill2 = null;
			wasHoldUsed = false;
		}

        // Set throwing pill(s) to vertical
        throwingPill.SetRotation(1);

        if (wasHoldUsed)
		{
			throwingPill2.SetRotation(1);
		}

		throwStartPos = throwingPill.Position;
		throwHoldStartPos = activePill.Position;
		currentState = pillStates.Throwing;
		throwProgress = 0;
		throwSpinProgress = 0;

		SetProcess(true);
	}

	public void StartFallingAfterWait()
	{
        isPillWaitingToFall = false;
        SetProcess(true);

		if (activePill.IsPowerUp)
			SfxMan.Play("PowerUpGet");
    }

    private void ActivateNextPill()
	{
        pillsUsed++;

		if (!CommonGameSettings.IsMultiplayer && !jarMan.IsPlayerOut)
			Mario.ResetFrame();

		// if holding left or right, auto set moveTimer to zero so it starts auto repeat right away
		if (IsActionPressed("MoveLeft") || IsActionPressed("MoveRight"))
			moveTimer = 0;

		// increase fall speed 
		if (!PlayerGameSettings.NoFallSpeedIncrease && pillsUsed % pillsToIncreaseFallSpeed == 0 && fallSpeed < softDropSpeed)
		{
			fallSpeed += fallSpeedIncreaseAmount;
			
			SfxMan.Play("SpeedUp");

			if (fallSpeed > softDropSpeed)
				fallSpeed = softDropSpeed;

			if (PlayerGameSettings.UseFallSpeedAsLockSpeed)
				lockSpeed = fallSpeed;
			else if (lockSpeed < initialLockSpeed)
			{
				if (fallSpeed < initialLockSpeed)
					lockSpeed = fallSpeed;
				else
					lockSpeed = initialLockSpeed;
			}
		}
		
		// If thrown pill went into hold reserve and the reserve was previously empty, show the pill in the hold reserve
		if (!holdPill.Visible && wasHoldUsed)
			holdPill.Visible = true;
		
		// If power-up pill was visisble, hide it and reset its state
		if (powerUpPill.Visible)
		{
			powerUpPill.Visible = false;
			powerUpPill.ResetState();
		}

        // Store old active pill state before changing it
        PillAttributes activePillAttributes = activePill.GetAttributes();

        // Set active pill to match the pill that was thrown
        activePill.SetAttributes(throwingPill.GetAttributes());

		// If hold was used, set the hold pill to match the last active pill
		if (wasHoldUsed)
		{
			holdPill.SetAttributes(activePillAttributes);
            holdPill.SetRotation(0);
			holdPill.ResetScale();
        }

		// Reset activePill's rotation and grid position
		activePill.SetRotation(0);
		activePill.GridPos = startGridPos;

		// If pill thrown was the pill thrown from mario, randomise its segments
		if (throwingPill == nextPill)
		{
            nextPillsUsed++;
			RandomiseNextPillColours();
        }

		ResetAllTimersAndResets();

		activePill.Visible = true;

		// disable, hide pills and return if player is out
		if (jarMan.IsPlayerOut)
		{
            activePill.Visible = false;
            nextPill.Visible = false;
            SetProcess(false);
            return;
        }
		// Check for hazards, if found disable processing to signal to place the pill right away (if found here, the player's gonna be in a fun loop)
		if (ArePillTilesTouchingHazard(activePill.GridPos))
		{
            SetProcess(false);
			AddPillToTilemap(activePill);
        }
		// Check if the active pill isn't blocked by anything - proceed if clear, otherwise do a game over
		else if (AreTargetPosCellsFree(activePill.GridPos))
		{
			currentState = pillStates.Controlling;
			UpdateActivePillPosition();

            if (jarMan.DoTilesToFallExist)
			{
                isPillWaitingToFall = true;
				SetProcess(false);
				jarMan.SetProcess(true);
			}
			else
			{
                SetProcess(true);

				if (activePill.IsPowerUp)
					SfxMan.Play("PowerUpGet");
			}
		}
		else
		{
			UpdateActivePillPosition();
			SetProcess(false);
			jarMan.GameOver();
		}
	}

	private void UpdateActivePillPosition()
	{
		activePill.GlobalPosition = jarMan.TilemapGlobalPos + activePill.GridPos * tileSize;
		activePill.GlobalPosition += tileSize / 2;

		if (currentState == pillStates.Controlling)
		{
			UpdateGhostPillPreview();

			if (activePill.IsPowerUp && activePill.PowerUpHasPreview)
            {
				if (activePill.LandPos != activePill.GridPos)
				{
					Vector2 ghostPos;
					ghostPos = jarMan.TilemapGlobalPos + activePill.LandPos * tileSize;
					ghostPos += tileSize / 2;
					
					activePill.SetGhostPowerUpPreviewGlobalPosition(ghostPos);
				}

				activePill.UpdateGhostPowerUpPreviewVisibility();
            }
		}
	}
	private void UpdateGhostPillPreview()
	{
		if (!CommonGameSettings.IsGhostPillEnabled)
			return;

		jarMan.ClearPreviewTiles();

		int downwardSteps = 0;
		
		Vector2I downPos = activePill.GridPos + Vector2I.Down;

		while (!DoesTargetPosBlockDrop(downPos))
		{
			downwardSteps++;
			downPos += Vector2I.Down;
		}

		Vector2I ghostPos = downPos + Vector2I.Up;

		if (downwardSteps > 0)
		{
			jarMan.CreateGhostPill(activePill, ghostPos);
		}

		activePill.LandPos = ghostPos;
	}

	private bool Rotate(int dir)
	{
		// if pill only has one position, it doesnt need to be rotated
		if (activePill.UnrotatedTiles.Count() == 1)
			return false;

		// the potential new rotation for if the pill successfully rotates
        int potentialRotation = activePill.PillRotation + dir;
		if (potentialRotation > 3)
            potentialRotation -= 4;
		else if (potentialRotation < 0)
            potentialRotation += 4;

        Vector2I targetPos = activePill.GridPos;

		// hazard check pre-kicks, if found disable processing to signal to place the pill right away
		if (ArePillTilesTouchingHazard(targetPos, potentialRotation))
			SetProcess(false);

		// kicks - set targetPos and rotation based on kick result
        Vector3I kickResult = PillKicks.DoKickChecks(dir, activePill, jarMan);
        targetPos = new Vector2I(kickResult.X, kickResult.Y);

        // return false is rotation could occur
        if (kickResult.Z == activePill.PillRotation)
            return false;

		// update activePill's rotation 
        activePill.SetRotation(kickResult.Z);

		// update 's GridPos to targetPos and do UpdateActivePillPosition
        if (activePill.GridPos != targetPos)
		{
			activePill.GridPos = targetPos;
			UpdateActivePillPosition();
		}
		// if targetPos hasn't changed from activePill.GridPos, just update ghost preview
		else
		{
			UpdateGhostPillPreview();
		}

		// hazard check post-kicks, if found disable processing to signal to place the pill right away
		if (ArePillTilesTouchingHazard(targetPos))
			SetProcess(false);

		return true;
	}

	private bool Move(Vector2I dir, bool bypassCollisions = false)
	{
		Vector2I targetPos = activePill.GridPos + dir;

        bool areCellsFree = AreTargetPosCellsFree(targetPos);

        if (bypassCollisions || areCellsFree)
		{
			activePill.GridPos = targetPos;
			UpdateActivePillPosition();

			// check for hazards, if found disable processing to signal to place the pill right away
			if (ArePillTilesTouchingHazard(targetPos))
				SetProcess(false);
				
			return true;
		}

		return false;
	}

	private void HardDrop()
	{
		int downwardSteps = 0;

		Vector2I downPos = activePill.GridPos + Vector2I.Down;

		while (!DoesTargetPosBlockDrop(downPos))
		{
			downwardSteps++;
			downPos += Vector2I.Down;
		}

		activePill.GridPos = downPos + Vector2I.Up;
		UpdateActivePillPosition();
	}

	private bool IsGrounded()
	{
        return !AreTargetPosCellsFree(activePill.GridPos + Vector2I.Down);
	}

	// Whether or not the active pill's tiles relative to targetPos are already occupied any existing cells
	// rotation -1 = just use current rotation
	private bool AreTargetPosCellsFree(Vector2I targetPos, int newRotation = -1)
	{
		bool areCellsFree = true;

        Vector2I[] poses = newRotation == -1 ? activePill.RotatedTiles.Keys.ToArray() : activePill.GetRotatedTilePositions(newRotation);

        foreach (Vector2I tilePos in poses)
		{
            if (!jarMan.IsCellFree(targetPos + tilePos))
			{
                areCellsFree = false;
                break;
            }
        }

        return areCellsFree;
    }

	// Whether or not the active pill's tiles relative to targetPos are already occupied any existing cells
	// rotation -1 = just use current rotation
	private bool ArePillTilesTouchingHazard(Vector2I targetPos, int newRotation = -1)
	{
		bool touchingHazard = false;

        Vector2I[] poses = newRotation == -1 ? activePill.RotatedTiles.Keys.ToArray() : activePill.GetRotatedTilePositions(newRotation);

        foreach (Vector2I tilePos in poses)
		{
            if (jarMan.IsTileHazard(targetPos + tilePos))
			{
                touchingHazard = true;
                break;
            }
        }

        return touchingHazard;
    }

	private bool DoesTargetPosBlockDrop(Vector2I targetPos)
	{
		return !AreTargetPosCellsFree(targetPos) || ArePillTilesTouchingHazard(targetPos + Vector2I.Up);
	}


	private void UpdateTimers(double delta)
	{
		bool gnd = IsGrounded();

		if (!gnd)
			fallTimer -= delta * fallSpeed;

		if (softDropTimer > 0)
			softDropTimer -= delta * softDropSpeed;

		if (gnd || lockTimer < 1)
			lockTimer -= delta * lockSpeed;
		
		if (IsActionPressed("MoveLeft") || IsActionPressed("MoveRight"))
			moveTimer -= delta * PlayerGameSettings.FirstMoveSpeed;
		else
			moveTimer = 1;
	}

	private void UpdateTimersOnGroundedAdjustment()
	{
		softDropTimer = 1;
		fallTimer = 1;
		if (lockResets < PlayerGameSettings.MaxLockResets)
		{
			lockTimer = 1;
			lockResets++;
		}
	}

	// Updates throwing pill movement (uncontrollable)
	private void TickThrow(double dt)
	{
		throwProgress += throwSpeed * (float)dt;
		throwSpinProgress += throwRotateSpeed * (float)dt;

		if (throwProgress >= 1)
		{
			throwingPill.Position = throwStartPos;
            throwingPill.SetRotation(0);

			ActivateNextPill();

            throwingPill.ResetScale();

            if (throwingPill2 != null)
			{
            	throwingPill2.ResetScale();
			}

			return;
		}

		// Set thrown pill position along an upwards curve depending on throwProgress
		Vector2 newPos;

		newPos = throwStartPos * (1 - throwProgress) + throwEndPos * throwProgress;
		newPos.Y -= throwHeight * Mathf.Sin(throwProgress * Mathf.Pi);

		throwingPill.Position = newPos;
        throwingPill.InterpolatedScale(1.0f - throwProgress);

        // If throwingPill2 exists, move it into hold slot
        if (throwingPill2 != null)
		{
			// Set throwingPill2 position along a downwards curve depending on throwProgress
			Vector2 newPos2;
            newPos2 = throwHoldStartPos * (1 - throwProgress) + throwHoldEndPos * throwProgress;
			newPos2.Y += throwHeight * Mathf.Sin(throwProgress * Mathf.Pi);

			throwingPill2.Position = newPos2;
        	throwingPill2.InterpolatedScale(throwProgress);
        }

		// Rotate pill after throwRotateRate intervals
		if (throwSpinProgress >= 1)
		{
			throwSpinProgress -= 1;

            throwingPill.Rotate(true);

            if (throwingPill2 != null)
			{
				throwingPill2.Rotate(true);
			}
		}
	}

	// Updates controllable pill movement
	private void TickControl(double dt)
	{
		// training bots decide which buttons to press this frame, then the pill reacts to them like any other player's input
		if (jarMan.Bot != null)
		{
			jarMan.Bot.UpdateControls(dt);

			// the bot can end its game early (pill limit)
			if (jarMan.IsPlayerOut)
				return;
		}

		if (IsActionJustPressed("Hold") && PlayerGameSettings.IsHoldEnabled)
		{
			if (!wasHoldUsed)
			{
				Hold();
				return;
			}
		}

		UpdateTimers(dt);

		// left/right movement
		if (!(IsActionPressed("MoveLeft") && IsActionPressed("MoveRight")))
		{
			if (IsActionJustPressed("MoveLeft") || (moveTimer <= 0 && IsActionPressed("MoveLeft")))
			{
				if (Move(Vector2I.Left))
				{
					SfxMan.Play("Move");

					if (IsGrounded())
						UpdateTimersOnGroundedAdjustment();

					if (IsActionJustPressed("MoveLeft"))
						moveTimer = 1;
					else
						moveTimer = 1.0f * (PlayerGameSettings.FirstMoveSpeed / PlayerGameSettings.RepeatedMoveSpeed);
				}
				else if (moveTimer > 0)
                    moveTimer = 0;

			}
			if (IsActionJustPressed("MoveRight") || (moveTimer <= 0 && IsActionPressed("MoveRight")))
			{
				if (Move(Vector2I.Right))
				{
					SfxMan.Play("Move");

					if (IsGrounded())
						UpdateTimersOnGroundedAdjustment();

					if (IsActionJustPressed("MoveRight"))
						moveTimer = 1;
					else
						moveTimer = 1.0f * (PlayerGameSettings.FirstMoveSpeed / PlayerGameSettings.RepeatedMoveSpeed);
				}
				else if (moveTimer > 0)
                    moveTimer = 0;

            }
		}

		// if processing has stopped (due to a hazard hit), place pill then return
		if (!IsProcessing())
		{
			AddPillToTilemap(activePill);
			return;
		}

		// rotation
		if (!(IsActionPressed("RotateLeft") && IsActionPressed("RotateRight")))
		{
			if (IsActionJustPressed("RotateLeft"))
			{
				bool gnd = IsGrounded();
				int origY = activePill.GridPos.Y;
				if (Rotate(-1))
				{
					SfxMan.Play("Rotate");

					if (gnd)
					{
						UpdateTimersOnGroundedAdjustment();

						// if y position has changed after rotation, reset locking
						if (origY != activePill.GridPos.Y)
						{
							lockTimer = 1;
							lockResets = 0;
						}
					}
				}
			}
			if (IsActionJustPressed("RotateRight"))
			{
				bool gnd = IsGrounded();
				int origY = activePill.GridPos.Y;
				if (Rotate(1))
				{
					SfxMan.Play("Rotate");
					
					if (gnd)
					{
						UpdateTimersOnGroundedAdjustment();

						// if y position has changed after rotation, reset locking
						if (origY != activePill.GridPos.Y)
						{
							lockTimer = 1;
							lockResets = 0;
						}
					}
				}
			}
		}

		// if processing has stopped (due to a hazard hit), place pill then return
		if (!IsProcessing())
		{
			AddPillToTilemap(activePill);
			return;
		}

		// gravity and dropping
		
		// place pill into tilemap once locking finishes, hard-dropped, or (started soft-droping or soft-dropping with InstantSoftDropLock) while grounded 
		if (((lockTimer <= 0 /* || IsActionJustPressed("SoftDrop") */ || (PlayerGameSettings.InstantSoftDropLock && IsActionPressed("SoftDrop"))) && IsGrounded()) || (IsActionJustPressed("HardDrop") && IsHardDropEnabled))
		{
			if (IsActionJustPressed("HardDrop") && IsHardDropEnabled)
				HardDrop();

			AddPillToTilemap(activePill);
			return;
		}
		// dropp down either by soft drop or gravity
		else if ((IsActionPressed("SoftDrop") && softDropTimer <= 0 && canSoftDrop) || fallTimer <= 0)
		{
			if (Move(Vector2I.Down))
				ResetAllTimersAndResets();
		}
		else if (!canSoftDrop && !IsActionPressed("SoftDrop"))
		{
			canSoftDrop = true;
		}

		// if processing has stopped (due to a hazard hit), place pill then return
		if (!IsProcessing())
		{
			AddPillToTilemap(activePill);
			return;
		}
	}

	private void AddPillToTilemap(Pill pill)
	{
		SetProcess(false);
		jarMan.ClearPreviewTiles();
		jarMan.AddPillToTilemap(pill);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
        if (canUseDebugMode && Input.IsActionJustPressed("ToggleDebugMode"))
			debugMode = !debugMode;

		if (debugMode)
		{
			if (IsActionJustPressed("MoveLeft"))
				Move(Vector2I.Left, true);
			if (IsActionJustPressed("MoveRight"))
				Move(Vector2I.Right, true);
			if (IsActionJustPressed("MoveUp"))
				Move(Vector2I.Up, true);
			if (IsActionJustPressed("MoveDown"))
				Move(Vector2I.Down, true);

			if (IsActionJustPressed("RotateLeft"))
				Rotate(-1);
			if (IsActionJustPressed("RotateRight") && !IsActionJustPressed("MoveUp"))
				Rotate(1);

			return;
		}

		if (currentState == pillStates.Throwing)
			TickThrow((float)delta);
		else
			TickControl((float)delta);
    }
}
