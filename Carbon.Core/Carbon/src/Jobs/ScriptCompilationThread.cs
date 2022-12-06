﻿///
/// Copyright (c) 2022 Carbon Community 
/// All rights reserved
/// 

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Carbon.Base;
using Carbon.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Carbon.Jobs;

public class ScriptCompilationThread : BaseThreadedJob
{
	public string FilePath;
	public string FileName;
	public string Source;
	public string[] References;
	public string[] Requires;
	public Dictionary<Type, List<string>> Hooks = new Dictionary<Type, List<string>>();
	public Dictionary<Type, List<string>> UnsupportedHooks = new Dictionary<Type, List<string>>();
	public Dictionary<Type, List<HookMethodAttribute>> HookMethods = new Dictionary<Type, List<HookMethodAttribute>>();
	public Dictionary<Type, List<PluginReferenceAttribute>> PluginReferences = new Dictionary<Type, List<PluginReferenceAttribute>>();
	public float CompileTime;
	public Assembly Assembly;
	public List<CompilerException> Exceptions = new List<CompilerException>();

	internal DateTime TimeSinceCompile;

	private static HashSet<MetadataReference> cachedReferences = new HashSet<MetadataReference>();
	internal static bool _hasInit { get; set; }
	internal static void _doInit()
	{
		if (_hasInit) return;
		_hasInit = true;

		foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			try
			{
				byte[] raw = Supervisor.Resolver.GetAssemblyBytes(assembly.GetName().Name);
				if (raw == null || raw.Length == 0) throw new ArgumentException();

				using (MemoryStream mem = new MemoryStream(raw))
					cachedReferences.Add(MetadataReference.CreateFromStream(mem));
			}
			catch { }
		}
		Logger.Debug($"ScriptCompilationThread cached {cachedReferences.Count} assemblies", 2);
	}

	internal static Dictionary<string, object> _referenceCache = new Dictionary<string, object>();
	internal static Dictionary<string, byte[]> _compilationCache = new Dictionary<string, byte[]>();

	internal static byte[] _getPlugin(string name)
	{
		if (!_compilationCache.TryGetValue(name, out var result)) return null;

		return result;
	}
	internal static void _overridePlugin(string name, byte[] pluginAssembly)
	{
		if (pluginAssembly == null) return;

		var plugin = _getPlugin(name);
		if (plugin == null)
		{
			try { _compilationCache.Add(name, pluginAssembly); } catch { }
			return;
		}

		Array.Clear(plugin, 0, plugin.Length);
		try { _compilationCache[name] = pluginAssembly; } catch { }
	}

	internal static MetadataReference _getReferenceFromCache(string reference)
	{
		try
		{
			byte[] raw = Supervisor.Resolver.GetAssemblyBytes(reference);
			if (raw == null || raw.Length == 0) throw new ArgumentException();

			using (MemoryStream mem = new MemoryStream(raw))
				return MetadataReference.CreateFromStream(mem);
		}
		catch (System.Exception e)
		{
			Logger.Error($"_getReferenceFromCache('{reference}') failed", e);
			return null;
		}
	}

	internal List<MetadataReference> _addReferences()
	{
		// add all cached references to the output list
		var references = new List<MetadataReference>();
		foreach (var reference in cachedReferences) references.Add(reference as MetadataReference);

		// goes through the requested references by the plugin
		foreach (var reference in References)
		{
			// checks if they are already cached
			if (string.IsNullOrEmpty(reference) ||
				cachedReferences.Any(x => x is MetadataReference metadata && metadata.Display.Contains(reference))) continue;

			try
			{
				// actually the method name is inducing error, it must load the asm
				// from disk as it was already marked as a cache miss
				var outReference = _getReferenceFromCache(reference);

				// redudant check for references.contains() ?
				if (outReference != null && !references.Contains(outReference)) references.Add(outReference);
			}
			catch { }
		}

		Logger.Debug($"ScriptCompilationThread using {references.Count} assembly references", 2);
		return references;
	}

	public class CompilerException : Exception
	{
		public string FilePath;
		public CompilerError Error;
		public CompilerException(string filePath, CompilerError error) { FilePath = filePath; Error = error; }

		public override string ToString()
		{
			return $"{Error.ErrorText}\n ({FilePath} {Error.Column} line {Error.Line})";
		}
	}

	public override void Start()
	{
		try
		{
			FileName = Path.GetFileNameWithoutExtension(FilePath);
			_doInit();
		}
		catch (Exception ex) { Logger.Error($"Couldn't compile '{FileName}'", ex); }

		base.Start();
	}

	public override void ThreadFunction()
	{
		try
		{
			Exceptions.Clear();

			TimeSinceCompile = DateTime.Now;

			var references = _addReferences();
			var trees = new List<SyntaxTree>();
			trees.Add(CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.Latest)));

			foreach (string require in Requires)
			{
				try
				{
					var requiredPlugin = _getPlugin(require);

					using (var dllStream = new MemoryStream(requiredPlugin))
					{
						references.Add(MetadataReference.CreateFromStream(dllStream));
					}
				}
				catch { /* do nothing */ }
			}

			var options = new CSharpCompilationOptions(
				OutputKind.DynamicallyLinkedLibrary,
				optimizationLevel: OptimizationLevel.Release,
				deterministic: true, warningLevel: 4
			);

			var compilation = CSharpCompilation.Create(
				$"Script.{FileName}.{Guid.NewGuid():N}", trees, references, options);

			using (var dllStream = new MemoryStream())
			{
				var emit = compilation.Emit(dllStream);

				foreach (var error in emit.Diagnostics)
				{
					var span = error.Location.GetMappedLineSpan().Span;
					switch (error.Severity)
					{
#if DEBUG_VERBOSE
							case DiagnosticSeverity.Warning:
								Logger.Warn($"Compile error {error.Id} '{FilePath}' @{span.Start.Line + 1}:{span.Start.Character + 1}" +
									Environment.NewLine + error.GetMessage(CultureInfo.InvariantCulture));
								break;
#endif
						case DiagnosticSeverity.Error:
#if DEBUG_VERBOSE
								Logger.Error($"Compile error {error.Id} '{FilePath}' @{span.Start.Line + 1}:{span.Start.Character + 1}" +
									Environment.NewLine + error.GetMessage(CultureInfo.InvariantCulture));
#endif
							Exceptions.Add(new CompilerException(FilePath,
								new CompilerError(FileName, span.Start.Line + 1, span.Start.Character + 1, error.Id, error.GetMessage(CultureInfo.InvariantCulture))));
							break;
					}
				}

				if (emit.Success)
				{
					var assembly = dllStream.ToArray();
					if (assembly != null)
					{
						_overridePlugin(FileName, assembly);
						Assembly = Assembly.Load(assembly);
					}
				}
			}

			if (Assembly == null)
			{
				throw null;
			}

			CompileTime = (float)(DateTime.Now - TimeSinceCompile).Milliseconds;

			references.Clear();
			references = null;
			trees.Clear();
			trees = null;

			foreach (var type in Assembly.GetTypes())
			{
				var hooks = new List<string>();
				var unsupportedHooks = new List<string>();
				var hookMethods = new List<HookMethodAttribute>();
				var pluginReferences = new List<PluginReferenceAttribute>();
				Hooks.Add(type, hooks);
				UnsupportedHooks.Add(type, unsupportedHooks);
				HookMethods.Add(type, hookMethods);
				PluginReferences.Add(type, pluginReferences);

				foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
				{
					if (HookValidator.IsIncompatibleOxideHook(method.Name))
					{
						unsupportedHooks.Add(method.Name);
					}

					if (Community.Runtime.HookProcessorEx.IsHookLoaded(method.Name))
					{
						if (!hooks.Contains(method.Name)) hooks.Add(method.Name);
					}
					else
					{
						var attribute = method.GetCustomAttribute<HookMethodAttribute>();
						if (attribute == null) continue;

						attribute.Method = method;
						hookMethods.Add(attribute);
					}
				}

				foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
				{
					var attribute = field.GetCustomAttribute<PluginReferenceAttribute>();
					if (attribute == null) continue;

					attribute.Field = field;
					pluginReferences.Add(attribute);
				}
			}

			if (Exceptions.Count > 0) throw null;
		}
		catch (Exception ex) { Logger.Error($"Threading compilation failed for '{FileName}'", ex); }
	}
}
