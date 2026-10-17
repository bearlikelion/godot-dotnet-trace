#if TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

public static class SourceLocator
{
	// Full names come from TraceEvent: "Module!Namespace.Type+Nested.Method(params)".
	public static (string File, int Line)? Find(string dllPath, string fullName)
	{
		string pdbPath = Path.ChangeExtension(dllPath, "pdb");
		if (!File.Exists(dllPath) || !File.Exists(pdbPath))
		{
			return null;
		}
		string body = fullName[(fullName.IndexOf('!') + 1)..];
		int paren = body.IndexOf('(');
		if (paren >= 0)
		{
			body = body[..paren];
		}
		int lastDot = body.LastIndexOf('.');
		if (lastDot < 0)
		{
			return null;
		}
		string methodName = body[(lastDot + 1)..];
		string typeName = StripGenerics(body[..lastDot]);
		using FileStream peStream = File.OpenRead(dllPath);
		using PEReader pe = new PEReader(peStream);
		MetadataReader md = pe.GetMetadataReader();
		using FileStream pdbStream = File.OpenRead(pdbPath);
		using MetadataReaderProvider pdbProvider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
		MetadataReader pdb = pdbProvider.GetMetadataReader();
		foreach (TypeDefinitionHandle typeHandle in md.TypeDefinitions)
		{
			TypeDefinition type = md.GetTypeDefinition(typeHandle);
			if (StripGenerics(FullTypeName(md, type)) != typeName)
			{
				continue;
			}
			foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
			{
				if (md.GetString(md.GetMethodDefinition(methodHandle).Name) != methodName)
				{
					continue;
				}
				MethodDebugInformation debug = pdb.GetMethodDebugInformation(methodHandle);
				SequencePoint? point = debug.GetSequencePoints().FirstOrDefault(p => !p.IsHidden);
				if (point == null || point.Value.Document.IsNil)
				{
					continue;
				}
				return (pdb.GetString(pdb.GetDocument(point.Value.Document).Name), point.Value.StartLine);
			}
		}
		return null;
	}

	private static string FullTypeName(MetadataReader md, TypeDefinition type)
	{
		string name = md.GetString(type.Name);
		TypeDefinitionHandle declaring = type.GetDeclaringType();
		if (!declaring.IsNil)
		{
			return FullTypeName(md, md.GetTypeDefinition(declaring)) + "+" + name;
		}
		string ns = md.GetString(type.Namespace);
		return ns == "" ? name : ns + "." + name;
	}

	// Drops `1 arity suffixes and [T] instantiations so both sides compare equal.
	private static string StripGenerics(string name)
	{
		List<char> result = new List<char>(name.Length);
		int depth = 0;
		for (int i = 0; i < name.Length; i++)
		{
			char c = name[i];
			if (c == '[')
			{
				depth++;
			}
			else if (c == ']')
			{
				depth--;
			}
			else if (depth == 0 && c == '`')
			{
				while (i + 1 < name.Length && char.IsDigit(name[i + 1]))
				{
					i++;
				}
			}
			else if (depth == 0)
			{
				result.Add(c);
			}
		}
		return new string(result.ToArray());
	}
}
#endif
