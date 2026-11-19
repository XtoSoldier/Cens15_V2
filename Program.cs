using CENS15_V2.Data;
using CENS15_V2.Services.Interfaces;
using CENS15_V2.Services;
using Microsoft.EntityFrameworkCore;
using CENS15_V2.Helper;
using CENS15.V2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.FileProviders;
using System.Text;
using System.Text.Json.Serialization;
using CENS15_V2.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers(options =>
    {
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Ingresá el token JWT así: Bearer {tu_token}"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwt = builder.Configuration.GetSection("Jwt");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt["Key"])),

            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SessionAuthentication")
                    .LogWarning("JWT rechazado ({FailureType}). TraceId: {TraceId}",
                        ctx.Exception.GetType().Name, ctx.HttpContext.TraceIdentifier);
                return Task.CompletedTask;
            },
            OnTokenValidated = async ctx =>
            {
                var epoch = ctx.HttpContext.RequestServices.GetRequiredService<SessionEpoch>();
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SessionAuthentication");
                if (!await SessionPrincipalValidator.ValidateAsync(ctx.Principal, db, epoch.Current, jwt["Key"]!, ctx.HttpContext.RequestAborted,
                    reason => logger.LogWarning("Sesion rechazada ({Reason}). TraceId: {TraceId}", reason, ctx.HttpContext.TraceIdentifier)))
                    ctx.Fail("Sesion invalidada. Ingrese nuevamente.");
            }
        };
    });
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddAuthorization(options =>
{
    foreach (var responsibility in ResponsibilityPolicies.All)
    {
        options.AddPolicy(responsibility, policy =>
            policy.RequireAssertion(context =>
                ResponsibilityPolicies.HasResponsibility(context.User, responsibility)));
    }
});
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection."))
);
builder.Services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddHttpClient<IEmailService, EmailJsService>();
builder.Services.AddScoped<PasswordHasher>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IResponsibilityService, ResponsibilityService>();
builder.Services.AddScoped<IAlumnoService, AlumnoService>();
builder.Services.AddScoped<IAlumnoDocumentoService, AlumnoDocumentoService>();
builder.Services.AddScoped<ITipoDocumentoAlumnoService, TipoDocumentoAlumnoService>();
builder.Services.AddScoped<IOrientacionService, OrientacionService>();
builder.Services.AddScoped<ICursoService, CursoService>();
builder.Services.AddScoped<IAnexoService, AnexoService>();
builder.Services.AddScoped<IHorarioAnexoService, HorarioAnexoService>();
builder.Services.AddScoped<IMateriaService, MateriaService>();
builder.Services.AddScoped<IDocenteService, DocenteService>();
builder.Services.AddScoped<IPreceptorService, PreceptorService>();
builder.Services.AddScoped<IInscripcionService, InscripcionService>();
builder.Services.AddScoped<ICursadaMateriaService, CursadaMateriaService>();
builder.Services.AddScoped<ICalificacionService, CalificacionService>();
builder.Services.AddScoped<ICertificadoTemplateService, CertificadoTemplateService>();
builder.Services.AddScoped<ILoginActivityService, LoginActivityService>();
builder.Services.AddScoped<TransferenciaService>();
builder.Services.AddSingleton<CheckpointGate>();
builder.Services.AddSingleton<CheckpointStorage>();
builder.Services.AddSingleton<SessionEpoch>();
builder.Services.AddSingleton<CheckpointPreviews>();
builder.Services.AddScoped<CheckpointModel>();
builder.Services.AddScoped<CheckpointRecovery>();
builder.Services.AddScoped<CheckpointService>();
builder.Services.AddSingleton<StartupReadiness>();
builder.Services.AddScoped<IDatabaseSchemaProbe, DatabaseSchemaProbe>();
builder.Services.AddScoped<DatabaseMaintenance>();
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    var fallback = options.InvalidModelStateResponseFactory;
    options.InvalidModelStateResponseFactory = context =>
        context.HttpContext.Request.Path.StartsWithSegments("/api/checkpoints", StringComparison.OrdinalIgnoreCase)
            ? new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { message = "Solicitud de checkpoint invalida o incompleta." })
            : fallback(context);
});

await using var app = builder.Build();

// Maintenance is a console-only operation, never enabled by settings or HTTP requests.
if (args.Any(arg => arg.Equals("--maintenance", StringComparison.OrdinalIgnoreCase)
    || arg.StartsWith("--maintenance=", StringComparison.OrdinalIgnoreCase)))
{
    var operation = builder.Configuration["maintenance"];
    var database = builder.Configuration["confirm-database"];
    if (operation is not ("migrate" or "seed") || string.IsNullOrWhiteSpace(database))
    {
        Console.Error.WriteLine("Uso: --maintenance migrate|seed --confirm-database NOMBRE_BD. La API debe estar detenida.");
        Environment.ExitCode = 2;
        return;
    }
    await using var scope = app.Services.CreateAsyncScope();
    try
    {
        await scope.ServiceProvider.GetRequiredService<DatabaseMaintenance>().RunAsync(operation, database);
        Console.WriteLine("Mantenimiento completado. Inicie la API sin --maintenance.");
    }
    catch (Exception ex)
    {
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMaintenance")
            .LogError("Mantenimiento rechazado o fallido: {ExceptionType}. Verifique configuracion, permisos y recuperacion pendiente.", ex.GetType().Name);
        Console.Error.WriteLine("No se completo el mantenimiento. No se iniciara el servidor HTTP.");
        Environment.ExitCode = 1;
    }
    return;
}

await ApplicationStartup.InitializeAsync(app.Services, builder.Configuration, app.Logger, app.Lifetime.ApplicationStopping);

// Diagnostics remain independent of database, checkpoint gate and JWT handlers.
app.UseCors("AllowAll");
app.UseMiddleware<StartupReadinessMiddleware>();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
    app.UseSwagger();
    app.UseSwaggerUI();
//}
app.UseMiddleware<CheckpointGateMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

var uploadsRoot = Path.Combine(
    app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
    "uploads");
app.UseDefaultFiles();
app.UseStaticFiles();
if (Directory.Exists(uploadsRoot)) app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRoot),
    RequestPath = "/uploads"
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
