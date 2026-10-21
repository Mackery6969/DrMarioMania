using Godot;
using System;

public partial class EditorBaseTileSelector : EditorBaseInputSelector
{
    [ExportGroup("Textures")]
    [Export] protected Texture2D pillTexture;
    [Export] protected Texture2D virusTexture;
    [Export] protected Texture2D powerUpTexture;
    [Export] protected Texture2D objectTexture;

    [ExportGroup("References")]
    [Export] protected Button firstButton;
    [Export] protected EditorCursor cursor;
    [Export] protected EditorBaseSelector drawingToolSelector;
    
    public Texture2D PillTexture
    {
        set
        {
            ReplaceButtonTexture(pillTexture, value);
            pillTexture = value;
        }
    }
    public Texture2D VirusTexture
    {
        set
        {
            ReplaceButtonTexture(virusTexture, value);
            virusTexture = value;
        }
    }
    public Texture2D PowerUpTexture
    {
        set
        {
            ReplaceButtonTexture(powerUpTexture, value);
            powerUpTexture = value;
        }
    }
    public Texture2D ObjectTexture
    {
        set
        {
            ReplaceButtonTexture(objectTexture, value);
            objectTexture = value;
        }
    }

    private void ReplaceButtonTexture(Texture2D oldTexture, Texture2D newTexture)
    {
        foreach (Sprite2D sprite in buttonSprites)
        {
            if (sprite.Texture == oldTexture)
                sprite.Texture = newTexture;
        }
    }

    protected Vector2I pillAtlasSize;
    protected Vector2I virusAtlasSize;
    protected Vector2I powerUpAtlasSize;
    protected Vector2I objectAtlasSize;

    // Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
        pillAtlasSize = cursor.GetTileTypeAtlasSize(0);
        virusAtlasSize = cursor.GetTileTypeAtlasSize(1);
        powerUpAtlasSize = cursor.GetTileTypeAtlasSize(2);
        objectAtlasSize = cursor.GetTileTypeAtlasSize(3);

        for (int i = 0; i < buttonCount; i++)
        {
            Button newButton;
            
            if (i == 0)
            {
                newButton = firstButton;
            }
            else
            {
                newButton = firstButton.Duplicate() as Button;
                AddChild(newButton);
                newButton.ButtonPressed = false;
            }

            buttons.Add(newButton);
            buttonSprites.Add(newButton.GetChild<Sprite2D>(0));
            shiftedSprites.Add(newButton.ButtonPressed);

            int index = i;
            newButton.Pressed += () => PressButton(index);
            newButton.Pressed += () => drawingToolSelector.PressButton(0);
            newButton.Pressed += () =>  cursor.SetDrawingTool(0);

            newButton.ButtonDown += () => ButtonDown(index);
            newButton.ButtonUp += () => ButtonUp(index);
        }
        
        buttonSprites[0].Position += Vector2.Down;
    }

    protected override void Previous()
    {
        if (editorMan.CanPressButtons)
            PressButton((selectedButton - 1 + buttons.Count) % buttons.Count);
    }

    protected override void Next()
    {
        if (editorMan.CanPressButtons)
            PressButton((selectedButton + 1) % buttons.Count);
    }        
}
