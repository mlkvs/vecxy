using Autofac;
using Vecxy.Kernel;

namespace Vecxy.Networking;

public sealed class NetworkingModule : IModule, IModule.IUpdatable, INetworking
{
    public sealed class Definition : AModuleDefinition<NetworkingModule>
    {
        private readonly NetworkingOptions _options;
        private readonly INetworkTransport? _transport;

        protected override IReadOnlyList<Type> Exports => [typeof(INetworking)];

        public Definition(NetworkingOptions? options = null, INetworkTransport? transport = null)
        { _options = options ?? new NetworkingOptions(); _transport = transport; }

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder.RegisterInstance(_options).AsSelf().SingleInstance();
            builder.RegisterInstance(_transport ?? new NullNetworkTransport()).As<INetworkTransport>().SingleInstance();
            builder.RegisterType<RpcRegistry>().AsSelf().SingleInstance();
            builder.RegisterType<NetworkRuntime>().AsSelf().SingleInstance();
            builder.RegisterType<NetworkingModule>().AsSelf().SingleInstance();
        }
    }

    private readonly NetworkRuntime _runtime;
    public NetworkingModule(NetworkRuntime runtime) => _runtime = runtime;
    public NetworkRole Role => _runtime.Role;
    public bool IsServer => _runtime.IsServer;
    public bool IsClient => _runtime.IsClient;
    public NetworkConnection? LocalConnection => _runtime.LocalConnection;
    public NetworkConnection? ServerConnection => _runtime.ServerConnection;
    public event Action<NetworkConnection>? Connected { add => _runtime.Connected += value; remove => _runtime.Connected -= value; }
    public event Action<NetworkConnection>? Disconnected { add => _runtime.Disconnected += value; remove => _runtime.Disconnected -= value; }
    public Task StartServerAsync(int port, CancellationToken cancellationToken = default) => _runtime.StartServerAsync(port, cancellationToken);
    public Task StartHostAsync(int port, CancellationToken cancellationToken = default) => _runtime.StartHostAsync(port, cancellationToken);
    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default) => _runtime.ConnectAsync(host, port, cancellationToken);
    public void Configure(NetworkRole role, NetworkConnection? local = null, NetworkConnection? server = null) =>
        _runtime.Configure(role, local, server);
    public NetworkObject CreateObject(NetworkObjectId id, NetworkConnection? owner = null) => _runtime.CreateObject(id, owner);
    public bool TryGetObject(NetworkObjectId id, out NetworkObject? networkObject) => _runtime.TryGetObject(id, out networkObject);
    public void RegisterRpc(RpcDescriptor descriptor) => _runtime.RegisterRpc(descriptor);
    public void OnInitialize() => _runtime.OnInitialize();
    public void OnUpdate(float deltaTime) => _runtime.OnUpdate(deltaTime);
    public void OnShutdown() => _runtime.OnShutdown();
    public void Dispose() { }
}
