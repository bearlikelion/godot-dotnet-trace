#if TOOLS
using Godot;

[Tool]
public partial class DotnetTracePlugin : EditorPlugin
{
	private TraceDebuggerPlugin? _debugger;

	public override void _EnterTree()
	{
		_debugger = new TraceDebuggerPlugin();
		AddDebuggerPlugin(_debugger);
	}

	public override void _ExitTree()
	{
		if (_debugger != null)
		{
			_debugger.RemoveTabs();
			RemoveDebuggerPlugin(_debugger);
			_debugger = null;
		}
	}
}
#endif
