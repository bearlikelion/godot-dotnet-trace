#if TOOLS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;

public sealed class TraceSession
{
	// Same providers as dotnet-trace's cpu-sampling profile.
	private static readonly EventPipeProvider[] Providers =
	{
		new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational),
		new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, 0x14C14FCCBDL),
	};

	private EventPipeSession? _session;
	private Task? _copy;

	public int Pid { get; private set; }
	public string NettracePath { get; private set; } = "";

	public static int FindGamePid()
	{
		List<int> candidates = DiagnosticsClient.GetPublishedProcesses()
			.Where(pid => pid != Environment.ProcessId)
			.ToList();
		if (candidates.Count == 0)
		{
			throw new InvalidOperationException("No .NET process with a diagnostics port found. Is the game running?");
		}
		// The game is launched from the editor binary, so prefer processes sharing its name.
		string editorName = Process.GetCurrentProcess().ProcessName;
		List<int> sameBinary = candidates.Where(pid => Process.GetProcessById(pid).ProcessName == editorName).ToList();
		return (sameBinary.Count > 0 ? sameBinary : candidates)
			.OrderByDescending(pid => Process.GetProcessById(pid).StartTime)
			.First();
	}

	public void Start(string outputDir)
	{
		Pid = FindGamePid();
		Directory.CreateDirectory(outputDir);
		NettracePath = Path.Combine(outputDir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".nettrace");
		DiagnosticsClient client = new DiagnosticsClient(Pid);
		_session = client.StartEventPipeSession(Providers, requestRundown: true);
		FileStream file = File.Create(NettracePath);
		_copy = _session.EventStream.CopyToAsync(file).ContinueWith(_ => file.Dispose());
	}

	public async Task<string> StopAsync()
	{
		if (_session == null || _copy == null)
		{
			return "";
		}
		try
		{
			_session.Stop();
		}
		catch (Exception)
		{
			// The game may already have exited; the stream copy ends on its own.
		}
		await _copy;
		_session.Dispose();
		_session = null;
		_copy = null;
		return NettracePath;
	}
}
#endif
