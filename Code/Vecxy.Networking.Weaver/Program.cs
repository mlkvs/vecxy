using Mono.Cecil;
using Mono.Cecil.Cil;
using ReflectionBindingFlags = System.Reflection.BindingFlags;
using System.Text;
using System.Text.Json;
using Vecxy.Networking;

if (args.Length is < 1 or > 3)
{
    Console.Error.WriteLine("Usage: Vecxy.Networking.Weaver <assembly> [VecxyTarget] [manifest]");
    return 2;
}

var assemblyPath = Path.GetFullPath(args[0]);
var target = args.Length > 1 ? args[1] : "Universal";
var manifestPath = args.Length > 2 ? Path.GetFullPath(args[2]) : Path.ChangeExtension(assemblyPath, ".rpc-manifest.json");
var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath)!);
var parameters = new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = File.Exists(pdbPath), InMemory = true };
using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, parameters);
if (assembly.CustomAttributes.Any(x => x.AttributeType.FullName == typeof(VecxyNetworkingWeavedAttribute).FullName))
{
    Console.WriteLine($"Vecxy Networking: already weaved {Path.GetFileName(assemblyPath)}");
    return 0;
}

var weaver = new NetworkingWeaver(assembly.MainModule, target);
var manifest = weaver.Execute();
var marker = assembly.MainModule.ImportReference(typeof(VecxyNetworkingWeavedAttribute).GetConstructor(Type.EmptyTypes)!);
assembly.CustomAttributes.Add(new CustomAttribute(marker));
var temp = assemblyPath + ".vecxy.tmp";
assembly.Write(temp, new WriterParameters { WriteSymbols = parameters.ReadSymbols });
File.Copy(temp, assemblyPath, true);
File.Delete(temp);
var tempPdb = Path.ChangeExtension(temp, ".pdb");
if (File.Exists(tempPdb)) { File.Copy(tempPdb, pdbPath, true); File.Delete(tempPdb); }
Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Vecxy Networking: weaved {manifest.Count} RPC(s) in {Path.GetFileName(assemblyPath)}");
return 0;

internal sealed class NetworkingWeaver(ModuleDefinition module, string target)
{
    private readonly List<RpcModel> _rpcs = [];
    private readonly MethodReference _isServer = module.ImportReference(typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.IsServer))!.GetMethod!);
    private readonly MethodReference _registerBase = module.ImportReference(typeof(NetworkBehaviour).GetMethod("__RegisterRpcs")!);
    private readonly MethodReference _registerRpc = module.ImportReference(typeof(INetworking).GetMethod(nameof(INetworking.RegisterRpc))!);
    private readonly MethodReference _rpcHandlerCtor = module.ImportReference(typeof(RpcHandler).GetConstructor(new[] { typeof(object), typeof(IntPtr) })!);
    private readonly MethodReference _descriptorCtor = module.ImportReference(typeof(RpcDescriptor).GetConstructors().Single());

    public List<object> Execute()
    {
        foreach (var type in AllTypes(module.Types).Where(IsNetworkBehaviour))
        {
            var models = new List<RpcModel>();
            foreach (var method in type.Methods.ToArray())
            {
                ApplySideGuard(method);
                var attribute = method.CustomAttributes.FirstOrDefault(x => x.AttributeType.Name is "ServerRpcAttribute" or "ClientRpcAttribute" or "TargetRpcAttribute");
                if (attribute is null) continue;
                var model = WeaveRpc(type, method, attribute);
                models.Add(model); _rpcs.Add(model);
            }
            if (models.Count > 0) AddRegistrationOverride(type, models);
        }
        return _rpcs.Select(x => (object)new
        {
            name = x.Signature, id = $"0x{x.Id:X8}", direction = x.Direction.ToString(),
            channel = x.Channel.ToString(), authority = x.RequireAuthority,
            parameters = x.Method.Parameters.Select(p => p.ParameterType.FullName).ToArray()
        }).ToList();
    }

    private RpcModel WeaveRpc(TypeDefinition type, MethodDefinition method, CustomAttribute attribute)
    {
        var direction = attribute.AttributeType.Name switch { "ServerRpcAttribute" => RpcDirection.Server, "ClientRpcAttribute" => RpcDirection.Client, _ => RpcDirection.Target };
        var signature = CanonicalSignature(method, direction);
        var id = XxHash32(signature);
        var channel = (RpcChannel)NamedInt(attribute, "Channel", 0);
        var authority = NamedBool(attribute, "RequireAuthority", true);
        var rpcTarget = (RpcTarget)NamedInt(attribute, "Target", 0);
        var body = CloneBody(type, method, $"__VecxyRpcBody_{id:X8}");
        ReplaceWithWrapper(method, body, id, direction, channel, rpcTarget);
        var handler = AddHandler(type, method, id, direction);
        return new(method, handler, id, signature, direction, channel, authority, rpcTarget);
    }

    private void ReplaceWithWrapper(MethodDefinition method, MethodDefinition body, uint id, RpcDirection direction, RpcChannel channel, RpcTarget rpcTarget)
    {
        method.Body = new MethodBody(method) { InitLocals = true };
        var il = method.Body.GetILProcessor();
        var send = Instruction.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, _isServer);
        if (direction == RpcDirection.Server) il.Emit(OpCodes.Brfalse, send); else il.Emit(OpCodes.Brtrue, send);
        EmitCallBody(il, method, body); il.Emit(OpCodes.Ret); il.Append(send);

        MethodReference? serializer = FindSerializer(id, "Serialize");
        if (direction == RpcDirection.Target) il.Emit(OpCodes.Ldarg_0);
        else il.Emit(OpCodes.Ldarg_0);
        if (direction == RpcDirection.Target) il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, unchecked((int)id));
        if (serializer is null) { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newarr, module.TypeSystem.Byte); }
        else
        {
            foreach (var parameter in PayloadParameters(method, direction)) il.Emit(OpCodes.Ldarg, parameter);
            il.Emit(OpCodes.Call, serializer);
        }
        il.Emit(OpCodes.Ldc_I4, (int)channel);
        if (direction == RpcDirection.Client) il.Emit(OpCodes.Ldc_I4, (int)rpcTarget);
        il.Emit(OpCodes.Call, SendMethod(direction));
        il.Emit(OpCodes.Ret);
    }

    private MethodDefinition AddHandler(TypeDefinition type, MethodDefinition wrapper, uint id, RpcDirection direction)
    {
        var handler = new MethodDefinition($"__VecxyRpcHandler_{id:X8}", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Void);
        handler.Parameters.Add(new ParameterDefinition("behaviour", ParameterAttributes.None, module.ImportReference(typeof(NetworkBehaviour))));
        handler.Parameters.Add(new ParameterDefinition("payload", ParameterAttributes.None, module.ImportReference(typeof(ReadOnlySpan<byte>))));
        handler.Parameters.Add(new ParameterDefinition("context", ParameterAttributes.None, module.ImportReference(typeof(RpcContext))));
        type.Methods.Add(handler);
        var il = handler.Body.GetILProcessor();
        var typed = new VariableDefinition(type); handler.Body.Variables.Add(typed); handler.Body.InitLocals = true;
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, type); il.Emit(OpCodes.Stloc, typed); il.Emit(OpCodes.Ldloc, typed);
        var deserializer = FindSerializer(id, "Deserialize");
        VariableDefinition? dto = null;
        if (deserializer is not null)
        {
            dto = new VariableDefinition(deserializer.ReturnType); handler.Body.Variables.Add(dto);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, deserializer); il.Emit(OpCodes.Stloc, dto);
        }
        foreach (var parameter in wrapper.Parameters)
        {
            if (direction == RpcDirection.Target && parameter.Index == 0)
            { il.Emit(OpCodes.Ldarga, handler.Parameters[2]); il.Emit(OpCodes.Call, module.ImportReference(typeof(RpcContext).GetProperty(nameof(RpcContext.Sender))!.GetMethod!)); continue; }
            if (parameter.ParameterType.FullName == typeof(RpcContext).FullName) { il.Emit(OpCodes.Ldarg_2); continue; }
            var property = dto!.VariableType.Resolve().Properties.Single(x => x.Name == $"P{parameter.Index}");
            il.Emit(OpCodes.Ldloc, dto); il.Emit(OpCodes.Callvirt, module.ImportReference(property.GetMethod));
        }
        il.Emit(OpCodes.Callvirt, wrapper); il.Emit(OpCodes.Ret);
        return handler;
    }

    private void AddRegistrationOverride(TypeDefinition type, IReadOnlyList<RpcModel> models)
    {
        var method = new MethodDefinition("__RegisterRpcs", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig, module.TypeSystem.Void);
        method.Parameters.Add(new ParameterDefinition("networking", ParameterAttributes.None, module.ImportReference(typeof(INetworking))));
        method.Overrides.Add(_registerBase); type.Methods.Add(method);
        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, _registerBase);
        foreach (var rpc in models)
        {
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, unchecked((int)rpc.Id)); il.Emit(OpCodes.Ldc_I4, (int)rpc.Direction);
            il.Emit(OpCodes.Ldc_I4, (int)rpc.Channel); il.Emit(rpc.RequireAuthority ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4, (int)rpc.Target); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldftn, rpc.Handler); il.Emit(OpCodes.Newobj, _rpcHandlerCtor);
            il.Emit(OpCodes.Newobj, _descriptorCtor); il.Emit(OpCodes.Callvirt, _registerRpc);
        }
        il.Emit(OpCodes.Ret);
    }

    private MethodDefinition CloneBody(TypeDefinition type, MethodDefinition source, string name)
    {
        var clone = new MethodDefinition(name, MethodAttributes.Private | MethodAttributes.HideBySig, source.ReturnType);
        foreach (var p in source.Parameters) clone.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));
        type.Methods.Add(clone); CloneMethodBody(source, clone); return clone;
    }

    private static void CloneMethodBody(MethodDefinition source, MethodDefinition target)
    {
        target.Body.InitLocals = source.Body.InitLocals; target.Body.MaxStackSize = source.Body.MaxStackSize;
        var variableMap = source.Body.Variables.Select(v => new VariableDefinition(v.VariableType)).ToArray();
        foreach (var v in variableMap) target.Body.Variables.Add(v);
        var map = new Dictionary<Instruction, Instruction>();
        foreach (var old in source.Body.Instructions) { var item = Instruction.Create(OpCodes.Nop); item.OpCode = old.OpCode; map[old] = item; target.Body.Instructions.Add(item); }
        foreach (var old in source.Body.Instructions)
        {
            var item = map[old]; item.Operand = old.Operand switch
            {
                Instruction i => map[i], Instruction[] a => a.Select(x => map[x]).ToArray(), VariableDefinition v => variableMap[v.Index],
                ParameterDefinition p => target.Parameters[p.Index], _ => old.Operand
            };
        }
        foreach (var old in source.Body.ExceptionHandlers) target.Body.ExceptionHandlers.Add(new ExceptionHandler(old.HandlerType)
        { CatchType = old.CatchType, TryStart = map[old.TryStart], TryEnd = old.TryEnd is null ? null : map[old.TryEnd], HandlerStart = map[old.HandlerStart], HandlerEnd = old.HandlerEnd is null ? null : map[old.HandlerEnd], FilterStart = old.FilterStart is null ? null : map[old.FilterStart] });
    }

    private void ApplySideGuard(MethodDefinition method)
    {
        var side = method.CustomAttributes.FirstOrDefault(x => x.AttributeType.Name is "ServerOnlyAttribute" or "ClientOnlyAttribute");
        if (side is null || !method.HasBody) return;
        var strip = side.AttributeType.Name == "ServerOnlyAttribute" ? target.Equals("Client", StringComparison.OrdinalIgnoreCase) : target.Equals("Server", StringComparison.OrdinalIgnoreCase);
        if (!strip) return;
        method.Body.Instructions.Clear(); var il = method.Body.GetILProcessor();
        if (method.ReturnType.MetadataType != MetadataType.Void)
        { var local = new VariableDefinition(method.ReturnType); method.Body.Variables.Add(local); method.Body.InitLocals = true; il.Emit(OpCodes.Ldloca, local); il.Emit(OpCodes.Initobj, method.ReturnType); il.Emit(OpCodes.Ldloc, local); }
        il.Emit(OpCodes.Ret);
    }

    private MethodReference? FindSerializer(uint id, string name) => module.Types.FirstOrDefault(x => x.Namespace == "Vecxy.Networking.Generated" && x.Name == $"RpcSerializer_{id:X8}")?.Methods.First(x => x.Name == name);
    private MethodReference SendMethod(RpcDirection direction)
    {
        var name = direction switch { RpcDirection.Server => "SendServerRpc", RpcDirection.Client => "SendClientRpc", _ => "SendTargetRpc" };
        return module.ImportReference(typeof(NetworkBehaviour).GetMethods(ReflectionBindingFlags.Instance | ReflectionBindingFlags.NonPublic).Single(x => x.Name == name && x.GetParameters().Any(p => p.ParameterType == typeof(byte[]))));
    }
    private static IEnumerable<ParameterDefinition> PayloadParameters(MethodDefinition method, RpcDirection direction) => method.Parameters.Where(p => !(direction == RpcDirection.Target && p.Index == 0) && p.ParameterType.FullName != typeof(RpcContext).FullName);
    private static void EmitCallBody(ILProcessor il, MethodDefinition wrapper, MethodDefinition body) { il.Emit(OpCodes.Ldarg_0); foreach (var p in wrapper.Parameters) il.Emit(OpCodes.Ldarg, p); il.Emit(OpCodes.Call, body); }
    private static bool IsNetworkBehaviour(TypeDefinition type) { for (var current = type.BaseType; current is not null;) { if (current.FullName == typeof(NetworkBehaviour).FullName) return true; try { current = current.Resolve()?.BaseType; } catch { return false; } } return false; }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots) { foreach (var type in roots) { yield return type; foreach (var nested in AllTypes(type.NestedTypes)) yield return nested; } }
    private static int NamedInt(CustomAttribute attribute, string name, int fallback) => attribute.Properties.FirstOrDefault(x => x.Name == name).Argument.Value is int value ? value : fallback;
    private static bool NamedBool(CustomAttribute attribute, string name, bool fallback) => attribute.Properties.FirstOrDefault(x => x.Name == name).Argument.Value is bool value ? value : fallback;
    private static string CanonicalSignature(MethodDefinition method, RpcDirection direction) => $"{method.DeclaringType.FullName.Replace('/', '.')}::{method.Name}({string.Join(",", method.Parameters.Select(x => TypeName(x.ParameterType)))}):{direction}Rpc";
    private static string TypeName(TypeReference type) => type.MetadataType switch { MetadataType.Boolean => "bool", MetadataType.Byte => "byte", MetadataType.SByte => "sbyte", MetadataType.Int16 => "short", MetadataType.UInt16 => "ushort", MetadataType.Int32 => "int", MetadataType.UInt32 => "uint", MetadataType.Int64 => "long", MetadataType.UInt64 => "ulong", MetadataType.Single => "float", MetadataType.Double => "double", MetadataType.Char => "char", MetadataType.String => "string", _ => type.FullName.Replace('/', '.') };
    private static uint XxHash32(string value) { var bytes = Encoding.UTF8.GetBytes(value); const uint p1=2654435761u,p2=2246822519u,p3=3266489917u,p4=668265263u,p5=374761393u; uint hash=p5+(uint)bytes.Length;var i=0;while(i<=bytes.Length-4){hash+=BitConverter.ToUInt32(bytes,i)*p3;hash=Rot(hash,17)*p4;i+=4;}while(i<bytes.Length){hash+=bytes[i++]*p5;hash=Rot(hash,11)*p1;}hash^=hash>>15;hash*=p2;hash^=hash>>13;hash*=p3;hash^=hash>>16;return hash; }
    private static uint Rot(uint v,int c)=>(v<<c)|(v>>(32-c));
    private sealed record RpcModel(MethodDefinition Method, MethodDefinition Handler, uint Id, string Signature, RpcDirection Direction, RpcChannel Channel, bool RequireAuthority, RpcTarget Target);
}
