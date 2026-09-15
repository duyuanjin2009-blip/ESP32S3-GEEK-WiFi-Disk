using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DuyuanyinDygPackager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new EditorForm());
    }
}

internal sealed class DygDocument
{
    [JsonPropertyName("dyg_version")] public int DygVersion { get; set; } = 1;
    [JsonPropertyName("board")] public string Board { get; set; } = EditorForm.BoardName;
    [JsonPropertyName("engine")] public string Engine { get; set; } = "lights-out";
    [JsonPropertyName("name")] public string Name { get; set; } = "新建熄灯游戏";
    [JsonPropertyName("author")] public string Author { get; set; } = "DUYUANJIN";
    [JsonPropertyName("version")] public string Version { get; set; } = "1.0.0";
    [JsonPropertyName("instructions")] public string Instructions { get; set; } = "轻触方格会切换本格和上下左右，让所有灯熄灭。";
    [JsonPropertyName("size")] public int Size { get; set; } = 4;
    [JsonPropertyName("initial")] public List<bool> Initial { get; set; } = new();
}

internal sealed class EditorForm : Form
{
    internal const string BoardName = "ESP32-S3-Touch-AMOLED-2.16";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly TextBox _name = NewTextBox();
    private readonly TextBox _author = NewTextBox();
    private readonly TextBox _version = NewTextBox();
    private readonly TextBox _instructions = NewTextBox(multiline: true);
    private readonly NumericUpDown _size = new() { Minimum = 3, Maximum = 6, Value = 4, Dock = DockStyle.Fill };
    private readonly TableLayoutPanel _board = new() { Dock = DockStyle.Fill, Padding = new Padding(8), BackColor = Color.FromArgb(243, 240, 252) };
    private readonly RichTextBox _json = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 10), BackColor = Color.White, WordWrap = false };
    private readonly ToolStripStatusLabel _status = new() { Text = "准备就绪" };
    private bool[] _cells = Array.Empty<bool>();
    private bool _loading;

    public EditorForm()
    {
        Text = "DUYUANJIN DYG 打包工具";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1000, 680);
        Size = new Size(1180, 760);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(247, 246, 252);

        Controls.Add(BuildLayout());
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);
        Controls.Add(statusStrip);

        _name.TextChanged += (_, _) => RefreshPreview();
        _author.TextChanged += (_, _) => RefreshPreview();
        _version.TextChanged += (_, _) => RefreshPreview();
        _instructions.TextChanged += (_, _) => RefreshPreview();
        _size.ValueChanged += (_, _) => ResizeBoard((int)_size.Value, preserve: true);
        NewDocument();
    }

    private Control BuildLayout()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(16, 16, 16, 28) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(BuildEditorPane(), 0, 0);
        layout.Controls.Add(BuildPreviewPane(), 1, 0);
        return layout;
    }

    private Control BuildEditorPane()
    {
        var pane = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 12, 0) };
        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, BackColor = Color.White, Padding = new Padding(16) };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));

        AddField(editor, 0, "游戏名称", _name);
        AddField(editor, 1, "作者", _author);
        AddField(editor, 2, "版本", _version);
        AddField(editor, 3, "说明", _instructions);
        AddField(editor, 4, "棋盘大小", _size);
        editor.Controls.Add(new Label { Text = "初始棋盘", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 8, 0, 0) }, 0, 5);
        editor.Controls.Add(_board, 1, 5);
        var tip = new Label { Text = "紫色为亮灯。点击任意方格切换亮灭。", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(100, 108, 135), TextAlign = ContentAlignment.MiddleLeft };
        editor.Controls.Add(tip, 1, 6);

        var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(0, 6, 0, 0) };
        commands.Controls.Add(NewButton("新建", (_, _) => NewDocument()));
        commands.Controls.Add(NewButton("打开", (_, _) => OpenDocument()));
        commands.Controls.Add(NewButton("校验", (_, _) => ValidateAndReport()));
        commands.Controls.Add(NewButton("保存 DYG", (_, _) => SaveDocument()));
        commands.Controls.Add(NewButton("导出文件夹", (_, _) => ExportFolder()));
        editor.Controls.Add(commands, 0, 7);
        editor.SetColumnSpan(commands, 2);

        pane.Controls.Add(editor);
        return pane;
    }

    private Control BuildPreviewPane()
    {
        var pane = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, BackColor = Color.White, Padding = new Padding(16) };
        pane.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        pane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        pane.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        pane.Controls.Add(new Label { Text = "设备读取的 DYG v1 JSON 预览", Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold) }, 0, 0);
        pane.Controls.Add(_json, 0, 1);
        pane.Controls.Add(new Label
        {
            Text = "目标开发板：ESP32-S3-Touch-AMOLED-2.16\r\n当前引擎：lights-out（熄灯）。单个清单不能超过 64KB。",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(100, 108, 135),
            Padding = new Padding(0, 8, 0, 0)
        }, 0, 2);
        return pane;
    }

    private static void AddField(TableLayoutPanel target, int row, string caption, Control input)
    {
        target.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        target.Controls.Add(input, 1, row);
    }

    private static TextBox NewTextBox(bool multiline = false) => new()
    {
        Dock = DockStyle.Fill,
        Multiline = multiline,
        ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None
    };

    private static Button NewButton(string text, EventHandler click) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(10, 5, 10, 5),
        BackColor = Color.FromArgb(102, 91, 199),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderSize = 0 },
        Cursor = Cursors.Hand
    }.Also(button => button.Click += click);

    private void NewDocument()
    {
        _loading = true;
        _name.Text = "新建熄灯游戏";
        _author.Text = "DUYUANJIN";
        _version.Text = "1.0.0";
        _instructions.Text = "轻触方格会切换本格和上下左右，让所有灯熄灭。";
        _size.Value = 4;
        ResizeBoard(4, preserve: false);
        _loading = false;
        RefreshPreview("已创建新游戏");
    }

    private void ResizeBoard(int size, bool preserve)
    {
        var old = _cells;
        _cells = new bool[size * size];
        if (preserve)
        {
            int oldSide = (int)Math.Sqrt(old.Length);
            int side = Math.Min(oldSide, size);
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                    _cells[y * size + x] = old[y * oldSide + x];
        }
        else
        {
            for (int i = 0; i < _cells.Length; i++) _cells[i] = (i + i / size) % 2 == 0;
        }

        _board.SuspendLayout();
        _board.Controls.Clear();
        _board.ColumnStyles.Clear();
        _board.RowStyles.Clear();
        _board.ColumnCount = size;
        _board.RowCount = size;
        for (int i = 0; i < size; i++)
        {
            _board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / size));
            _board.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / size));
        }
        for (int i = 0; i < _cells.Length; i++)
        {
            int index = i;
            var cell = new Button { Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Margin = new Padding(3), Tag = index };
            cell.FlatAppearance.BorderSize = 0;
            cell.Click += (_, _) => { _cells[index] = !_cells[index]; PaintCell(cell, _cells[index]); RefreshPreview("棋盘已更新"); };
            PaintCell(cell, _cells[index]);
            _board.Controls.Add(cell, i % size, i / size);
        }
        _board.ResumeLayout();
        if (!_loading) RefreshPreview("棋盘大小已更新");
    }

    private static void PaintCell(Button cell, bool lit)
    {
        cell.Text = lit ? "亮" : "灭";
        cell.BackColor = lit ? Color.FromArgb(108, 94, 202) : Color.FromArgb(237, 235, 248);
        cell.ForeColor = lit ? Color.White : Color.FromArgb(62, 65, 99);
    }

    private DygDocument ReadForm() => new()
    {
        Name = _name.Text.Trim(),
        Author = _author.Text.Trim(),
        Version = _version.Text.Trim(),
        Instructions = _instructions.Text.Trim(),
        Size = (int)_size.Value,
        Initial = _cells.ToList()
    };

    private void Apply(DygDocument document)
    {
        _loading = true;
        _name.Text = document.Name ?? "";
        _author.Text = document.Author ?? "";
        _version.Text = document.Version ?? "";
        _instructions.Text = document.Instructions ?? "";
        _size.Value = Math.Clamp(document.Size, 3, 6);
        ResizeBoard((int)_size.Value, preserve: false);
        for (int i = 0; i < _cells.Length && i < document.Initial.Count; i++) _cells[i] = document.Initial[i];
        foreach (Control control in _board.Controls)
            if (control is Button cell && cell.Tag is int index) PaintCell(cell, _cells[index]);
        _loading = false;
        RefreshPreview("已加载 DYG 文件");
    }

    private static List<string> Validate(DygDocument document)
    {
        var errors = new List<string>();
        if (document.DygVersion != 1) errors.Add("dyg_version 必须为 1。");
        if (document.Board != BoardName) errors.Add("board 必须严格为 ESP32-S3-Touch-AMOLED-2.16。");
        if (document.Engine != "lights-out") errors.Add("当前固件只支持 lights-out 引擎。");
        if (string.IsNullOrWhiteSpace(document.Name) || document.Name.Length > 40) errors.Add("游戏名称必须为 1 到 40 个字符。");
        if (document.Author.Length > 40) errors.Add("作者不能超过 40 个字符。");
        if (document.Version.Length > 20) errors.Add("版本不能超过 20 个字符。");
        if (document.Instructions.Length > 160) errors.Add("说明不能超过 160 个字符。");
        if (document.Size is < 3 or > 6) errors.Add("棋盘大小必须在 3 到 6 之间。");
        if (document.Initial.Count != document.Size * document.Size) errors.Add("initial 数量必须等于棋盘大小的平方。");
        int bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(document, JsonOptions));
        if (bytes > 64 * 1024) errors.Add("清单超过设备允许的 64KB 上限。");
        return errors;
    }

    private bool Check(out DygDocument document)
    {
        document = ReadForm();
        var errors = Validate(document);
        if (errors.Count == 0) return true;
        _status.Text = "校验失败";
        MessageBox.Show(string.Join(Environment.NewLine, errors), "DYG 校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private void RefreshPreview(string? status = null)
    {
        if (_loading) return;
        var document = ReadForm();
        _json.Text = JsonSerializer.Serialize(document, JsonOptions);
        var errors = Validate(document);
        _status.Text = errors.Count == 0 ? (status ?? "DYG v1 校验通过") : $"待修正：{errors[0]}";
    }

    private void ValidateAndReport()
    {
        if (!Check(out var document)) return;
        _status.Text = "DYG v1 校验通过";
        MessageBox.Show($"校验通过。\n名称：{document.Name}\n棋盘：{document.Size} × {document.Size}\n引擎：lights-out", "DYG 校验", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenDocument()
    {
        using var dialog = new OpenFileDialog { Filter = "DYG 游戏 (*.dyg;manifest.json)|*.dyg;manifest.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*", Title = "打开 DYG 或 manifest.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            if (bytes.Length > 64 * 1024) throw new InvalidDataException("文件超过设备允许的 64KB 清单上限。");
            var document = JsonSerializer.Deserialize<DygDocument>(bytes, JsonOptions) ?? throw new InvalidDataException("JSON 内容为空。");
            var errors = Validate(document);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
            Apply(document);
        }
        catch (Exception ex)
        {
            _status.Text = "无法打开文件";
            MessageBox.Show(ex.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveDocument()
    {
        if (!Check(out var document)) return;
        using var dialog = new SaveFileDialog { Filter = "DYG 游戏 (*.dyg)|*.dyg", FileName = SafeFileName(document.Name) + ".dyg", Title = "保存 DYG 游戏" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        WriteDocument(dialog.FileName, document);
        _status.Text = "DYG 文件已保存";
    }

    private void ExportFolder()
    {
        if (!Check(out var document)) return;
        using var dialog = new FolderBrowserDialog { Description = "选择导出游戏文件夹的位置" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string folder = Path.Combine(dialog.SelectedPath, SafeFileName(document.Name));
        Directory.CreateDirectory(folder);
        string manifest = Path.Combine(folder, "manifest.json");
        if (File.Exists(manifest) && MessageBox.Show("目标文件夹已存在 manifest.json，是否覆盖？", "确认覆盖", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        WriteDocument(manifest, document);
        File.WriteAllText(Path.Combine(folder, "README.txt"), "将此文件夹复制到 SD 卡的 /DUYUANJIN/Games/ 下。\r\n当前固件只运行 manifest.json 的 lights-out 声明式游戏。", new UTF8Encoding(false));
        _status.Text = "游戏文件夹已导出";
        MessageBox.Show($"已导出：{folder}\n将整个文件夹复制到 SD 卡 /DUYUANJIN/Games/ 下。", "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void WriteDocument(string path, DygDocument document) => File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions), new UTF8Encoding(false));

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var value = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        return string.IsNullOrWhiteSpace(value) ? "未命名游戏" : value;
    }
}

internal static class ControlExtensions
{
    internal static T Also<T>(this T control, Action<T> action) where T : Control
    {
        action(control);
        return control;
    }
}
