using Microsoft.EntityFrameworkCore;
using SignalTracker.Models;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// --- 1. Add Services ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllersWithViews().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = null;
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(300);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// --- 1a. CORS ---
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins(
                "https://deluxe-lily-ad7565.netlify.app",
                "http://localhost:5173"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// --- 1b. DbContext ---
var serverVersion = new MySqlServerVersion(new Version(8, 0, 29));
var connectionString = builder.Configuration.GetConnectionString("MySqlConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, serverVersion)
);

builder.Services.AddAuthorization();
builder.Services.AddScoped<CommonFunction>();

// --- 2. Kestrel for Render ---
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// --- 3. Build app ---
var app = builder.Build();

// --- 4. Middleware pipeline ---
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

// ✅ CORS MUST come before Authentication & Authorization
app.UseCors("AllowReactApp");

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// --- 5. Map controllers ---
app.MapControllers(); // ensure API endpoints are mapped

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

// --- 6. Handle SPA fallback (optional if serving frontend from backend) ---
app.MapFallbackToFile("index.html");

app.Run();
