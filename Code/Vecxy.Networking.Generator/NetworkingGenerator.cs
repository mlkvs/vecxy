using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Vecxy.Networking.Generator;

[Generator]
public sealed class NetworkingGenerator : IIncrementalGenerator
{
    private const string NetworkBehaviour = "Vecxy.Networking.NetworkBehaviour";
    private static readonly DiagnosticDescriptor RpcStatic = Error("VECXYRPC001", "RPC method cannot be static", "RPC method '{0}' cannot be static.");
    private static readonly DiagnosticDescriptor RpcOwner = Error("VECXYRPC002", "RPC requires NetworkBehaviour", "RPC method '{0}' must be declared inside NetworkBehaviour.");
    private static readonly DiagnosticDescriptor RpcParameter = Error("VECXYRPC003", "Unsupported RPC parameter", "RPC method '{0}' has unsupported parameter '{1}'.");
    private static readonly DiagnosticDescriptor RpcCollision = Error("VECXYRPC004", "RPC ID collision", "RPC ID collision detected between '{0}' and '{1}'.");
    private static readonly DiagnosticDescriptor TargetParameter = Error("VECXYRPC005", "TargetRpc target missing", "TargetRpc '{0}' requires NetworkConnection as its first parameter.");
    private static readonly DiagnosticDescriptor RpcReturn = Error("VECXYRPC006", "RPC return value", "Synchronous RPC '{0}' cannot return a value.");
    private static readonly DiagnosticDescriptor NotSerializable = Error("VECXYRPC007", "RPC parameter is not serializable", "Type '{0}' cannot be serialized by MemoryPack. Annotate it with [MemoryPackable] or provide a MemoryPack formatter.");
    private static readonly DiagnosticDescriptor RpcGeneric = Error("VECXYRPC008", "Generic RPC", "Generic RPC method '{0}' is not supported.");
    private static readonly DiagnosticDescriptor NetworkedOwner = Error("VECXYNET001", "Networked requires NetworkBehaviour", "Networked member '{0}' must be declared inside NetworkBehaviour.");
    private static readonly DiagnosticDescriptor OnChanged = Error("VECXYNET002", "Invalid OnChanged", "Networked OnChanged callback '{0}' must return void and accept (oldValue, newValue) of type '{1}'.");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methods = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                static (ctx, _) => ctx.SemanticModel.GetDeclaredSymbol((MethodDeclarationSyntax)ctx.Node) as IMethodSymbol)
            .Where(static symbol => symbol is not null)!;
        var members = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is PropertyDeclarationSyntax or FieldDeclarationSyntax,
                static (ctx, _) => GetNetworkedMember(ctx))
            .Where(static symbol => symbol is not null)!;
        context.RegisterSourceOutput(methods.Collect().Combine(members.Collect()), Emit);
    }

    private static ISymbol? GetNetworkedMember(GeneratorSyntaxContext context)
    {
        if (context.Node is PropertyDeclarationSyntax property)
            return HasAttribute(context.SemanticModel.GetDeclaredSymbol(property), "NetworkedAttribute") ? context.SemanticModel.GetDeclaredSymbol(property) : null;
        var field = (FieldDeclarationSyntax)context.Node;
        var symbol = field.Declaration.Variables.Count == 1 ? context.SemanticModel.GetDeclaredSymbol(field.Declaration.Variables[0]) : null;
        return HasAttribute(symbol, "NetworkedAttribute") ? symbol : null;
    }

    private static void Emit(SourceProductionContext context, (ImmutableArray<IMethodSymbol?> Left, ImmutableArray<ISymbol?> Right) input)
    {
        var rpcs = new List<RpcInfo>();
        foreach (var method in input.Left.OfType<IMethodSymbol>())
        {
            var rpcAttribute = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name is "ServerRpcAttribute" or "ClientRpcAttribute" or "TargetRpcAttribute");
            if (rpcAttribute is null) continue;
            ValidateRpc(context, method, rpcAttribute, rpcs);
        }
        foreach (var group in rpcs.GroupBy(x => x.Id).Where(x => x.Count() > 1))
        {
            var values = group.ToArray();
            foreach (var value in values) context.ReportDiagnostic(Diagnostic.Create(RpcCollision, value.Method.Locations.FirstOrDefault(), value.Signature, values.First(x => x != value).Signature));
        }

        var networked = input.Right.OfType<ISymbol>().OrderBy(x => x.ContainingType.ToDisplayString()).ThenBy(x => x.Name).ToArray();
        ValidateNetworked(context, networked);
        var fingerprintInput = string.Join("\n", rpcs.OrderBy(x => x.Id).Select(x => $"{x.Id:X8}:{x.Signature}")) + "\n" +
                               string.Join("\n", networked.Select((x, i) => $"{i}:{x.ContainingType.ToDisplayString()}.{x.Name}:{MemberType(x).ToDisplayString()}"));
        var fingerprint = XxHash32(fingerprintInput);
        var source = new StringBuilder("// <auto-generated />\nnamespace Vecxy.Networking.Generated;\ninternal static class VecxyGeneratedNetworkMetadata\n{\n")
            .Append("    public const uint ProtocolFingerprint = 0x").Append(fingerprint.ToString("X8")).Append("u;\n")
            .Append("    public static readonly (uint Id, string Signature, byte Direction)[] Rpcs =\n    [\n");
        foreach (var rpc in rpcs.OrderBy(x => x.Id)) source.Append("        (0x").Append(rpc.Id.ToString("X8")).Append("u, @\"").Append(rpc.Signature.Replace("\"", "\"\"")).Append("\", ").Append((byte)rpc.Direction).Append("),\n");
        source.Append("    ];\n}\n");
        foreach (var rpc in rpcs.Where(x => x.Method.Parameters.Any(p => p.Type.ToDisplayString() != "Vecxy.Networking.RpcContext" &&
                                                                        !(x.Direction == RpcDirection.Target && p.Ordinal == 0))))
        {
            var parameters = rpc.Method.Parameters.Where(p => p.Type.ToDisplayString() != "Vecxy.Networking.RpcContext" &&
                                                               !(rpc.Direction == RpcDirection.Target && p.Ordinal == 0)).ToArray();
            source.Append("internal sealed class RpcPayload_").Append(rpc.Id.ToString("X8")).Append("\n{\n");
            foreach (var parameter in parameters)
                source.Append("    public ").Append(parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append(" P").Append(parameter.Ordinal).Append(" { get; set; }\n");
            source.Append("}\ninternal static class RpcSerializer_").Append(rpc.Id.ToString("X8")).Append("\n{\n    public static byte[] Serialize(");
            source.Append(string.Join(", ", parameters.Select(p => $"{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} p{p.Ordinal}")));
            source.Append(")\n    {\n");
            foreach (var parameter in parameters)
                source.Append("        var b").Append(parameter.Ordinal).Append(" = global::MemoryPack.MemoryPackSerializer.Serialize(p").Append(parameter.Ordinal).Append(");\n");
            source.Append("        var result = new byte[").Append(parameters.Length * 4).Append(" + ")
                .Append(string.Join(" + ", parameters.Select(p => $"b{p.Ordinal}.Length"))).Append("];\n        var offset = 0;\n");
            foreach (var parameter in parameters)
            {
                source.Append("        global::System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset, 4), b").Append(parameter.Ordinal).Append(".Length); offset += 4;\n")
                    .Append("        b").Append(parameter.Ordinal).Append(".CopyTo(result.AsSpan(offset)); offset += b").Append(parameter.Ordinal).Append(".Length;\n");
            }
            source.Append("        return result;\n    }\n    public static RpcPayload_").Append(rpc.Id.ToString("X8")).Append(" Deserialize(global::System.ReadOnlySpan<byte> payload)\n    {\n        var offset = 0;\n        var result = new RpcPayload_").Append(rpc.Id.ToString("X8")).Append("();\n");
            foreach (var parameter in parameters)
            {
                source.Append("        if (payload.Length - offset < 4) throw new global::MemoryPack.MemoryPackSerializationException(\"Truncated RPC payload.\");\n")
                    .Append("        var length").Append(parameter.Ordinal).Append(" = global::System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, 4)); offset += 4;\n")
                    .Append("        if (length").Append(parameter.Ordinal).Append(" < 0 || length").Append(parameter.Ordinal).Append(" > payload.Length - offset) throw new global::MemoryPack.MemoryPackSerializationException(\"Invalid RPC payload length.\");\n")
                    .Append("        result.P").Append(parameter.Ordinal).Append(" = global::MemoryPack.MemoryPackSerializer.Deserialize<")
                    .Append(parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append(">(payload.Slice(offset, length").Append(parameter.Ordinal).Append("))!; offset += length").Append(parameter.Ordinal).Append(";\n");
            }
            source.Append("        if (offset != payload.Length) throw new global::MemoryPack.MemoryPackSerializationException(\"Trailing RPC payload data.\");\n        return result;\n    }\n}\n");
        }
        context.AddSource("VecxyGeneratedNetworkMetadata.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static void ValidateRpc(SourceProductionContext context, IMethodSymbol method, AttributeData attribute, List<RpcInfo> rpcs)
    {
        var location = method.Locations.FirstOrDefault();
        if (method.IsStatic) context.ReportDiagnostic(Diagnostic.Create(RpcStatic, location, method.Name));
        if (!Inherits(method.ContainingType, NetworkBehaviour)) context.ReportDiagnostic(Diagnostic.Create(RpcOwner, location, method.Name));
        if (!method.ReturnsVoid) context.ReportDiagnostic(Diagnostic.Create(RpcReturn, location, method.Name));
        if (method.IsGenericMethod) context.ReportDiagnostic(Diagnostic.Create(RpcGeneric, location, method.Name));
        var direction = attribute.AttributeClass!.Name switch { "ServerRpcAttribute" => RpcDirection.Server, "ClientRpcAttribute" => RpcDirection.Client, _ => RpcDirection.Target };
        if (direction == RpcDirection.Target && (method.Parameters.Length == 0 || method.Parameters[0].Type.ToDisplayString() != "Vecxy.Networking.NetworkConnection"))
            context.ReportDiagnostic(Diagnostic.Create(TargetParameter, location, method.Name));
        foreach (var parameter in method.Parameters.Skip(direction == RpcDirection.Target ? 1 : 0))
        {
            if (parameter.RefKind != RefKind.None) context.ReportDiagnostic(Diagnostic.Create(RpcParameter, parameter.Locations.FirstOrDefault(), method.Name, parameter.Name));
            if (parameter.Type.ToDisplayString() == "Vecxy.Networking.RpcContext") continue;
            if (!IsMemoryPackSerializable(parameter.Type)) context.ReportDiagnostic(Diagnostic.Create(NotSerializable, parameter.Locations.FirstOrDefault(), parameter.Type.ToDisplayString()));
        }
        var signature = CanonicalSignature(method, direction);
        rpcs.Add(new RpcInfo(method, XxHash32(signature), signature, direction));
    }

    private static void ValidateNetworked(SourceProductionContext context, IReadOnlyList<ISymbol> members)
    {
        foreach (var member in members)
        {
            if (!Inherits(member.ContainingType, NetworkBehaviour)) context.ReportDiagnostic(Diagnostic.Create(NetworkedOwner, member.Locations.FirstOrDefault(), member.Name));
            var attribute = member.GetAttributes().First(a => a.AttributeClass?.Name == "NetworkedAttribute");
            var callback = attribute.NamedArguments.FirstOrDefault(x => x.Key == "OnChanged").Value.Value as string;
            if (string.IsNullOrEmpty(callback)) continue;
            var type = MemberType(member);
            var valid = member.ContainingType.GetMembers(callback).OfType<IMethodSymbol>().Any(x => !x.IsStatic && x.ReturnsVoid && x.Parameters.Length == 2 &&
                SymbolEqualityComparer.Default.Equals(x.Parameters[0].Type, type) && SymbolEqualityComparer.Default.Equals(x.Parameters[1].Type, type));
            if (!valid) context.ReportDiagnostic(Diagnostic.Create(OnChanged, member.Locations.FirstOrDefault(), callback, type.ToDisplayString()));
        }
    }

    private static bool IsMemoryPackSerializable(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum || type.NullableAnnotation == NullableAnnotation.Annotated) return true;
        if (type.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_String) return true;
        var name = type.ToDisplayString();
        if (name is "System.Guid" or "System.Numerics.Vector2" or "System.Numerics.Vector3" or "System.Numerics.Vector4" or "System.Numerics.Quaternion") return true;
        if (type is IArrayTypeSymbol array) return IsMemoryPackSerializable(array.ElementType);
        return type.GetAttributes().Any(a => a.AttributeClass?.Name == "MemoryPackableAttribute");
    }

    private static string CanonicalSignature(IMethodSymbol method, RpcDirection direction) =>
        $"{method.ContainingType.ToDisplayString()}::{method.Name}({string.Join(",", method.Parameters.Select(x => x.Type.ToDisplayString()))}):{direction}Rpc";
    private static bool Inherits(INamedTypeSymbol? type, string fullName)
    { for (var current = type; current is not null; current = current.BaseType) if (current.ToDisplayString() == fullName) return true; return false; }
    private static bool HasAttribute(ISymbol? symbol, string name) => symbol?.GetAttributes().Any(x => x.AttributeClass?.Name == name) == true;
    private static ITypeSymbol MemberType(ISymbol symbol) => symbol is IPropertySymbol property ? property.Type : ((IFieldSymbol)symbol).Type;
    private static DiagnosticDescriptor Error(string id, string title, string message) => new(id, title, message, "Vecxy.Networking", DiagnosticSeverity.Error, true);

    private static uint XxHash32(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value); const uint p1 = 2654435761u, p2 = 2246822519u, p3 = 3266489917u, p4 = 668265263u, p5 = 374761393u;
        uint hash = p5 + (uint)bytes.Length; var index = 0;
        while (index <= bytes.Length - 4) { var lane = BitConverter.ToUInt32(bytes, index); hash += lane * p3; hash = RotateLeft(hash, 17) * p4; index += 4; }
        while (index < bytes.Length) { hash += bytes[index++] * p5; hash = RotateLeft(hash, 11) * p1; }
        hash ^= hash >> 15; hash *= p2; hash ^= hash >> 13; hash *= p3; hash ^= hash >> 16; return hash;
    }
    private static uint RotateLeft(uint value, int count) => (value << count) | (value >> (32 - count));
    private enum RpcDirection : byte { Server, Client, Target }
    private sealed record RpcInfo(IMethodSymbol Method, uint Id, string Signature, RpcDirection Direction);
}
