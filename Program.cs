using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.EntityFrameworkCore;
using SignalTracker.Models;
using SignalTracker.Helper;

var builder = WebApplication.CreateBuilder(args);

// ===== MVC + JSON =====
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllersWithViews()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = null);

// ===== Session + Caching =====
builder.Services.AddDistributedMemoryCache();
builder.Services.AddMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromMinutes(300);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
    o.Cookie.SameSite = SameSiteMode.None;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always; // HTTPS required
});

// ===== CORS =====
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", p => p
        .WithOrigins(
            "http://localhost:5173",
            "https://signaltracker.netlify.app" // ✅ spelling fix
        )
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

// ===== Database (FACTORY-ONLY pattern) =====
var serverVersion = new MySqlServerVersion(new Version(8, 0, 29));
var connectionString = builder.Configuration.GetConnectionString("MySqlConnection");

builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, serverVersion)
);

// Provide a scoped DbContext for places that DI ApplicationDbContext directly
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext()
);

// ===== Auth (cookies) =====
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "SignalTracker.AuthCookie";
        options.Cookie.SameSite = SameSiteMode.None;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // HTTPS required
        options.ExpireTimeSpan = TimeSpan.FromMinutes(300);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

// ===== Data Protection (optional) =====
var dpKeysPath = builder.Configuration["DataProtection:KeysPath"]
                 ?? Environment.GetEnvironmentVariable("DATAPROTECTION_KEYS_PATH");
if (!string.IsNullOrWhiteSpace(dpKeysPath))
{
    Directory.CreateDirectory(dpKeysPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath))
        .SetApplicationName("SignalTracker");
}

// ===== Other DI =====
builder.Services.AddScoped<CommonFunction>();

// ===== HTTP Logging (to see Cookie / Set-Cookie headers) =====
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields =
        HttpLoggingFields.RequestHeaders |
        HttpLoggingFields.ResponseHeaders;

    // Include Cookie / Set-Cookie explicitly
    options.RequestHeaders.Add("Cookie");
    options.ResponseHeaders.Add("Set-Cookie");
});

var app = builder.Build();

// ===== Pipeline =====
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor
});

// Log headers early
app.UseHttpLogging();

app.UseStaticFiles();
app.UseRouting();
app.UseCors("AllowReactApp");
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// ===== Debug endpoints (for cookie check) =====
// Public: see incoming cookie keys + auth state (values not exposed)
app.MapGet("/debug/cookies", (HttpContext ctx) =>
{
    var keys = ctx.Request.Cookies.Keys.ToList();
    var auth = ctx.User?.Identity?.IsAuthenticated ?? false;
    var name = ctx.User?.Identity?.Name;

    return Results.Ok(new
    {
        IsAuthenticated = auth,
        Name = name,
        CookieKeys = keys // e.g., SignalTracker.AuthCookie, .AspNetCore.Session
    });
});

// Authorized: quick whoami (200 => cookie sent & valid; 401 => not sent/invalid)
app.MapGet("/debug/whoami", (ClaimsPrincipal user) =>
{
    return Results.Ok(new
    {
        IsAuthenticated = true,
        Name = user.Identity?.Name,
        Claims = user.Claims.Select(c => new { c.Type, c.Value })
    });
}).RequireAuthorization();

// ===== MVC route =====
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

app.Run();
