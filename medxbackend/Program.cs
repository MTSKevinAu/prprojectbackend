using medxbackend.Data;
using medxbackend.Logging;
using medxbackend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using PdfSharp.Fonts;
using Serilog;
using System.Text;

GlobalFontSettings.FontResolver = new NotoSansFontResolver();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        "logs/medxbackend-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        shared: true));

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<SyncApiActionContext>();
builder.Services.AddHttpClient("Crm", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
})
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false
    });
builder.Services.AddHttpClient("Lis", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
})
    .ConfigurePrimaryHttpMessageHandler(services =>
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        var configuration = services.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue("Lis:ValidateServerCertificate", true))
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        return handler;
    });
builder.Services.AddScoped<DbHelper>();
builder.Services.AddScoped<ILisSyncApiService, LisSyncApiService>();
builder.Services.AddSingleton<LisAccessTokenCache>();
builder.Services.AddScoped<ICrmSyncApiService, CrmSyncApiService>();
builder.Services.AddScoped<ISyncRoutingRepository, SyncRoutingRepository>();
builder.Services.AddScoped<ISyncHistoryService, SyncHistoryService>();
builder.Services.AddScoped<IPatientSyncRoutingService, PatientSyncRoutingService>();
builder.Services.AddScoped<ISyncRuleService, SyncRuleService>();
builder.Services.AddScoped<SyncFailureDetails>();
builder.Services.AddScoped<IPatientDetailsExportService, PatientDetailsExportService>();
builder.Services.AddScoped<IPatientDetailsFinalUpdateService, PatientDetailsFinalUpdateService>();
builder.Services.AddScoped<ILisFinalPatientUpdateService, LisFinalPatientUpdateService>();
builder.Services.AddScoped<IPatientDetailsFinalQueryService, PatientDetailsFinalQueryService>();
builder.Services.AddScoped<ICrmSyncService, CrmSyncService>();
builder.Services.AddScoped<ICrmPreviewService, CrmPreviewService>();
builder.Services.AddSingleton<ILogService, LogService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddSingleton<IBarcodeService, BarcodeService>();
builder.Services.AddOptions<BarcodeOptions>()
    .Bind(builder.Configuration.GetSection(BarcodeOptions.SectionName))
    .Validate(options =>
        string.Equals(options.Format, "Code39", StringComparison.OrdinalIgnoreCase)
        || string.Equals(options.Format, "Code128", StringComparison.OrdinalIgnoreCase),
        "Barcode:Format must be Code39 or Code128.")
    .ValidateOnStart();
builder.Services.Configure<NetworkPrinterOptions>(
    builder.Configuration.GetSection(NetworkPrinterOptions.SectionName));
builder.Services.AddSingleton<IReceiptPrinterService, ReceiptPrinterService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Vite", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Patient Backend API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "输入 JWT Token，不需要手动添加 Bearer 前缀。"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = !string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Issuer"]),
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = !string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Audience"]),
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

var app = builder.Build();

app.UseSerilogRequestLogging();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Patient Backend API v1");
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseRouting();

app.UseCors("Vite");
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
