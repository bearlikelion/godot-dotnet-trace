#if TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

public sealed class ProfileTree
{
	public sealed class Node
	{
		public string FullName = "";
		public string Name = "";
		public int Calls;
		public double TotalMs;
		public double SelfMs;
		public readonly Dictionary<int, Node> Children = new Dictionary<int, Node>();

		public IEnumerable<Node> SortedChildren => Children.Values.OrderByDescending(c => c.TotalMs);
	}

	private sealed class Profile
	{
		public bool[] Open = Array.Empty<bool>();
		public int[] Frame = Array.Empty<int>();
		public double[] At = Array.Empty<double>();
	}

	private static readonly string[] GodotModules = { "GodotSharp", "GodotSharpEditor", "System.", "Microsoft.", "netstandard" };

	private readonly string[] _names;
	private readonly string[] _modules;
	private readonly string[] _namespaces;
	private readonly List<Profile> _profiles = new List<Profile>();

	public ProfileTree(string jsonPath)
	{
		using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
		_names = doc.RootElement.GetProperty("shared").GetProperty("frames").EnumerateArray()
			.Select(f => f.GetProperty("name").GetString() ?? "?").ToArray();
		_modules = _names.Select(n => n.Contains('!') ? n[..n.IndexOf('!')] : "").ToArray();
		_namespaces = _names.Select(n => NamespaceOf(n).ToLowerInvariant()).ToArray();
		foreach (JsonElement profile in doc.RootElement.GetProperty("profiles").EnumerateArray())
		{
			if (profile.GetProperty("type").GetString() != "evented")
			{
				continue;
			}
			JsonElement events = profile.GetProperty("events");
			Profile p = new Profile
			{
				Open = new bool[events.GetArrayLength()],
				Frame = new int[events.GetArrayLength()],
				At = new double[events.GetArrayLength()],
			};
			int i = 0;
			foreach (JsonElement ev in events.EnumerateArray())
			{
				p.Open[i] = ev.GetProperty("type").GetString() == "O";
				p.Frame[i] = ev.GetProperty("frame").GetInt32();
				p.At[i] = ev.GetProperty("at").GetDouble();
				i++;
			}
			_profiles.Add(p);
		}
	}

	// Hidden frames are skipped and their time is charged to the nearest visible caller.
	public Node Build(string projectModule, bool hideGodot, bool hideAddons, string[] addonPrefixes)
	{
		bool[] visible = new bool[_names.Length];
		for (int i = 0; i < _names.Length; i++)
		{
			visible[i] = IsVisible(i, projectModule, hideGodot, hideAddons, addonPrefixes);
		}
		Node root = new Node { Name = "root" };
		Stack<(Node Node, bool Own)> stack = new Stack<(Node, bool)>();
		foreach (Profile p in _profiles)
		{
			stack.Clear();
			stack.Push((root, false));
			for (int i = 0; i < p.Frame.Length; i++)
			{
				int frame = p.Frame[i];
				if (p.Open[i])
				{
					Node top = stack.Peek().Node;
					if (!visible[frame])
					{
						stack.Push((top, false));
						continue;
					}
					if (!top.Children.TryGetValue(frame, out Node? child))
					{
						child = new Node { FullName = _names[frame], Name = ShortName(_names[frame]) };
						top.Children[frame] = child;
					}
					child.Calls++;
					child.TotalMs -= p.At[i];
					stack.Push((child, true));
					continue;
				}
				(Node Node, bool Own) closed = stack.Pop();
				if (closed.Own)
				{
					closed.Node.TotalMs += p.At[i];
				}
			}
		}
		ComputeSelf(root);
		root.TotalMs = root.Children.Values.Sum(c => c.TotalMs);
		return root;
	}

	public static List<Node> Flatten(Node root)
	{
		Dictionary<string, Node> flat = new Dictionary<string, Node>();
		HashSet<string> ancestors = new HashSet<string>();
		Flatten(root, flat, ancestors);
		return flat.Values.OrderByDescending(n => n.SelfMs).ToList();
	}

	private static void Flatten(Node node, Dictionary<string, Node> flat, HashSet<string> ancestors)
	{
		foreach (Node child in node.Children.Values)
		{
			if (!flat.TryGetValue(child.FullName, out Node? sum))
			{
				sum = new Node { FullName = child.FullName, Name = child.Name };
				flat[child.FullName] = sum;
			}
			sum.Calls += child.Calls;
			sum.SelfMs += child.SelfMs;
			// Recursive frames only count toward total once per stack.
			bool recursive = !ancestors.Add(child.FullName);
			if (!recursive)
			{
				sum.TotalMs += child.TotalMs;
			}
			Flatten(child, flat, ancestors);
			if (!recursive)
			{
				ancestors.Remove(child.FullName);
			}
		}
	}

	private static void ComputeSelf(Node node)
	{
		double children = 0;
		foreach (Node child in node.Children.Values)
		{
			ComputeSelf(child);
			children += child.TotalMs;
		}
		node.SelfMs = Math.Max(0, node.TotalMs - children);
	}

	private bool IsVisible(int i, string projectModule, bool hideGodot, bool hideAddons, string[] addonPrefixes)
	{
		string name = _names[i];
		if (!name.Contains('!'))
		{
			return false;
		}
		string module = _modules[i];
		// Source-generated glue lives in the project assembly but is Godot plumbing.
		bool isGodot = GodotModules.Any(m => module.StartsWith(m, StringComparison.Ordinal))
			|| name.Contains("GodotClassMethod(", StringComparison.Ordinal);
		if (hideGodot && isGodot)
		{
			return false;
		}
		if (!hideAddons)
		{
			return true;
		}
		bool isAddon = (!isGodot && module != projectModule)
			|| addonPrefixes.Any(p => _namespaces[i].StartsWith(p, StringComparison.Ordinal));
		return !isAddon;
	}

	private static string NamespaceOf(string name)
	{
		string body = name.Contains('!') ? name[(name.IndexOf('!') + 1)..] : name;
		int paren = body.IndexOf('(');
		if (paren >= 0)
		{
			body = body[..paren];
		}
		int lastDot = body.LastIndexOf('.');
		int typeDot = lastDot > 0 ? body.LastIndexOf('.', lastDot - 1) : -1;
		return typeDot > 0 ? body[..typeDot] : "";
	}

	private static string ShortName(string name)
	{
		string body = name.Contains('!') ? name[(name.IndexOf('!') + 1)..] : name;
		int paren = body.IndexOf('(');
		if (paren >= 0)
		{
			body = body[..paren];
		}
		int lastDot = body.LastIndexOf('.');
		int typeDot = lastDot > 0 ? body.LastIndexOf('.', lastDot - 1) : -1;
		return typeDot > 0 ? body[(typeDot + 1)..] : body;
	}
}
#endif
