using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Echo.Infrastructure;

/// <summary>
///     Lets the command line framework create commands through the application's dependency injection container.
/// </summary>
/// <param name="services"><see cref="IServiceCollection"/></param>
internal sealed class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    private readonly IServiceCollection _services = services;

    /// <inheritdoc/>
    public ITypeResolver Build() =>
        new TypeResolver(_services.BuildServiceProvider());

    /// <inheritdoc/>
    public void Register(Type service, Type implementation) =>
        _services.AddSingleton(service, implementation);

    /// <inheritdoc/>
    public void RegisterInstance(Type service, object implementation) =>
        _services.AddSingleton(service, implementation);

    /// <inheritdoc/>
    public void RegisterLazy(Type service, Func<object> factory) =>
        _services.AddSingleton(service, _ => factory());

    private sealed class TypeResolver(ServiceProvider provider) : ITypeResolver, IDisposable
    {
        private readonly ServiceProvider _provider = provider;

        public object? Resolve(Type? type) =>
            type is null ? null : _provider.GetService(type);

        public void Dispose() =>
            _provider.Dispose();
    }
}