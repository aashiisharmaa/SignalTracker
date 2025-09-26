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

builder.Services.AddAuthorization();

// ✨ KEY FIX: Register your custom services here
builder.Services.AddScoped<CommonFunction>();

// --- 2. Configure Kestrel to use Render port ---
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// --- 3. Build the app ---
var app = builder.Build();

// Configure the HTTP request pipeline
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
