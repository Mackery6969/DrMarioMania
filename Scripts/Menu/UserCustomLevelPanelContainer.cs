using Godot;
using System;
using System.Collections.Generic;

public partial class UserCustomLevelPanelContainer : ScrollContainer
{
    // Container/list of user-made/imported custom level panels
    
    [Export] private PackedScene customLevelPanelPrefab;

    [ExportGroup("Local References")]
    [Export] private Label pageNumberLabel;
    [Export] private Button createButton;

    [ExportGroup("External References & Resources")]
    [Export] private NotificationBox notificationBox;
    [Export] private MenuManager menuMan;
    [Export] private UserCustomLevelList levelList;
    [Export] private CommonGameSettings commonGameSettings;
    private const int levelsPerPage = 10;
    private int currentPage = 0;
    private List<UserCustomLevelEntry> Levels { get { return levelList.CustomLevels; } }
    private int PageCount
    {
        get 
        {
            if (Levels.Count == 0)
                return 1;
            
            return ((Levels.Count - 1) / levelsPerPage) + 1;
        }
    }

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        currentPage = commonGameSettings.LastUserCustomLevelPage;
        UpdateCurrentPage();
    }

    public void UpdateVisuals()
    {
        createButton.Visible = !commonGameSettings.IsMultiplayer;
        UpdateCurrentPage();
    }

    public void ImportLevel()
    {
        string code = DisplayServer.ClipboardGet();

        bool attemptedCode = code.Length > 16;

        bool skipImport = false;

        if (attemptedCode)
        {
            int codeVer = -1;
            bool parsed = int.TryParse(code[0].ToString(), out codeVer);

            if (!parsed)
                skipImport = true;
            else if (codeVer > GameConstants.levelCodeVer)
            {
                notificationBox.ShowMessage("This level requires a newer version of the game!", 3000);
                return;
            }
        }

        bool success = false;

        if (!skipImport)
            success = attemptedCode && levelList.ImportLevel(code);

        if (success)
        {
            notificationBox.ShowMessage("Level imported!");
            currentPage = 0;
            UpdateCurrentPage();
        }
        else
        {
            if (attemptedCode)
                notificationBox.ShowMessage("Import failed :(\nHave you copied a level code?", 3000);
            else
                notificationBox.ShowMessage("To import, copy a level code to clipboard then try again.", 3000);
        }
    }

    public void NextPage()
    {
        if (currentPage > PageCount - 2)
            return;

        currentPage++;
        UpdateCurrentPage();
    }

    public void PreviousPage()
    {
        if (currentPage < 1)
            return;
        
        currentPage--;
        UpdateCurrentPage();
    }

    public void UpdateCurrentPage()
    {
        // reset v scroll position
        ScrollVertical = 0;

        // keep page in range (e.g. after deleting the only level on the last page)
        currentPage = Math.Clamp(currentPage, 0, PageCount - 1);

        // delete old panels
        foreach (Node node in GetChild(0).GetChildren())
        {
            node.QueueFree();
        }

        UserCustomLevelPanel prevPanel = null;

        // make new panels
        for (int i = 0; i < levelsPerPage; i++)
        {
            int levelIndex = currentPage * levelsPerPage + i;

            if (levelIndex > Levels.Count - 1)
                break;

            UserCustomLevelPanel levelPanel = customLevelPanelPrefab.Instantiate<UserCustomLevelPanel>();
            GetChild(0).AddChild(levelPanel);

            levelPanel.LevelContainer = this;
            levelPanel.NotificationBox = notificationBox;
            levelPanel.SetLevelDetails(levelIndex, Levels[levelIndex]);

			levelPanel.PlayButton.Pressed += () => menuMan.StartGame(levelIndex, false);
			levelPanel.EditButton.Pressed += () => menuMan.StartEditor(levelIndex);

            if (prevPanel != null)
			{
				levelPanel.SetAboveNeighbour(prevPanel);
				prevPanel.SetBelowNeighbour(levelPanel);
			}

			prevPanel = levelPanel;
        }

        if (commonGameSettings.LastUserCustomLevelPage != currentPage)
			commonGameSettings.LastUserCustomLevelPage = currentPage;

        if (pageNumberLabel != null)
            pageNumberLabel.Text = (currentPage + 1) + "/" + PageCount;
    }
}
