using System.Text.Json;

namespace CuaChild;

internal sealed class McpConfigDialog : Form
{
    private readonly ComboBox format = Ui.Combo("Codex · TOML", "通用客户端 · JSON");
    private readonly TextBox path = new();
    private readonly ComboBox name = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox command = new();
    private readonly TextBox args = new() { Multiline = true, Height = 90, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10) };
    private readonly NumericUpDown startup = new() { Minimum = 1, Maximum = 86400, Value = 120 };
    private readonly NumericUpDown tool = new() { Minimum = 1, Maximum = 86400, Value = 120 };
    private readonly TextBox preview = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 10), BackColor = Color.FromArgb(20, 29, 42), ForeColor = Color.FromArgb(219, 232, 240), BorderStyle = BorderStyle.None };
    private readonly Label message = new() { AutoSize = true, ForeColor = Ui.Muted, MaximumSize = new Size(660, 0), Margin = new Padding(12) };
    private readonly Button save;
    private readonly Options options;
    private string? loadedPath, source, pending;
    private bool Toml => format.SelectedIndex == 0;
    internal McpConfigDialog(Options options, string? initialPath = null)
    {
        this.options = options;
        Ui.Style(this, "MCP 配置", new Size(1040, 700));
        MinimumSize = new Size(900, 480);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1040, 600), SplitterDistance = 400, Panel1MinSize = 320, Panel2MinSize = 300, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.AutoScroll = true;
        var fields = Ui.Fields(); split.Panel1.Controls.Add(fields);
        Ui.Field(fields, "配置格式", format); Ui.Field(fields, "配置文件", path);
        var files = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        files.Controls.Add(Ui.Button("选择文件", ChooseFile)); files.Controls.Add(Ui.Button("读取", () => Attempt(LoadDocument)));
        fields.Controls.Add(files);
        Ui.Field(fields, "服务名称", name);
        fields.Controls.Add(Ui.Button("填入 Cua Child 配置", Defaults));
        Ui.Field(fields, "启动命令 / 可执行文件", command);
        fields.Controls.Add(Ui.Button("选择程序", () => { using var pick = new OpenFileDialog { Filter = "可执行文件|*.exe;*.cmd;*.bat|所有文件|*.*" }; if (pick.ShowDialog(this) == DialogResult.OK) command.Text = pick.FileName; }));
        Ui.Field(fields, "参数（JSON 字符串数组）", args);
        Ui.Field(fields, "启动超时（秒，仅 Codex）", startup); Ui.Field(fields, "工具超时（秒，仅 Codex）", tool);
        var previewPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), BackColor = preview.BackColor };
        var caption = new Label { Text = "保存预览", Dock = DockStyle.Top, Height = 36, ForeColor = Color.White, Font = new Font(Font, FontStyle.Bold) };
        previewPanel.Controls.Add(preview); previewPanel.Controls.Add(caption); split.Panel2.Controls.Add(previewPanel);
        var footer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        message.Text = "选择已有服务可修改；新名称会新增服务。其他服务与额外字段保留。";
        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, FlowDirection = FlowDirection.RightToLeft };
        save = Ui.Button("保存配置", () => Attempt(Save), true); save.Enabled = false;
        actions.Controls.Add(save); actions.Controls.Add(Ui.Button("预览", () => Attempt(BuildPreview))); actions.Controls.Add(Ui.Button("关闭", Close));
        footer.Controls.Add(message, 0, 0); footer.Controls.Add(actions, 1, 0);
        root.Controls.Add(split, 0, 0); root.Controls.Add(footer, 0, 1); Controls.Add(root);
        foreach (var field in new Control[] { path, name, command, args }) field.TextChanged += (_, _) => InvalidatePreview();
        startup.ValueChanged += (_, _) => InvalidatePreview(); tool.ValueChanged += (_, _) => InvalidatePreview();
        format.SelectedIndexChanged += (_, _) => { loadedPath = null; name.Items.Clear(); startup.Enabled = tool.Enabled = Toml; InvalidatePreview(); };
        name.SelectionChangeCommitted += (_, _) => Attempt(() => Fill(McpConfigStore.Read(source ?? "", Toml, name.Text)));
        path.Text = initialPath ?? McpConfigStore.CodexPath;
        Defaults();
        if (File.Exists(path.Text)) Attempt(LoadDocument);
    }
    private void Attempt(Action action) { try { action(); } catch (Exception ex) { message.Text = ex.Message; MessageBox.Show(this, ex.Message, "配置未保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    private void InvalidatePreview() { pending = null; save.Enabled = false; preview.Text = "点击“预览”查看将要保存的配置。"; }
    private void Defaults() => Fill(new("cua-child", Path.Combine(AppContext.BaseDirectory, "cua-child.exe"), ["mcp", "--driver", options.Driver, "--timeout", options.Timeout.ToString()]));
    private void Fill(McpEntry entry)
    {
        name.Text = entry.Name; command.Text = entry.Command; args.Text = JsonSerializer.Serialize(entry.Args, new JsonSerializerOptions { WriteIndented = true });
        startup.Value = Math.Clamp(entry.StartupTimeout, 1, 86400); tool.Value = Math.Clamp(entry.ToolTimeout, 1, 86400);
        InvalidatePreview();
    }
    private void ChooseFile()
    {
        using var pick = new SaveFileDialog { Title = "选择或创建 MCP 配置文件", Filter = Toml ? "TOML 配置|*.toml" : "JSON 配置|*.json", FileName = Toml ? "config.toml" : "mcp.json", OverwritePrompt = false };
        if (pick.ShowDialog(this) != DialogResult.OK) return;
        path.Text = pick.FileName; Attempt(LoadDocument);
    }
    private void LoadDocument() => LoadDocument(true);
    private void LoadDocument(bool fillSelected)
    {
        var fullPath = Path.GetFullPath(path.Text);
        string? text = File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
        string[] names = McpConfigStore.Names(text ?? "", Toml);
        source = text; loadedPath = fullPath; name.Items.Clear(); name.Items.AddRange(names);
        if (fillSelected && names.Contains("cua-child")) Fill(McpConfigStore.Read(text!, Toml, "cua-child"));
        InvalidatePreview(); message.Text = text is null ? "新配置文件；保存后创建。" : $"已读取 {names.Length} 个服务。选择名称可编辑 stdio 配置。";
    }
    private void BuildPreview()
    {
        string fullPath = Path.GetFullPath(path.Text);
        if (loadedPath != fullPath) LoadDocument(false);
        var values = JsonSerializer.Deserialize<string[]>(args.Text) ?? throw new FormatException("参数必须是 JSON 字符串数组。");
        if (values.Any(x => x is null)) throw new FormatException("参数不能包含 null。");
        pending = McpConfigStore.Merge(source ?? "", Toml, new(name.Text.Trim(), command.Text.Trim(), values, (int)startup.Value, (int)tool.Value));
        preview.Text = pending; save.Enabled = true; message.Text = "保存将合并所选服务并备份原文件。排版可能重新格式化。";
    }
    private void Save()
    {
        if (pending is null || loadedPath is null) throw new InvalidOperationException("请先预览配置。");
        string? backup = McpConfigStore.Save(loadedPath, source, pending);
        source = pending; pending = null; save.Enabled = false;
        message.Text = "配置已保存。请在客户端重新加载 MCP 服务。";
        MessageBox.Show(this, message.Text + (backup is null ? "" : "\n备份：" + backup), "保存完成");
    }
}
