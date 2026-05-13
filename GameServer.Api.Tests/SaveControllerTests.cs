using System.Text.Json.Nodes;
using GameServer.Api.Controllers;
using GameServer.Api.Models.Domain;
using GameServer.Api.Models.Dto.Save;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class SaveControllerTests
{
    [Fact]
    public async Task Load_WhenSaveExists_ReturnsDataAsJsonNode()
    {
        var service = new StubSaveService
        {
            Record = new SaveRecord
            {
                Data = "{\"Meta\":{\"SchemaVersion\":2},\"Inventory\":{\"InventoryItems\":{}}}",
                LastModified = 123
            }
        };

        var controller = new SaveController(NullLogger<SaveController>.Instance, service);

        var result = await controller.Load("p1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<SaveLoadResponse>(ok.Value);
        var dataObject = Assert.IsType<JsonObject>(payload.Data);
        Assert.Equal(2, dataObject["Meta"]!["SchemaVersion"]!.GetValue<int>());
        Assert.Equal(123, payload.LastModified);
    }

    private sealed class StubSaveService : ISaveService
    {
        public SaveRecord? Record { get; set; }

        public Task SaveAsync(string playerId, string data, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<SaveRecord> LoadAsync(string playerId, CancellationToken cancellationToken)
        {
            if (Record is null)
            {
                throw new KeyNotFoundException();
            }

            return Task.FromResult(Record);
        }

        public Task DeleteAsync(string playerId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<long> GetLastModifiedTimestampAsync(string playerId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
