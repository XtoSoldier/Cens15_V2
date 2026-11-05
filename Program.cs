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
                Console.WriteLine("JWT validation failed.");
                return Task.CompletedTask;
            },
            OnTokenValidated = async ctx =>
            {
                var epoch = ctx.HttpContext.RequestServices.GetRequiredService<SessionEpoch>();
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                if (!await SessionPrincipalValidator.ValidateAsync(ctx.Principal, db, epoch.Current, jwt["Key"]!, ctx.HttpContext.RequestAborted))
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
        ?? AppDbContextFactory.DefaultConnectionString)
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
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    var fallback = options.InvalidModelStateResponseFactory;
    options.InvalidModelStateResponseFactory = context =>
        context.HttpContext.Request.Path.StartsWithSegments("/api/checkpoints", StringComparison.OrdinalIgnoreCase)
            ? new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { message = "Solicitud de checkpoint invalida o incompleta." })
            : fallback(context);
});

var app = builder.Build();

// Recovery precedes migrations, seeding, static-file providers and all request handling.
using (var recoveryScope = app.Services.CreateScope())
{
    await recoveryScope.ServiceProvider.GetRequiredService<CheckpointRecovery>().InitializeAsync();
}
app.UseMiddleware<CheckpointGateMiddleware>();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
    app.UseSwagger();
    app.UseSwaggerUI();
//}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

var uploadsRoot = Path.Combine(
    app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
    "uploads");
Directory.CreateDirectory(uploadsRoot);

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRoot),
    RequestPath = "/uploads"
});

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();



using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();

    await db.Database.MigrateAsync();
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"CursadasMaterias\" ADD COLUMN IF NOT EXISTS \"MateriaNombre\" text NOT NULL DEFAULT '';");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Calificaciones\" ADD COLUMN IF NOT EXISTS \"MateriaNombre\" text NOT NULL DEFAULT '';");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Inscripciones\" ADD COLUMN IF NOT EXISTS \"CursoNombre\" text NOT NULL DEFAULT '';");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Inscripciones\" ADD COLUMN IF NOT EXISTS \"Division\" text NOT NULL DEFAULT '';");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Auths\" ADD COLUMN IF NOT EXISTS \"MustChangePassword\" boolean NOT NULL DEFAULT false;");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Auths\" ADD COLUMN IF NOT EXISTS \"InitialAccessSentAt\" timestamp with time zone NULL;");
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""LoginActivities"" (
            ""Id"" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            ""Email"" text NOT NULL,
            ""UserId"" uuid NULL,
            ""Success"" boolean NOT NULL,
            ""Reason"" text NULL,
            ""Platform"" text NULL,
            ""DeviceInfo"" text NULL,
            ""AppVersion"" text NULL,
            ""IpAddress"" text NULL,
            CONSTRAINT ""FK_LoginActivities_Users_UserId"" FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""Id"") ON DELETE SET NULL
        );
    ");
    await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_LoginActivities_CreatedAt\" ON \"LoginActivities\" (\"CreatedAt\");");
    await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_LoginActivities_Email\" ON \"LoginActivities\" (\"Email\");");
    await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_LoginActivities_UserId\" ON \"LoginActivities\" (\"UserId\");");
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""PasswordResetCodes"" (
            ""Id"" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            ""AuthId"" uuid NOT NULL,
            ""CodeHash"" text NOT NULL,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            ""ExpiresAt"" timestamp with time zone NOT NULL,
            ""UsedAt"" timestamp with time zone NULL,
            ""Attempts"" integer NOT NULL,
            CONSTRAINT ""FK_PasswordResetCodes_Auths_AuthId"" FOREIGN KEY (""AuthId"") REFERENCES ""Auths"" (""Id"") ON DELETE CASCADE
        );
    ");
    await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_PasswordResetCodes_AuthId\" ON \"PasswordResetCodes\" (\"AuthId\");");
    await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_PasswordResetCodes_ExpiresAt\" ON \"PasswordResetCodes\" (\"ExpiresAt\");");
    // A full restore must not silently acquire seeded roles/responsibilities on its next restart.
    if (!await scope.ServiceProvider.GetRequiredService<CheckpointRecovery>().HasRestoredAsync())
        await DbSeeder.Seed(db, hasher);
}

app.MapControllers();

app.Run();
