using System.Globalization;
using System.Reflection;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class PublicApiCompatibilityTests {
	[Fact]
	public void Public_surface_matches_reviewed_baseline() {
		string baseline = Path.Combine(AppContext.BaseDirectory, "PublicApiBaseline.txt");
		string[] expected = File.ReadAllLines(baseline).Where(line => line.Length != 0 && !line.StartsWith('#')).ToArray();
		string[] actual = Describe(typeof(PtyProcess).Assembly).ToArray();
		Assert.True(expected.SequenceEqual(actual, StringComparer.Ordinal),
			"Public API differs from packaging/PublicApiBaseline.txt.\nPUBLIC-API-ACTUAL-BEGIN\n" + string.Join("\n", actual) + "\nPUBLIC-API-ACTUAL-END");
	}

	private static IEnumerable<string> Describe(Assembly assembly) {
		List<string> lines = [];
		foreach (Type type in assembly.GetExportedTypes().OrderBy(TypeName, StringComparer.Ordinal)) {
			lines.Add("type " + TypeKind(type) + " " + TypeName(type));
			foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
				string value = field.IsLiteral ? " = " + Value(field.GetRawConstantValue()) : "";
				lines.Add("  field " + TypeName(field.FieldType) + " " + field.Name + value);
			}
			foreach (ConstructorInfo constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
				lines.Add("  ctor " + TypeName(type) + "(" + Parameters(constructor.GetParameters()) + ")");
			foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
				string access = (property.GetMethod?.IsPublic == true ? "get;" : "") + (property.SetMethod?.IsPublic == true ? "set;" : "");
				string index = property.GetIndexParameters().Length == 0 ? "" : "[" + Parameters(property.GetIndexParameters()) + "]";
				lines.Add("  property " + TypeName(property.PropertyType) + " " + property.Name + index + " { " + access + " }");
			}
			foreach (EventInfo @event in type.GetEvents(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
				lines.Add("  event " + TypeName(@event.EventHandlerType!) + " " + @event.Name);
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
				if (method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name.StartsWith("set_", StringComparison.Ordinal) ||
					method.Name.StartsWith("add_", StringComparison.Ordinal) || method.Name.StartsWith("remove_", StringComparison.Ordinal)) continue;
				string generic = method.IsGenericMethodDefinition ? "<" + string.Join(",", method.GetGenericArguments().Select(argument => argument.Name)) + ">" : "";
				lines.Add("  method " + TypeName(method.ReturnType) + " " + method.Name + generic + "(" + Parameters(method.GetParameters()) + ")");
			}
		}
		return lines.OrderBy(line => line, StringComparer.Ordinal);
	}

	private static string Parameters(IEnumerable<ParameterInfo> parameters) => string.Join(",", parameters.Select(parameter => {
		Type type = parameter.ParameterType;
		string modifier = parameter.IsOut ? "out " : type.IsByRef ? "ref " : "";
		if (type.IsByRef) type = type.GetElementType()!;
		string optional = parameter.HasDefaultValue ? "=" + Value(parameter.DefaultValue) : "";
		return modifier + TypeName(type) + " " + parameter.Name + optional;
	}));

	private static string TypeName(Type type) {
		if (type.IsGenericParameter) return type.Name;
		if (type.IsArray) return TypeName(type.GetElementType()!) + "[]";
		if (type.IsPointer) return TypeName(type.GetElementType()!) + "*";
		if (!type.IsGenericType) return (type.FullName ?? type.Name).Replace('+', '.');
		Type definition = type.GetGenericTypeDefinition();
		string name = (definition.FullName ?? definition.Name).Replace('+', '.');
		int marker = name.IndexOf('`'); if (marker >= 0) name = name[..marker];
		return name + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
	}

	private static string TypeKind(Type type) => type.IsEnum ? "enum" : type.IsValueType ? "struct" : type.IsInterface ? "interface" : type.IsAbstract && type.IsSealed ? "static-class" : type.IsAbstract ? "abstract-class" : type.IsSealed ? "sealed-class" : "class";
	private static string Value(object? value) => value switch {
		null => "null",
		string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
		char character => "'" + character.ToString() + "'",
		bool flag => flag ? "true" : "false",
		IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
		_ => value.ToString() ?? "null"
	};
}
