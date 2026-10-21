using Godot;
using System;
using System.Collections.Generic;
using static PowerUpEnums;

public partial class EditorColourSelector : EditorBaseTileSelector
{
    [Export] protected EditorTileTypeSelector tileSelector;

    // Called when the node enters the scene tree for the first time.
	public override void _Ready()
    {
        // No. of colours
        buttonCount = 10;
        base._Ready();

        UpdateVisuals();
    }

    public override void UpdateVisuals()
    {
        if (cursor.TileType == 3)
        {
            Visible = false;
            return;
        }

        Visible = true;

        for (int i = 0; i < buttons.Count; i++)
        {
            // tile buttonSprites[i]s
            
            if (buttonSprites[i].Texture != cursor.GetTileTypeTexture(cursor.TileType))
            {
                buttonSprites[i].Frame = 0;
                buttonSprites[i].Texture = cursor.GetTileTypeTexture(cursor.TileType);

                buttonSprites[i].Hframes = cursor.GetTileTypeAtlasSize(cursor.TileType).X;
                buttonSprites[i].Vframes = cursor.GetTileTypeAtlasSize(cursor.TileType).Y;
            }

            // object
            if (cursor.TileType == 3)
            {
                buttonSprites[i].Frame = buttonSprites[i].Hframes * cursor.TileID;
            }
            // power-up
            else if (cursor.TileType == 2)
            {
                buttonSprites[i].Frame = buttonSprites[i].Hframes * (i + 1) + cursor.TileID;
            }
            // pill/virus
            else
            {
                buttonSprites[i].Frame = buttonSprites[i].Hframes * i;

                if (cursor.TileType == 0)
                    buttonSprites[i].Frame += 4;
            }
        }
    }

    public override void PressButton(int index)
    {
        base.PressButton(index);

        cursor.SetCursorColour(index + 1);

        tileSelector.UpdateVisuals();
    }
}
