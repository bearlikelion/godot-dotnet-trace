#if TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

[Tool]
public partial class TracePanel : VBoxContainer
{
	private enum Column { Name, Calls, Total, TotalPercent, Self, SelfPercent }

	private readonly TraceSession _session = new TraceSession();
	private Button _record = null!;
	private Button _stop = null!;
	private SpinBox _seconds = null!;
	private Timer _timer = null!;
	private Timer _autostartTimer = null!;
	private int _autostartAttempts;
	private Button _open = null!;
	private Button _folder = null!;
	private CheckBox _hideGodot = null!;
	private CheckBox _hideAddons = null!;
	private CheckBox _flat = null!;
	private CheckBox _autostart = null!;
	private Label _status = null!;
	private Tree _tree = null!;
	private ProfileTree? _profile;
	private string _jsonPath = "";
	private bool _gameRunning;
	private ulong _recordStart;

	private const string AutostartSetting = "debugger/dotnet_trace_autostart";

	private static string TracesDir => ProjectSettings.GlobalizePath("user://traces");

	public override void _Process(double delta)
	{
		if (!_stop.Disabled)
		{
			_status.Text = $"Recording pid {_session.Pid}: {(Time.GetTicksMsec() - _recordStart) / 1000.0:F1} s";
		}
	}

	public override void _Ready()
	{
		HBoxContainer bar = new HBoxContainer();
		AddChild(bar);
		_record = AddButton(bar, "Record", MethodName.OnRecordPressed, "Play");
		_stop = AddButton(bar, "Stop", MethodName.OnStopPressed, "Stop");
		_seconds = new SpinBox { MinValue = 0, MaxValue = 600, Step = 1, Value = 0, Prefix = "for", Suffix = "s", TooltipText = "0 records until Stop" };
		bar.AddChild(_seconds);
		_timer = new Timer { OneShot = true };
		_timer.Connect(Timer.SignalName.Timeout, new Callable(this, MethodName.OnStopPressed));
		AddChild(_timer);
		_autostartTimer = new Timer { WaitTime = 0.5 };
		_autostartTimer.Connect(Timer.SignalName.Timeout, new Callable(this, MethodName.OnAutostartTimeout));
		AddChild(_autostartTimer);
		_autostart = new CheckBox { Text = "Autostart", ButtonPressed = EditorInterface.Singleton.GetEditorSettings().GetSetting(AutostartSetting).AsBool() };
		_autostart.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.OnAutostartToggled));
		bar.AddChild(_autostart);
		bar.AddChild(new VSeparator());
		_hideGodot = AddCheck(bar, "Hide Godot", true);
		_hideAddons = AddCheck(bar, "Hide addons", true);
		_flat = AddCheck(bar, "Flat", false);
		AddButton(bar, "Collapse All", MethodName.OnCollapsePressed, "CollapseTree");
		AddButton(bar, "Expand All", MethodName.OnExpandPressed, "ExpandTree");
		bar.AddChild(new VSeparator());
		_open = AddButton(bar, "Open in speedscope.app", MethodName.OnOpenPressed, "ExternalLink");
		_folder = AddButton(bar, "Open traces folder", MethodName.OnFolderPressed, "Folder");
		_status = new Label { Text = "Run the game to enable recording.", SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right, ClipText = true };
		bar.AddChild(_status);
		_tree = new Tree { Columns = 6, SizeFlagsVertical = SizeFlags.ExpandFill, HideRoot = true };
		_tree.SetColumnTitlesVisible(true);
		SetColumn(Column.Name, "Function", 0);
		SetColumn(Column.Calls, "Calls", 70);
		SetColumn(Column.Total, "Total ms", 90);
		SetColumn(Column.TotalPercent, "Total %", 70);
		SetColumn(Column.Self, "Self ms", 90);
		SetColumn(Column.SelfPercent, "Self %", 70);
		_tree.Connect(Tree.SignalName.ItemActivated, new Callable(this, MethodName.OnItemActivated));
		AddChild(_tree);
		UpdateButtons(false);
	}

	public void OnSessionStarted()
	{
		_gameRunning = true;
		UpdateButtons(false);
		if (_autostart.ButtonPressed)
		{
			_autostartAttempts = 0;
			_status.Text = "Waiting for the .NET runtime...";
			_autostartTimer.Start();
		}
	}

	public void OnSessionStopped()
	{
		_gameRunning = false;
		_autostartTimer.Stop();
		if (_stop.Disabled)
		{
			UpdateButtons(false);
			return;
		}
		OnStopPressed();
	}

	// Name-based callables survive .NET assembly reloads; C# delegates do not.
	private Button AddButton(Node parent, string text, StringName method, string icon = "")
	{
		Button button = new Button { Text = text, Flat = icon != "" };
		if (icon != "")
		{
			button.Icon = EditorInterface.Singleton.GetEditorTheme().GetIcon(icon, "EditorIcons");
			button.TooltipText = text;
			button.Text = "";
		}
		button.Connect(BaseButton.SignalName.Pressed, new Callable(this, method));
		parent.AddChild(button);
		return button;
	}

	private CheckBox AddCheck(Node parent, string text, bool on)
	{
		CheckBox check = new CheckBox { Text = text, ButtonPressed = on };
		check.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.OnFilterToggled));
		parent.AddChild(check);
		return check;
	}

	private void SetColumn(Column column, string title, int width)
	{
		int i = (int)column;
		_tree.SetColumnTitle(i, title);
		if (width > 0)
		{
			_tree.SetColumnExpand(i, false);
			_tree.SetColumnCustomMinimumWidth(i, width);
		}
	}

	private void UpdateButtons(bool recording)
	{
		_record.Disabled = recording || !_gameRunning;
		_stop.Disabled = !recording;
		_open.Disabled = _jsonPath == "";
		_folder.Disabled = !Directory.Exists(TracesDir);
	}

	// speedscope.app cannot be handed a local file, so show it beside the browser for a drag and drop.
	private void OnOpenPressed()
	{
		OS.ShellOpen("https://www.speedscope.app");
		OS.ShellShowInFileManager(_jsonPath);
	}

	// The debugger connects before the game's runtime exists, so poll until the port appears.
	private void OnAutostartTimeout()
	{
		if (!_gameRunning || !_stop.Disabled)
		{
			_autostartTimer.Stop();
			return;
		}
		_autostartAttempts++;
		if (TryStartRecording() || _autostartAttempts >= 20)
		{
			_autostartTimer.Stop();
		}
	}

	private void OnAutostartToggled(bool on)
	{
		EditorInterface.Singleton.GetEditorSettings().SetSetting(AutostartSetting, on);
	}

	private void OnFilterToggled(bool on)
	{
		Refresh();
	}

	private void OnCollapsePressed()
	{
		SetCollapsed(true);
	}

	private void OnExpandPressed()
	{
		SetCollapsed(false);
	}

	private void OnFolderPressed()
	{
		OS.ShellOpen(TracesDir);
	}

	private void OnItemActivated()
	{
		TreeItem? item = _tree.GetSelected();
		if (item == null)
		{
			return;
		}
		string fullName = item.GetMetadata((int)Column.Name).AsString();
		if (fullName == "")
		{
			return;
		}
		string dll = ProjectSettings.GlobalizePath($"res://.godot/mono/temp/bin/Debug/{ProjectModule()}.dll");
		(string File, int Line)? location = SourceLocator.Find(dll, fullName);
		if (location == null)
		{
			_status.Text = $"No source for {item.GetText((int)Column.Name)}";
			return;
		}
		Script script = ResourceLoader.Load<Script>(ProjectSettings.LocalizePath(location.Value.File));
		EditorInterface.Singleton.EditScript(script, location.Value.Line);
	}

	private static string ProjectModule()
	{
		string module = ProjectSettings.GetSetting("dotnet/project/assembly_name", "").AsString();
		return module != "" ? module : ProjectSettings.GetSetting("application/config/name").AsString();
	}

	private void OnRecordPressed()
	{
		TryStartRecording();
	}

	private bool TryStartRecording()
	{
		try
		{
			_session.Start(TracesDir);
		}
		catch (Exception e)
		{
			_status.Text = e.Message;
			return false;
		}
		_recordStart = Time.GetTicksMsec();
		if (_seconds.Value > 0)
		{
			_timer.Start(_seconds.Value);
		}
		UpdateButtons(true);
		return true;
	}

	private async void OnStopPressed()
	{
		_timer.Stop();
		UpdateButtons(false);
		_record.Disabled = true;
		_status.Text = "Stopping and converting...";
		try
		{
			string nettrace = await _session.StopAsync();
			_jsonPath = await Task.Run(() => SpeedscopeConverter.Convert(nettrace));
			_profile = await Task.Run(() => new ProfileTree(_jsonPath));
			_status.Text = _jsonPath;
			Refresh();
		}
		catch (Exception e)
		{
			_status.Text = e.Message;
		}
		UpdateButtons(false);
	}

	private void Refresh()
	{
		if (_profile == null)
		{
			return;
		}
		ProfileTree.Node root = _profile.Build(
			ProjectModule(), _hideGodot.ButtonPressed, _hideAddons.ButtonPressed, AddonPrefixes());
		_tree.Clear();
		TreeItem header = _tree.CreateItem(_tree.CreateItem());
		header.SetText((int)Column.Name, "Script Functions");
		header.SetSelectable((int)Column.Name, false);
		SetNumber(header, Column.Total, root.TotalMs.ToString("F1"));
		SetNumber(header, Column.TotalPercent, "100.0");
		if (_flat.ButtonPressed)
		{
			foreach (ProfileTree.Node node in ProfileTree.Flatten(root))
			{
				AddRow(header, node, root.TotalMs);
			}
			return;
		}
		foreach (ProfileTree.Node node in root.SortedChildren)
		{
			AddSubtree(header, node, root.TotalMs, 1);
		}
	}

	private void SetCollapsed(bool collapsed)
	{
		TreeItem? header = _tree.GetRoot()?.GetFirstChild();
		if (header == null)
		{
			return;
		}
		for (TreeItem? item = header.GetFirstChild(); item != null; item = item.GetNext())
		{
			item.SetCollapsedRecursive(collapsed);
		}
	}

	private void AddSubtree(TreeItem parent, ProfileTree.Node node, double totalMs, int depth)
	{
		TreeItem item = AddRow(parent, node, totalMs);
		item.Collapsed = depth > 2;
		foreach (ProfileTree.Node child in node.SortedChildren)
		{
			AddSubtree(item, child, totalMs, depth + 1);
		}
	}

	private TreeItem AddRow(TreeItem parent, ProfileTree.Node node, double totalMs)
	{
		TreeItem item = _tree.CreateItem(parent);
		item.SetText((int)Column.Name, node.Name);
		item.SetTooltipText((int)Column.Name, node.FullName);
		item.SetMetadata((int)Column.Name, node.FullName);
		SetNumber(item, Column.Calls, node.Calls.ToString());
		SetNumber(item, Column.Total, node.TotalMs.ToString("F1"));
		SetNumber(item, Column.TotalPercent, Percent(node.TotalMs, totalMs), node.TotalMs / totalMs);
		SetNumber(item, Column.Self, node.SelfMs.ToString("F1"));
		SetNumber(item, Column.SelfPercent, Percent(node.SelfMs, totalMs), node.SelfMs / totalMs);
		item.SetCustomColor((int)Column.Name, Heat(node.SelfMs / totalMs));
		return item;
	}

	// Blend from the normal font color toward the editor's error red as the share of time grows.
	private static Color Heat(double share)
	{
		Theme theme = EditorInterface.Singleton.GetEditorTheme();
		Color normal = theme.GetColor("font_color", "Editor");
		Color hot = theme.GetColor("error_color", "Editor");
		return normal.Lerp(hot, (float)Math.Clamp(share * 2.0, 0.0, 1.0));
	}

	private static void SetNumber(TreeItem item, Column column, string text, double share = -1)
	{
		item.SetText((int)column, text);
		item.SetTextAlignment((int)column, HorizontalAlignment.Right);
		if (share >= 0)
		{
			item.SetCustomColor((int)column, Heat(share));
		}
	}

	private static string Percent(double value, double total)
	{
		return total > 0 ? (100.0 * value / total).ToString("F1") : "0.0";
	}

	// Addon folder names double as namespace prefixes: imgui-godot matches ImGuiGodot.
	private static string[] AddonPrefixes()
	{
		string addons = ProjectSettings.GlobalizePath("res://addons");
		if (!Directory.Exists(addons))
		{
			return Array.Empty<string>();
		}
		return Directory.GetDirectories(addons)
			.Select(d => Path.GetFileName(d).Replace("-", "").Replace("_", "").ToLowerInvariant())
			.Where(d => d != "dotnettrace")
			.ToArray();
	}
}
#endif
