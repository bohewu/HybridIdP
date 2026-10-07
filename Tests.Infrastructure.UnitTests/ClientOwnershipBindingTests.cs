using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Core.Domain.Events;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public class ClientOwnershipBindingTests
{
    [Fact]
    public async Task Ownership_ShouldFollowApplicationAcrossRenameAndDenyReusedClientId()
    {
        await using var fixture = await Fixture.CreateAsync();
        var owner = await fixture.AddPersonAsync();
        var other = await fixture.AddPersonAsync();
        var first = await fixture.Service.CreateClientAsync(Request("original"), owner);
        var firstId = Guid.Parse(first.Id);
        await fixture.Service.UpdateClientAsync(firstId, new UpdateClientRequest(
            "renamed", null, null, null, null, null, null, null, null));
        var second = await fixture.Service.CreateClientAsync(Request("original"), other);
        var secondId = Guid.Parse(second.Id);

        Assert.True(await fixture.Service.IsClientOwnedByPersonAsync(firstId, owner));
        Assert.False(await fixture.Service.IsClientOwnedByPersonAsync(secondId, owner));
        Assert.True(await fixture.Service.IsClientOwnedByPersonAsync(secondId, other));
        var (items, count) = await fixture.Service.GetClientsAsync(0, 25, null, null, null, owner);
        Assert.Equal(1, count);
        Assert.Equal(first.Id, Assert.Single(items).Id);
        await fixture.Service.DeleteClientAsync(firstId);
        Assert.DoesNotContain(fixture.Context.ClientOwnerships, row => row.ApplicationId == firstId);
        Assert.True(await fixture.Service.IsClientOwnedByPersonAsync(secondId, other));
    }

    [Fact]
    public async Task Ownership_ShouldDenyUnboundLegacyRowEvenWhenIdentifierMatches()
    {
        await using var fixture = await Fixture.CreateAsync();
        var owner = await fixture.AddPersonAsync();
        fixture.Context.ClientOwnerships.Add(new ClientOwnership
        {
            ClientId = "legacy", CreatedByPersonId = owner, CreatedAt = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();
        var client = await fixture.Service.CreateClientAsync(Request("legacy"));
        Assert.False(await fixture.Service.IsClientOwnedByPersonAsync(Guid.Parse(client.Id), owner));
        var (items, _) = await fixture.Service.GetClientsAsync(0, 25, null, null, null, owner);
        Assert.Empty(items);
    }

    [Fact]
    public async Task CreateClient_ShouldRollBackApplicationAndCacheWhenOwnershipCannotBeSaved()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.CreateClientAsync(
            Request("failed-create"), Guid.NewGuid())); // Missing Person violates the real FK.
        Assert.Null(await fixture.Manager.FindByClientIdAsync("failed-create"));
        Assert.Empty(fixture.Context.ClientOwnerships.AsNoTracking());
        var owner = await fixture.AddPersonAsync();
        var created = await fixture.Service.CreateClientAsync(Request("failed-create"), owner);
        Assert.True(await fixture.Service.IsClientOwnedByPersonAsync(Guid.Parse(created.Id), owner));
    }

    private static CreateClientRequest Request(string id) => new(
        id, null, null, null, null, null, new List<string> { "https://client.example/callback" }, null, null, null);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        public ApplicationDbContext Context { get; }
        public IOpenIddictApplicationManager Manager { get; }
        public ClientService Service { get; }

        private Fixture(SqliteConnection connection, ServiceProvider provider, AsyncServiceScope scope)
        {
            _connection = connection;
            _provider = provider;
            _scope = scope;
            Context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            Service = new ClientService(Manager, Mock.Of<IDomainEventPublisher>(), Context,
                Mock.Of<IOpenIddictScopeManager>(), Options.Create(new RedirectUriSecurityPolicyOptions()), Moq.Mock.Of<global::Infrastructure.Authorization.IApiScopeUsagePolicy>());
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection().AddLogging();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection).UseOpenIddict<Guid>());
            services.AddOpenIddict().AddCore(options => options.UseEntityFrameworkCore()
                .UseDbContext<ApplicationDbContext>().ReplaceDefaultEntities<Guid>());
            var provider = services.BuildServiceProvider();
            var fixture = new Fixture(connection, provider, provider.CreateAsyncScope());
            await fixture.Context.Database.EnsureCreatedAsync();
            return fixture;
        }

        public async Task<Guid> AddPersonAsync()
        {
            var person = new Person { Id = Guid.NewGuid() };
            Context.Persons.Add(person);
            await Context.SaveChangesAsync();
            return person.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
