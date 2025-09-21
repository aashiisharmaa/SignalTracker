// FILE: SignalTracker/Program.cs

using Microsoft.EntityFrameworkCore;
using SignalTracker.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

// --- 1. Add Services to the Container ---

// This service is essential for accessing HttpContext in controllers and other services.
builder.Services.AddHttpContextAccessor();

builder.Services.AddControllersWithViews().AddJsonOptions(options =>
{
    // Keep original property names (e.g., "Status") in JSON responses for the frontend.
    options.JsonSerializerOptions.PropertyNamingPolicy = null;
});

// **REQUIRED**: Sessions need a cache to store data. Add a distributed memory cache.
builder.Services.AddDistributedMemoryCache();

// **REQUIRED**: Configure server-side session management.
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(300);
    options.Cookie.HttpOnly = true; // Makes the cookie inaccessible to client-side scripts.
    options.Cookie.IsEssential = true; // Essential for the app to function.
});

// Add CORS policy to allow requests from your React app.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:5173") // The exact URL of your React app.
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // **CRITICAL**: This must be enabled for cookies to be sent.
    });
});

var serverVersion = new MySqlServerVersion(new Version(8, 0, 29));
// Configure the database connection using the connection string from appsettings.json.
var connectionString = builder.Configuration.GetConnectionString("MySqlConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options => 
    options.UseMySql(connectionString, serverVersion)
);

// --- CONFIGURE COOKIE AUTHENTICATION ---
// This sets up the authentication scheme that will be used to sign users in and validate requests.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "SignalTracker.AuthCookie";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(300);
        options.SlidingExpiration = true; // Resets the cookie's expiration time on each request.

        // **CRITICAL FOR APIs**: This section prevents the backend from automatically redirecting
        // an unauthenticated API request to a login page. Instead, it will just return a
        // 401 Unauthorized status, which the frontend can handle gracefully.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = 401; // Set the status code to Unauthorized.
            return Task.CompletedTask;       // Complete the task to prevent the redirect.
        };
    });

builder.Services.AddAuthorization();


// --- 2. Configure the HTTP Request Pipeline ---

var app = builder.Build();

// Configure error handling for production environments.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts(); // Adds Strict-Transport-Security header.
}

// Serve static files (like images, css, or legacy JS) from the wwwroot folder.
app.UseStaticFiles();

// Enable routing to direct requests to the correct controllers and actions.
app.UseRouting();

// **CRITICAL ORDERING STEP 1**: The CORS policy must be applied *before* authentication and authorization.
app.UseCors("AllowReactApp");

// **CRITICAL ORDERING STEP 2**: The session middleware must be enabled *before* authentication.
// This ensures session data is available during the authentication process.
app.UseSession();

// **CRITICAL ORDERING STEP 3**: Enable authentication and authorization.
// This will inspect incoming requests for the auth cookie and set the User principal.
app.UseAuthentication();
app.UseAuthorization();


// Map the default controller route for your MVC controllers.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Run the application.
app.Run();