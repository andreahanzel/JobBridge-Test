using JobBridge.Components;
using JobBridge.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using JobBridge.Services;
using JobBridge.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using System.Text.Json.Serialization; 

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(builder.Configuration);

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
// builder.Services.AddSingleton<SessionAuthService>();
// builder.Services.AddSingleton<AuthenticationStateProvider>(provider => provider.GetService<SessionAuthService>()!);

builder.Services.AddHttpClient();
// Use production path if in production
var dbPath = builder.Environment.IsProduction() 
    ? "/app/data/jobbridge.db" 
    : "jobbridge.db";
builder.Services.AddSqlite<JobBridgeContext>($"Data Source={dbPath}");

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentity<User, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<JobBridgeContext>()
.AddSignInManager()
.AddDefaultTokenProviders();

// Configure application cookie settings
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/authentication";
    options.LogoutPath = "/authentication/logout";
    options.AccessDeniedPath = "/authentication";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = 401;
        }
        else
        {
            context.Response.Redirect("/authentication");
        }
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = 403;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToLogout = context =>
    {
        context.Response.StatusCode = 200;
        return Task.CompletedTask;
    };
});
// Email sender service
builder.Services.AddSingleton<IEmailSender<User>, IdentityNoOpEmailSender>();

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<JobService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ApplicationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
    app.UseStatusCodePagesWithReExecute("/Error/{0}");
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseStatusCodePagesWithReExecute("/Error/{0}");
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();

// Add server-side login endpoint
app.MapGet("/server-login", async (HttpContext context, SignInManager<User> signInManager, string email, bool remember = false) =>
{
    var user = await signInManager.UserManager.FindByEmailAsync(email);
    if (user != null)
    {
        await signInManager.SignInAsync(user, remember);
        return Results.LocalRedirect("/");
    }
    return Results.LocalRedirect("/authentication?error=Login failed");
});

using (var scope = app.Services.CreateScope())
{
    var serviceProvider = scope.ServiceProvider;
    var db = serviceProvider.GetRequiredService<JobBridgeContext>();
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        // Check if database exists and can connect
        var canConnect = await db.Database.CanConnectAsync();
        
        if (!canConnect)
        {
            logger.LogInformation("Database doesn't exist. Creating...");
            await db.Database.EnsureCreatedAsync();
        }
        else
        {
            // Try to migrate, but don't fail if tables exist
            try
            {
                await db.Database.MigrateAsync();
                logger.LogInformation("Database migration completed successfully.");
            }
            catch (Exception migEx)
            {
                logger.LogWarning(migEx, "Migration skipped - database already exists");
            }
        }
        
        // Seed data
        try
        {
            await SeedData.InitializeAsync(serviceProvider);
            logger.LogInformation("Database seeding completed successfully.");
        }
        catch (Exception seedEx)
        {
            logger.LogWarning(seedEx, "Seeding completed with some warnings.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Critical database setup error");
        // Don't rethrow - let the app start anyway
    }
}

app.Run();