using Godot;

public partial class Busy : Node
{
	private double _sink;

	public override void _Process(double delta)
	{
		SpinMath();
		SpinAlloc();
	}

	private void SpinMath()
	{
		for (int i = 0; i < 200000; i++)
		{
			_sink += Mathf.Sin(i) * Mathf.Cos(i);
		}
	}

	private void SpinAlloc()
	{
		for (int i = 0; i < 2000; i++)
		{
			_sink += new int[64].Length;
		}
	}
}
