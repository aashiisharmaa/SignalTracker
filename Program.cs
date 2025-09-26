// FILE: SignalTracker/Program.cs

using Microsoft.EntityFrameworkCore;
using SignalTracker.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

// --- 1. Add Services to the Container ---

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

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("https://deluxe-lily-ad7565.netlify.app","http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Correctly register the DbContext
var serverVersion = new MySqlServerVersion(new Version(8, 0, 29));
var connectionString = builder.Configuration.GetConnectionString("MySqlConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, serverVersion)
);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",                   // for local development
                "https://your-frontend-url.netlify.app"   // for deployed frontend
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});


builder.Services.AddAuthorization();

// ✨ KEY FIX: Register your custom services here.
// Controllers do NOT need to be registered manually.
builder.Services.AddScoped<CommonFunction>();


// --- 2. Configure the HTTP Request Pipeline ---
var app = builder.Build(); // Build the app AFTER all services are registered.

// Configure the rest of the pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseCors("AllowReactApp");
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();