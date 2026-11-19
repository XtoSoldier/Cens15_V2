using System.Text.Json;
using System.Text.Json.Serialization;
using CENS15_V2.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace CENS15_V2.Tests;

public class SwaggerDocumentTests
{
    [Fact]
    public void AllControllersGenerateSwaggerWithCheckpointMultipartFields()
    {
        // Register MVC metadata only: never execute Program, migrations, seeding or endpoints.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Services.AddControllers(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
            .AddApplicationPart(typeof(CheckpointsController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new OpenApiInfo { Title = "CENS15", Version = "v1" }));
        using var provider = builder.Services.BuildServiceProvider();

        var document = provider.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        Assert.NotEmpty(document.Paths);
        var operation = document.Paths["/api/checkpoints/importar"].Operations[OperationType.Post];
        var schema = operation.RequestBody.Content["multipart/form-data"].Schema;
        if (schema.Reference != null) schema = document.Components.Schemas[schema.Reference.Id];
        Assert.Equal("string", schema.Properties["archivo"].Type);
        Assert.Equal("binary", schema.Properties["archivo"].Format);
        Assert.Equal("string", schema.Properties["clave"].Type);
        Assert.DoesNotContain("ct", schema.Properties.Keys);

        using var buffer = new StringWriter();
        document.SerializeAsV3(new OpenApiJsonWriter(buffer));
        using var json = JsonDocument.Parse(buffer.ToString());
        Assert.True(json.RootElement.GetProperty("paths").TryGetProperty("/api/checkpoints/importar", out _));
    }
}
