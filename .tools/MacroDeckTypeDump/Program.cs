using System.Reflection;
using System.Runtime.Loader;

if (args.Length == 0)
{
	Console.Error.WriteLine("Usage: MacroDeckTypeDump <path-to-Macro-Deck-2.dll>");
	return 1;
}

var asmPath = args[0];
var baseDir = Path.GetDirectoryName(asmPath);
if (string.IsNullOrWhiteSpace(baseDir) || !File.Exists(asmPath))
{
	Console.Error.WriteLine("Assembly path not found.");
	return 1;
}

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
	var candidate = Path.Combine(baseDir, $"{name.Name}.dll");
	return File.Exists(candidate)
		? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate)
		: null;
};

var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(asmPath);
var allTypes = asm.GetTypes();

var interestingTypes = new[]
{
	"SuchByte.MacroDeck.Plugins.MacroDeckPlugin",
	"SuchByte.MacroDeck.Plugins.PluginAction",
	"SuchByte.MacroDeck.Plugins.PluginManifest",
	"SuchByte.MacroDeck.Plugins.PluginConfiguration",
	"SuchByte.MacroDeck.Icons.IconManager",
	"SuchByte.MacroDeck.Icons.Icon",
	"SuchByte.MacroDeck.Icons.IconPack",
	"SuchByte.MacroDeck.GUI.ButtonEditor",
	"SuchByte.MacroDeck.GUI.CustomControls.ActionConfigControl",
	"SuchByte.MacroDeck.ActionButton.ActionButton",
	"SuchByte.MacroDeck.Device.MacroDeckDevice",
	"SuchByte.MacroDeck.Device.DeviceManager",
	"SuchByte.MacroDeck.MacroDeck",
	"SuchByte.MacroDeck.Logging.MacroDeckLogger",
	"SuchByte.MacroDeck.CottleIntegration.TemplateManager",
	"SuchByte.MacroDeck.Extension.StringExtensions",
};

foreach (var typeName in interestingTypes)
{
	var type = allTypes.FirstOrDefault(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal));
	if (type == null)
	{
		Console.WriteLine($"[Missing] {typeName}");
		continue;
	}

	Console.WriteLine($"\n== {type.FullName} ==");

	var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
	foreach (var prop in props.OrderBy(p => p.Name))
	{
		Console.WriteLine($"P {prop.PropertyType.Name} {prop.Name}");
	}

	var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
	foreach (var ctor in ctors)
	{
		var parameters = string.Join(", ", ctor.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
		Console.WriteLine($"C {type.Name}({parameters})");
	}

	var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
	foreach (var method in methods.OrderBy(m => m.Name))
	{
		if (method.IsSpecialName) continue;
		var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
		var modifiers = new List<string>();
		if (method.IsAbstract) modifiers.Add("abstract");
		if (method.IsVirtual && !method.IsAbstract) modifiers.Add("virtual");
		if (method.IsStatic) modifiers.Add("static");
		var modifierText = modifiers.Count > 0 ? $" [{string.Join(",", modifiers)}]" : string.Empty;
		Console.WriteLine($"M {method.ReturnType.Name} {method.Name}({parameters}){modifierText}");
	}
}

return 0;
