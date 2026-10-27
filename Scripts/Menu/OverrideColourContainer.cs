using Godot;
using System;
using System.Collections.Generic;
using static GameConstants;
using static ColourOrderCategoryEnums;

public partial class OverrideColourContainer : Control
{
	[Export] private bool doIntialSetup;
	[Export] private Button firstButton;
	[Export] private Control themeContainer;
	[Export] private ActiveOverrideColourIcon firstActiveIcon;

	// The themes used to represent each unique colour order, shown by their sprites
	[Export] private Godot.Collections.Array<int> previewThemes;

	[ExportGroup("Resources")]
	[Export] private ThemeList themeList;
	[Export] private CommonGameSettings commonGameSettings;

	private List<Button> buttons = new List<Button>();
	private List<Sprite2D> buttonSprites = new List<Sprite2D>();
	private List<ActiveOverrideColourIcon> activeIcons = new List<ActiveOverrideColourIcon>();

	private int currentColourOrder = 0;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		if (!doIntialSetup)
			return;

		// create add buttons
		for (int i = 0; i < noOfColours; i++)
		{
			// add button ==============================================================
			Button button;
            
            if (i == 0)
                button = firstButton;
            else
            {
                button = firstButton.Duplicate() as Button;
                firstButton.GetParent().AddChild(button);
            }

			buttons.Add(button);
			buttonSprites.Add(button.GetChild<Sprite2D>(0));

			if (i == 0)
            {
                buttonSprites[i].Hframes = pillTileSetWidth;
                buttonSprites[i].Vframes = noOfColours;
            }

			buttonSprites[i].Frame = i * buttonSprites[i].Hframes + PillConstants.atlasSingle;

			int colour = i + 1;
			button.Pressed += () => ToggleColour(colour);

			// active colour icon ==============================================================
			ActiveOverrideColourIcon activeIcon;

			if (i == 0)
                activeIcon = firstActiveIcon;
            else
            {
                activeIcon = firstActiveIcon.Duplicate() as ActiveOverrideColourIcon;
                firstActiveIcon.GetParent().AddChild(activeIcon);
            }

			activeIcons.Add(activeIcon);
			Sprite2D pillSprite = activeIcons[i].PillSprite;

			if (i == 0)
            {
                pillSprite.Hframes = pillTileSetWidth;
                pillSprite.Vframes = noOfColours;
            }

			pillSprite.Frame = i * pillSprite.Hframes + PillConstants.atlasSingle;
		}

		SetColourOrder((int)themeList.GetColourOrder(commonGameSettings.CustomLevelTheme));

		for (int i = 0; i < themeContainer.GetChildren().Count; i++)
		{
			themeContainer.GetChild<Button>(i).ButtonPressed = i == currentColourOrder;
		}
	}

	// adding/removing colour ====================================================================================
	public void ToggleColour(int colour)
	{
		if (commonGameSettings.ColourOrderHasOverrideColour(currentColourOrder, colour))
		{
			RemoveOverrideColour(colour);
		}
		else
		{
			AddOverrideColour(colour);
		}
	}

	private void AddOverrideColour(int colour)
	{
		commonGameSettings.AddOverrideCustomLevelColour(currentColourOrder, colour);
		// update sprite state
		SetButtonState(colour - 1, true);
		SetActiveIconState(colour - 1, true, -1);
	}

	private void RemoveOverrideColour(int colour)
	{
		commonGameSettings.RemoveOverrideCustomLevelColour(currentColourOrder, colour);
		// update sprite state
		SetButtonState(colour - 1, false);		
		SetActiveIconState(colour - 1, false, -1);
	}

	public void ClearOverrideColours()
	{
		commonGameSettings.GetOverrideCustomLevelColours(currentColourOrder)?.Clear();
		UpdateVisuals();
	}

	// setting currentColourOrder ====================================================================================
	public void SetColourOrder(int col)
	{
		currentColourOrder = col;
		UpdateVisuals();
	}

	// visuals ====================================================================================
	public void UpdateVisuals()
	{
		Texture2D newTex = themeList.GetPillTileTexture(previewThemes[currentColourOrder]);

		for (int i = 0; i < noOfColours; i++)
		{
			// add button ==============================================================
			int colour = i + 1;
			bool active = commonGameSettings.ColourOrderHasOverrideColour(currentColourOrder, colour);
			buttonSprites[i].Texture = newTex;

			SetButtonState(i, active);

			// active colour icon ==============================================================
			activeIcons[i].Visible = false;
			activeIcons[i].PillSprite.Texture = newTex;
		}

		Godot.Collections.Array<int> overrideColours = commonGameSettings.GetOverrideCustomLevelColours(currentColourOrder);

		if (overrideColours != null)
		{
			for (int i = overrideColours.Count - 1; i >= 0; i--)
			{
				int colour = overrideColours[i];

				SetActiveIconState(colour - 1, true, 0);
			}
		}
	}

	public void SetButtonState(int index, bool active)
	{
		//buttonSprites[index].SelfModulate = new Color(1,1,1, active ? 0.25f : 1);
		buttons[index].ButtonPressed = active;
	}

	public void SetActiveIconState(int index, bool active, int position)
	{
		if (active)
		{
			activeIcons[index].Visible = true;
			activeIcons[index].SetArrowVisibility(true);
			
			if (position == -1)
			{
				position = noOfColours -1;
			}

			activeIcons[index].GetParent().MoveChild(activeIcons[index], position);
		}
		else
		{
			activeIcons[index].Visible = false;
		}
	}	
}
