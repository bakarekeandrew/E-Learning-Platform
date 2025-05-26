using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using E_Learning_Platform.Pages.Services;
using Microsoft.Extensions.DependencyInjection;
using E_Learning_Platform.Hubs;
using E_Learning_Platform.Services;
using E_Learning_Platform.Authorization;
using System.Security.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel with strict TLS requirements
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
        httpsOptions.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.NoCertificate;
    });
});

// Add services to the container
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(159);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.Name = "Elearning.Session";
});

builder.Services.AddSignalR(); // Add SignalR service
builder.Services.AddHostedService<DashboardUpdateService>(); // Add background service
builder.Services.AddHostedService<InfoUpdateService>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpContextAccessor();

// Add services
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddSingleton<LoggingService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<OtpService>(provider =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException("DefaultConnection string is not configured");
    }
    return new OtpService(connectionString);
});
builder.Services.AddScoped<IUserService, UserService>();

// Add authorization handlers
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

// Authentication setup with secure defaults
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/Login";
    options.AccessDeniedPath = "/AccessDenied";
    options.Cookie.Name = "E_Learning_Platform.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(1);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// Authorization setup
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // Role-based policies
    options.AddPolicy("AdminOnly", policy => 
        policy.RequireRole("ADMIN").RequireAuthenticatedUser());
    options.AddPolicy("InstructorOnly", policy => 
        policy.RequireRole("INSTRUCTOR").RequireAuthenticatedUser());
    options.AddPolicy("StudentOnly", policy => 
        policy.RequireRole("STUDENT").RequireAuthenticatedUser());

    // Permission-based policies
    options.AddPolicy("ViewCourses", policy =>
        policy.RequireClaim("Permission", "COURSE.VIEW"));
    options.AddPolicy("EditCourses", policy =>
        policy.RequireClaim("Permission", "COURSE.EDIT"));
    options.AddPolicy("ManageUsers", policy =>
        policy.RequireClaim("Permission", "USER.MANAGE"));
    options.AddPolicy("ViewReports", policy =>
        policy.RequireClaim("Permission", "REPORT.VIEW"));
    options.AddPolicy("ManagePermissions", policy =>
        policy.RequireClaim("Permission", "PERMISSION.MANAGE"));
});

// Razor Pages configuration
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", "AdminOnly");
    options.Conventions.AuthorizeFolder("/Instructor", "InstructorOnly");
    options.Conventions.AuthorizeFolder("/Student", "StudentOnly");
    options.Conventions.AuthorizePage("/UsersInfo", "ManageUsers");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapHub<DashboardHub>("/dashboardHub");
app.MapHub<InfoHub>("/infoHub");
app.MapGet("", () => Results.Redirect("/HomePage"));

app.Run(); 