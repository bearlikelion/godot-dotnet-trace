#if TOOLS
using System.IO;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing.Stacks.Formats;

public static class SpeedscopeConverter
{
	public static string Convert(string nettracePath)
	{
		string outputPath = Path.ChangeExtension(nettracePath, "speedscope.json");
		string etlx = TraceLog.CreateFromEventPipeDataFile(nettracePath, null, new TraceLogOptions { ContinueOnError = true });
		using (SymbolReader symbolReader = new SymbolReader(TextWriter.Null) { SymbolPath = SymbolPath.MicrosoftSymbolServerPath })
		using (TraceLog traceLog = new TraceLog(etlx))
		{
			MutableTraceEventStackSource stackSource = new MutableTraceEventStackSource(traceLog) { OnlyManagedCodeStacks = true };
			SampleProfilerThreadTimeComputer computer = new SampleProfilerThreadTimeComputer(traceLog, symbolReader) { IncludeEventSourceEvents = false };
			computer.GenerateThreadTimeStacks(stackSource);
			SpeedScopeStackSourceWriter.WriteStackViewAsJson(stackSource, outputPath);
		}
		if (File.Exists(etlx))
		{
			File.Delete(etlx);
		}
		return outputPath;
	}
}
#endif
