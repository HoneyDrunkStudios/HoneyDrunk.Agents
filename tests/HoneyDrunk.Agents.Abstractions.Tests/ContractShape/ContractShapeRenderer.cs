using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace HoneyDrunk.Agents.Abstractions.Tests.ContractShape;

/// <summary>
/// Renders the public shape of a set of root types, plus every public type of the same assembly that their
/// signatures reach, as sorted text lines. Each line names its type, so a diff shows exactly what moved.
/// </summary>
internal static class ContractShapeRenderer
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Renders the shape: a <c># package:</c> and <c># roots:</c> header, then one sorted line per type and member.</summary>
    /// <param name="roots">The root types, all from one assembly.</param>
    /// <returns>The rendered shape.</returns>
    public static string Render(IReadOnlyList<Type> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (roots.Count == 0)
        {
            throw new ArgumentException("At least one root type is required.", nameof(roots));
        }

        var assembly = roots[0].Assembly;
        if (roots.Any(root => root.Assembly != assembly))
        {
            throw new ArgumentException("All root types must come from one assembly.", nameof(roots));
        }

        var context = new NullabilityInfoContext();
        var lines = new List<string>();
        foreach (var type in Closure(roots, assembly))
        {
            lines.Add($"{Name(type)} = {TypeDeclaration(type)}");
            lines.AddRange(MemberLines(type, context).Select(line => $"{Name(type)} {line}"));
        }

        var builder = new StringBuilder();
        builder.Append("# package: ").Append(assembly.GetName().Name).Append('\n');
        builder.Append("# roots: ").Append(string.Join(", ", roots.Select(Name).Order(StringComparer.Ordinal))).Append('\n');
        foreach (var line in lines.Order(StringComparer.Ordinal))
        {
            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    private static List<Type> Closure(IReadOnlyList<Type> roots, Assembly assembly)
    {
        var found = new HashSet<Type>();
        var queue = new Queue<Type>();
        foreach (var root in roots)
        {
            Visit(root, assembly, found, queue);
        }

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            foreach (var referenced in SignatureTypes(type))
            {
                Visit(referenced, assembly, found, queue);
            }
        }

        return [.. found];
    }

    private static void Visit(Type type, Assembly assembly, HashSet<Type> found, Queue<Type> queue)
    {
        if (type.HasElementType)
        {
            Visit(type.GetElementType()!, assembly, found, queue);
            return;
        }

        if (type.IsGenericParameter)
        {
            return;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                Visit(argument, assembly, found, queue);
            }

            type = type.GetGenericTypeDefinition();
        }

        if (type.Assembly == assembly && type.IsVisible && found.Add(type))
        {
            queue.Enqueue(type);
        }
    }

    private static IEnumerable<Type> SignatureTypes(Type type)
    {
        if (type.BaseType is not null)
        {
            yield return type.BaseType;
        }

        foreach (var contract in type.GetInterfaces())
        {
            yield return contract;
        }

        foreach (var member in VisibleMembers(type))
        {
            switch (member)
            {
                case MethodBase method:
                    if (method is MethodInfo info)
                    {
                        yield return info.ReturnType;
                    }

                    foreach (var parameter in method.GetParameters())
                    {
                        yield return parameter.ParameterType;
                    }

                    break;
                case PropertyInfo property:
                    yield return property.PropertyType;
                    foreach (var parameter in property.GetIndexParameters())
                    {
                        yield return parameter.ParameterType;
                    }

                    break;
                case FieldInfo field:
                    yield return field.FieldType;
                    break;
                case EventInfo @event when @event.EventHandlerType is not null:
                    yield return @event.EventHandlerType;
                    break;
            }
        }
    }

    private static IEnumerable<MemberInfo> VisibleMembers(Type type)
    {
        var accessors = new HashSet<MethodInfo>();
        foreach (var property in type.GetProperties(Declared))
        {
            accessors.UnionWith(property.GetAccessors(nonPublic: true));
            if (IsVisible(property.GetMethod) || IsVisible(property.SetMethod))
            {
                yield return property;
            }
        }

        foreach (var @event in type.GetEvents(Declared))
        {
            accessors.UnionWith(new[] { @event.AddMethod, @event.RemoveMethod, @event.RaiseMethod }.OfType<MethodInfo>());
            if (IsVisible(@event.AddMethod))
            {
                yield return @event;
            }
        }

        foreach (var constructor in type.GetConstructors(Declared).Where(constructor => IsVisible(constructor) && !IsCompilerGenerated(constructor)))
        {
            yield return constructor;
        }

        foreach (var method in type.GetMethods(Declared).Where(method => IsVisible(method) && !accessors.Contains(method) && !IsCompilerGenerated(method)))
        {
            yield return method;
        }

        foreach (var field in type.GetFields(Declared).Where(field => (field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly) && !field.IsSpecialName && !IsCompilerGenerated(field)))
        {
            yield return field;
        }
    }

    private static IEnumerable<string> MemberLines(Type type, NullabilityInfoContext context)
    {
        foreach (var member in VisibleMembers(type))
        {
            yield return member switch
            {
                ConstructorInfo constructor => $"ctor({Parameters(constructor.GetParameters(), context)})",
                MethodInfo method => MethodLine(method, context),
                PropertyInfo property => PropertyLine(property, context),
                FieldInfo field when type.IsEnum => $"value: {field.Name} = {Convert.ToInt64(field.GetRawConstantValue(), CultureInfo.InvariantCulture)}",
                FieldInfo field => FieldLine(field, context),
                EventInfo @event => $"event: {Name(@event.EventHandlerType!, context.Create(@event))} {@event.Name}",
                _ => throw new InvalidOperationException($"Unexpected member kind {member.MemberType}."),
            };
        }
    }

    private static string MethodLine(MethodInfo method, NullabilityInfoContext context)
    {
        var generics = method.IsGenericMethodDefinition ? $"<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>" : string.Empty;
        var returnType = Name(method.ReturnType, context.Create(method.ReturnParameter));
        return $"method: {Static(method.IsStatic)}{returnType} {method.Name}{generics}({Parameters(method.GetParameters(), context)})";
    }

    private static string PropertyLine(PropertyInfo property, NullabilityInfoContext context)
    {
        var accessors = new StringBuilder("{ ");
        if (IsVisible(property.GetMethod))
        {
            accessors.Append("get; ");
        }

        if (IsVisible(property.SetMethod))
        {
            var isInit = property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Any(modifier => modifier == typeof(IsExternalInit));
            accessors.Append(isInit ? "init; " : "set; ");
        }

        accessors.Append('}');
        var indexer = property.GetIndexParameters() is { Length: > 0 } index ? $"[{Parameters(index, context)}]" : string.Empty;
        var isStatic = (property.GetMethod ?? property.SetMethod)!.IsStatic;
        return $"property: {Static(isStatic)}{Name(property.PropertyType, context.Create(property))} {property.Name}{indexer} {accessors}";
    }

    private static string FieldLine(FieldInfo field, NullabilityInfoContext context)
    {
        var type = Name(field.FieldType, context.Create(field));
        if (field.IsLiteral)
        {
            return $"field: const {type} {field.Name} = {Literal(field.GetRawConstantValue(), field.FieldType)}";
        }

        var modifiers = Static(field.IsStatic) + (field.IsInitOnly ? "readonly " : string.Empty);
        return $"field: {modifiers}{type} {field.Name}";
    }

    private static string Parameters(ParameterInfo[] parameters, NullabilityInfoContext context) =>
        string.Join(", ", parameters.Select(parameter => Parameter(parameter, context)));

    private static string Parameter(ParameterInfo parameter, NullabilityInfoContext context)
    {
        var type = parameter.ParameterType;
        var modifier = string.Empty;
        if (type.IsByRef)
        {
            modifier = parameter.IsOut ? "out " : parameter.IsIn ? "in " : "ref ";
            type = type.GetElementType()!;
        }
        else if (parameter.IsDefined(typeof(ParamArrayAttribute), inherit: false))
        {
            modifier = "params ";
        }

        var nullability = context.Create(parameter);
        var text = $"{modifier}{Name(type, nullability, useWriteState: !parameter.IsOut)} {parameter.Name}";
        return parameter.HasDefaultValue ? $"{text} = {Literal(parameter.RawDefaultValue, type)}" : text;
    }

    private static string TypeDeclaration(Type type)
    {
        if (type.IsInterface)
        {
            return Inheritance("interface", null, type);
        }

        if (type.IsEnum)
        {
            return $"enum : {Name(Enum.GetUnderlyingType(type))}";
        }

        if (type.IsSubclassOf(typeof(Delegate)))
        {
            return "delegate";
        }

        var modifiers = new List<string>();
        if (type.IsValueType)
        {
            if (type.IsDefined(typeof(IsReadOnlyAttribute), inherit: false))
            {
                modifiers.Add("readonly");
            }

            if (IsRecord(type))
            {
                modifiers.Add("record");
            }

            modifiers.Add("struct");
            return Inheritance(string.Join(' ', modifiers), null, type);
        }

        if (type.IsAbstract && type.IsSealed)
        {
            modifiers.Add("static");
        }
        else if (type.IsAbstract)
        {
            modifiers.Add("abstract");
        }
        else if (type.IsSealed)
        {
            modifiers.Add("sealed");
        }

        if (IsRecord(type))
        {
            modifiers.Add("record");
        }

        modifiers.Add("class");
        return Inheritance(string.Join(' ', modifiers), type.BaseType, type);
    }

    private static string Inheritance(string kind, Type? baseType, Type type)
    {
        var parents = new List<string>();
        if (baseType is not null && baseType != typeof(object) && baseType != typeof(ValueType))
        {
            parents.Add(Name(baseType));
        }

        parents.AddRange(type.GetInterfaces().Select(contract => Name(contract)).Order(StringComparer.Ordinal));
        return parents.Count == 0 ? kind : $"{kind} : {string.Join(", ", parents)}";
    }

    private static bool IsRecord(Type type) =>
        type.GetMethod("PrintMembers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(StringBuilder)]) is not null;

    private static string Name(Type type) => Name(type, null);

    private static string Name(Type type, NullabilityInfo? nullability, bool useWriteState = false)
    {
        var state = nullability is null ? NullabilityState.Unknown : useWriteState ? nullability.WriteState : nullability.ReadState;
        var marker = !type.IsValueType && state == NullabilityState.Nullable ? "?" : string.Empty;

        if (type.IsGenericParameter)
        {
            return type.Name + marker;
        }

        if (type.IsArray)
        {
            return $"{Name(type.GetElementType()!, nullability?.ElementType)}[{new string(',', type.GetArrayRank() - 1)}]{marker}";
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return $"{Name(underlying, nullability?.GenericTypeArguments.FirstOrDefault())}?";
        }

        var prefix = type.IsNested ? Name(type.DeclaringType!) + "+" : type.Namespace is null ? string.Empty : type.Namespace + ".";
        if (!type.IsGenericType)
        {
            return prefix + type.Name + marker;
        }

        var arguments = type.GetGenericArguments();
        var argumentNames = arguments.Select((argument, index) =>
            Name(argument, nullability is not null && index < nullability.GenericTypeArguments.Length ? nullability.GenericTypeArguments[index] : null));
        var tick = type.Name.IndexOf('`', StringComparison.Ordinal);
        var simpleName = tick < 0 ? type.Name : type.Name[..tick];
        return $"{prefix}{simpleName}<{string.Join(", ", argumentNames)}>{marker}";
    }

    private static string Literal(object? value, Type type) => value switch
    {
        null => type.IsValueType ? "default" : "null",
        string text => $"\"{text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
        char character => $"'{character}'",
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };

    private static string Static(bool isStatic) => isStatic ? "static " : string.Empty;

    private static bool IsVisible(MethodBase? method) => method is not null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);

    private static bool IsCompilerGenerated(MemberInfo member) => member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);
}
