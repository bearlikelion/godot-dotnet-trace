#if TOOLS
using System.Collections.Generic;
using Godot;

[Tool]
public partial class TraceDebuggerPlugin : EditorDebuggerPlugin
{
	private readonly List<(EditorDebuggerSession Session, TracePanel Panel)> _panels = new List<(EditorDebuggerSession, TracePanel)>();

	public override void _SetupSession(int sessionId)
	{
		EditorDebuggerSession session = GetSession(sessionId);
		TracePanel panel = new TracePanel();
		panel.Name = "C# Trace";
		session.AddSessionTab(panel);
		session.Connect(EditorDebuggerSession.SignalName.Started, new Callable(panel, TracePanel.MethodName.OnSessionStarted));
		session.Connect(EditorDebuggerSession.SignalName.Stopped, new Callable(panel, TracePanel.MethodName.OnSessionStopped));
		_panels.Add((session, panel));
	}

	// Stale tabs outlive an assembly reload and would call into freed delegates.
	public void RemoveTabs()
	{
		foreach ((EditorDebuggerSession session, TracePanel panel) in _panels)
		{
			if (GodotObject.IsInstanceValid(session) && GodotObject.IsInstanceValid(panel))
			{
				session.RemoveSessionTab(panel);
				panel.QueueFree();
			}
		}
		_panels.Clear();
	}
}
#endif
