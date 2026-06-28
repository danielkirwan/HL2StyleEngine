namespace Engine.AssetImporter;

internal sealed class AssetImporterForm : Form
{
    private readonly TextBox _sourceText = new();
    private readonly TextBox _destinationText = new();
    private readonly TextBox _nameText = new();
    private readonly TextBox _blenderText = new();
    private readonly TextBox _logText = new();
    private readonly Button _convertButton = new();

    public AssetImporterForm()
    {
        Text = "HS2 Asset Importer";
        Width = 900;
        Height = 620;
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;

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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(root);

        AddPathRow(root, 0, "Source folder", _sourceText, "Browse...", BrowseSourceFolder);
        AddPathRow(root, 1, "Destination", _destinationText, "Browse...", BrowseDestinationFolder);
        AddTextRow(root, 2, "Output name", _nameText);
        AddPathRow(root, 3, "Blender exe", _blenderText, "Browse...", BrowseBlenderExe);

        _destinationText.Text = FindDefaultDestinationFolder();
        _blenderText.Text = FbxToGlbConverter.ResolveBlenderExe(null) ?? "";
        _nameText.Text = "test_pistol";

        _convertButton.Text = "Convert FBX to GLB";
        _convertButton.Dock = DockStyle.Left;
        _convertButton.Width = 180;
        _convertButton.Click += async (_, _) => await ConvertAsync();
        root.Controls.Add(_convertButton, 1, 4);

        _logText.Dock = DockStyle.Fill;
        _logText.Multiline = true;
        _logText.ReadOnly = true;
        _logText.ScrollBars = ScrollBars.Both;
        _logText.WordWrap = false;
        root.Controls.Add(_logText, 0, 5);
        root.SetColumnSpan(_logText, 3);

        Label hint = new()
        {
            Dock = DockStyle.Fill,
            Text = "Tip: set HS2_BLENDER_EXE or browse to blender.exe. Output GLBs placed in Game/Content/Models/ViewModels are copied to the game on build.",
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(hint, 0, 6);
        root.SetColumnSpan(hint, 3);
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

    private void BrowseSourceFolder(object? sender, EventArgs e)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Choose the folder containing the FBX and texture files.",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _sourceText.Text = dialog.SelectedPath;
        string? fbx = Directory.EnumerateFiles(dialog.SelectedPath, "*.fbx", SearchOption.AllDirectories).FirstOrDefault();
        if (fbx != null)
            _nameText.Text = Path.GetFileNameWithoutExtension(fbx);
    }

    private void BrowseDestinationFolder(object? sender, EventArgs e)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Choose where converted GLB files should be written.",
            SelectedPath = Directory.Exists(_destinationText.Text) ? _destinationText.Text : "",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _destinationText.Text = dialog.SelectedPath;
    }

    private void BrowseBlenderExe(object? sender, EventArgs e)
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Choose blender.exe",
            Filter = "Blender executable|blender.exe|Executable files|*.exe|All files|*.*"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _blenderText.Text = dialog.FileName;
    }

    private async Task ConvertAsync()
    {
        _convertButton.Enabled = false;
        AppendLog("Starting conversion...");

        try
        {
            FbxImportRequest request = new()
            {
                SourceFolder = _sourceText.Text.Trim(),
                DestinationFolder = _destinationText.Text.Trim(),
                OutputName = _nameText.Text.Trim(),
                BlenderExePath = _blenderText.Text.Trim()
            };

            FbxConversionResult result = await FbxToGlbConverter.ConvertAsync(request);
            AppendLog(result.Log);
            AppendLog(result.Success
                ? $"Ready: {result.OutputPath}"
                : "Conversion failed.");
        }
        finally
        {
            _convertButton.Enabled = true;
        }
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        _logText.AppendText(message.TrimEnd() + Environment.NewLine + Environment.NewLine);
    }

    private static string FindDefaultDestinationFolder()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Game", "Game.csproj");
            if (File.Exists(candidate))
                return Path.Combine(dir.FullName, "Game", "Content", "Models", "ViewModels");

            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "Content", "Models", "ViewModels");
    }
}
