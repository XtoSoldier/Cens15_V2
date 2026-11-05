using System.Reflection;
using System.Text.Json;
using CENS15_V2.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CENS15_V2.Tests;

public sealed class HealthTests
{
    [Fact]
    public void ProbeIsAnonymousGetAndDoesNotCacheOrRequireDependencies()
    {
        var type = typeof(HealthController);
        Assert.NotNull(type.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("api/health", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.NotNull(type.GetMethod(nameof(HealthController.Get))!.GetCustomAttribute<HttpGetAttribute>());
        Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        var result = Assert.IsType<OkObjectResult>(new HealthController().Get());
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("{\"status\":\"ok\"}", JsonSerializer.Serialize(result.Value));
    }
}
