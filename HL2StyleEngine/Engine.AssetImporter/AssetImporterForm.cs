namespace Engine.AssetImporter;

internal sealed class AssetImporterForm : Form
{
    private readonly ImportTabControls _modelTab = new();
    private readonly ImportTabControls _animationTab = new();

    public AssetImporterForm()
    {
        Text = "HS2 Asset Importer";
        Width = 940;
        Height = 660;
        MinimumSize = new Size(780, 540);
        StartPosition = FormStartPosition.CenterScreen;

        TabControl tabs = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Point(12, 6)
        };

        tabs.TabPages.Add(BuildImportTab(
            "Models",
            _modelTab,
            FbxImportMode.Model,
            FindDefaultModelDestinationFolder(),
            "test_pistol",
            "Convert FBX model to GLB",
            "Convert all FBX in source",
            "Tip: model import rebuilds material nodes, searches nearby texture folders, and writes static mesh GLBs. Output GLBs placed in Game/Content/Models are copied to the game on build."));

        tabs.TabPages.Add(BuildImportTab(
            "Animations",
            _animationTab,
            FbxImportMode.Animation,
            FindDefaultAnimationDestinationFolder(),
            "weapon_idle",
            "Convert FBX animation to GLB",
            "Convert all animation FBX in source",
            "Tip: animation import preserves armatures, skins, actions, and clips where Blender exposes them. Runtime playback still needs the engine animation/skinning system."));

        Controls.Add(tabs);
    }

    private TabPage BuildImportTab(
        string title,
        ImportTabControls controls,
        FbxImportMode importMode,
        string defaultDestination,
        string defaultName,
        string convertButtonText,
        string convertAllText,
        string hintText)
    {
        TabPage page = new(title);

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 7,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        page.Controls.Add(root);

        AddPathRow(root, 0, "Source folder", controls.SourceText, "Browse...", (_, _) => BrowseSourceFolder(controls, importMode));
        AddPathRow(root, 1, "Destination", controls.DestinationText, "Browse...", (_, _) => BrowseDestinationFolder(controls));
        AddTextRow(root, 2, "Output name", controls.NameText);
        AddPathRow(root, 3, "Blender exe", controls.BlenderText, "Browse...", (_, _) => BrowseBlenderExe(controls));

        controls.DestinationText.Text = defaultDestination;
        controls.BlenderText.Text = FbxToGlbConverter.ResolveBlenderExe(null) ?? "";
        controls.NameText.Text = defaultName;

        controls.ConvertButton.Text = convertButtonText;
        controls.ConvertButton.Width = 210;
        controls.ConvertButton.Height = 30;
        controls.ConvertButton.Click += async (_, _) => await ConvertAsync(controls, importMode);

        controls.ConvertAllFbxCheck.Text = convertAllText;
        controls.ConvertAllFbxCheck.AutoSize = true;
        controls.ConvertAllFbxCheck.Margin = new Padding(12, 7, 0, 0);

        FlowLayoutPanel actionRow = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        actionRow.Controls.Add(controls.ConvertButton);
        actionRow.Controls.Add(controls.ConvertAllFbxCheck);
        root.Controls.Add(actionRow, 1, 4);
        root.SetColumnSpan(actionRow, 2);

        controls.LogText.Dock = DockStyle.Fill;
        controls.LogText.Multiline = true;
        controls.LogText.ReadOnly = true;
        controls.LogText.ScrollBars = ScrollBars.Both;
        controls.LogText.WordWrap = false;
        root.Controls.Add(controls.LogText, 0, 5);
        root.SetColumnSpan(controls.LogText, 3);

        Label hint = new()
        {
            Dock = DockStyle.Fill,
            Text = hintText,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(hint, 0, 6);
        root.SetColumnSpan(hint, 3);

        return page;
    }

    private static void AddTextRow(TableLayoutPanel root, int row, string label, TextBox textBox)
    {
        root.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        textBox.Dock = DockStyle.Fill;
        root.Controls.Add(textBox, 1, row);
        root.SetColumnSpan(textBox, 2);
    }

    private static void AddPathRow(TableLayoutPanel root, int row, string label, TextBox textBox, string buttonText, EventHandler handler)
    {
        root.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        textBox.Dock = DockStyle.Fill;
        root.Controls.Add(textBox, 1, row);

        Button button = new() { Text = buttonText, Dock = DockStyle.Fill };
        button.Click += handler;
        root.Controls.Add(button, 2, row);
    }

    private void BrowseSourceFolder(ImportTabControls controls, FbxImportMode importMode)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = importMode == FbxImportMode.Animation
                ? "Choose the folder containing animation FBX files."
                : "Choose the folder containing the FBX and texture files.",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        controls.SourceText.Text = dialog.SelectedPath;
        List<string> fbxFiles = Directory
            .EnumerateFiles(dialog.SelectedPath, "*.fbx", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (fbxFiles.Count == 1)
        {
            controls.ConvertAllFbxCheck.Checked = false;
            controls.NameText.Text = Path.GetFileNameWithoutExtension(fbxFiles[0]);
        }
        else if (fbxFiles.Count > 1)
        {
            controls.ConvertAllFbxCheck.Checked = true;
            controls.NameText.Text = "";
            string kind = importMode == FbxImportMode.Animation ? "animation" : "model";
            AppendLog(controls, $"Found {fbxFiles.Count} FBX files. Batch {kind} mode enabled; output names will use each FBX file name.");
        }
    }

    private void BrowseDestinationFolder(ImportTabControls controls)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Choose where converted GLB files should be written.",
            SelectedPath = Directory.Exists(controls.DestinationText.Text) ? controls.DestinationText.Text : "",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            controls.DestinationText.Text = dialog.SelectedPath;
    }

    private void BrowseBlenderExe(ImportTabControls controls)
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Choose blender.exe",
            Filter = "Blender executable|blender.exe|Executable files|*.exe|All files|*.*"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        controls.BlenderText.Text = dialog.FileName;
        SyncBlenderPath(dialog.FileName);
    }

    private void SyncBlenderPath(string blenderPath)
    {
        _modelTab.BlenderText.Text = blenderPath;
        _animationTab.BlenderText.Text = blenderPath;
    }

    private async Task ConvertAsync(ImportTabControls controls, FbxImportMode importMode)
    {
        controls.ConvertButton.Enabled = false;
        string importName = importMode == FbxImportMode.Animation ? "animation" : "model";
        AppendLog(controls, controls.ConvertAllFbxCheck.Checked
            ? $"Starting batch {importName} conversion..."
            : $"Starting {importName} conversion...");

        try
        {
            FbxImportRequest request = new()
            {
                SourceFolder = controls.SourceText.Text.Trim(),
                DestinationFolder = controls.DestinationText.Text.Trim(),
                OutputName = controls.NameText.Text.Trim(),
                BlenderExePath = controls.BlenderText.Text.Trim(),
                ConvertAllFbx = controls.ConvertAllFbxCheck.Checked,
                ImportMode = importMode
            };

            FbxConversionResult result = await FbxToGlbConverter.ConvertAsync(request);
            AppendLog(controls, result.Log);
            if (result.Success)
            {
                AppendLog(controls, result.OutputPaths.Count > 1
                    ? $"Ready: {result.OutputPaths.Count} GLB files in {request.DestinationFolder}"
                    : $"Ready: {result.OutputPath}");
            }
            else
            {
                AppendLog(controls, result.OutputPaths.Count > 0
                    ? $"Conversion finished with errors. Produced {result.OutputPaths.Count} GLB files."
                    : "Conversion failed.");
            }
        }
        finally
        {
            controls.ConvertButton.Enabled = true;
        }
    }

    private static void AppendLog(ImportTabControls controls, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        controls.LogText.AppendText(message.TrimEnd() + Environment.NewLine + Environment.NewLine);
    }

    private static string FindDefaultModelDestinationFolder()
        => FindDefaultContentFolder("Models", "ViewModels");

    private static string FindDefaultAnimationDestinationFolder()
        => FindDefaultContentFolder("Animations");

    private static string FindDefaultContentFolder(params string[] contentSegments)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Game", "Game.csproj");
            if (File.Exists(candidate))
            {
                string[] segments = [dir.FullName, "Game", "Content", .. contentSegments];
                return Path.Combine(segments);
            }

            dir = dir.Parent;
        }

        string[] fallbackSegments = [AppContext.BaseDirectory, "Content", .. contentSegments];
        return Path.Combine(fallbackSegments);
    }

    private sealed class ImportTabControls
    {
        public TextBox SourceText { get; } = new();
        public TextBox DestinationText { get; } = new();
        public TextBox NameText { get; } = new();
        public TextBox BlenderText { get; } = new();
        public TextBox LogText { get; } = new();
        public Button ConvertButton { get; } = new();
        public CheckBox ConvertAllFbxCheck { get; } = new();
    }
}