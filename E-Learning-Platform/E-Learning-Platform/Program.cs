using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using E_Learning_Platform.Services;
using Microsoft.Extensions.DependencyInjection;
using E_Learning_Platform.Hubs;
using E_Learning_Platform.Authorization;
using System.Security.Authentication;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using QuestPDF.Infrastructure;

// Configure TLS and security protocols
System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls13 | System.Net.SecurityProtocolType.Tls12;
System.Net.ServicePointManager.DefaultConnectionLimit = 50;
System.Net.ServicePointManager.CheckCertificateRevocationList = true;

// Force SQL Client to use TLS 1.2 or higher
//AppContext.SetSwitch("System.Net.Security.AllowedProtocols", 12288); // TLS 1.2 and 1.3 only

var builder = WebApplication.CreateBuilder(args);

// Verify and configure database connection
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("DefaultConnection string is not configured");
}

// Add TLS requirements to connection string if not present
if (!connectionString.Contains("Encrypt="))
{
    connectionString += ";Encrypt=True;TrustServerCertificate=True;TLS Version=1.2;";
}

// Configure database connection with retry logic
builder.Services.AddScoped<IDbConnection>(sp =>
{
    var logger = sp.GetRequiredService<ILoggingService>();
    logger.LogInfo("Database", "Attempting to create database connection");
    logger.LogInfo("Database", $"Connection string: {connectionString}");

    var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
    connection.AccessToken = null; // Force new security negotiation
    var retryCount = 0;
    const int maxRetries = 3;

    while (retryCount < maxRetries)
    {
        try
        {
            connection.Open();
            logger.LogInfo("Database", "Database connection established successfully");
            return connection;
        }
        catch (SqlException ex)
        {
            retryCount++;
            if (retryCount == maxRetries)
            {
                logger.LogError("Database", $"Failed to connect to database after {maxRetries} attempts: {ex.Message}");
                throw;
            }
            logger.LogWarning("Database", $"Database connection attempt {retryCount} failed: {ex.Message}. Retrying...");
            Task.Delay(1000 * retryCount).Wait(); // Exponential backoff
        }
    }

    throw new InvalidOperationException("Failed to establish database connection");
});

// Configure Kestrel with modern security settings
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ConfigureHttpsDefaults(httpsOptions =>
    {
        // Only allow TLS 1.2 and 1.3
        httpsOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
        httpsOptions.HandshakeTimeout = TimeSpan.FromSeconds(30);
        httpsOptions.CheckCertificateRevocation = true;
    });
});

// Add services to the container
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.Name = ".Elearning.Session";
});

// Add memory cache (required for session)
builder.Services.AddDistributedMemoryCache();

// Add HTTPS redirection configuration
builder.Services.AddHttpsRedirection(options =>
{
    options.HttpsPort = 7058;
    options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
});

// Configure HSTS with secure defaults
builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(365);
    options.ExcludedHosts.Clear(); // Ensure no hosts are excluded from HSTS
});

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 102400; // 100 KB
    options.StreamBufferCapacity = 10;
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

builder.Services.AddHostedService<DashboardUpdateService>();
builder.Services.AddHostedService<InfoUpdateService>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpContextAccessor();

// Register services
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<ILoggingService, LoggingService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<INotificationEventService, NotificationEventService>();
builder.Services.AddScoped<IAssignmentService, AssignmentService>();
builder.Services.AddScoped<ICourseProgressService, CourseProgressService>();
builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

// Database connection
builder.Services.AddTransient<IDbConnection>(sp => 
    new SqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));

// Authentication setup
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.LoginPath = "/Login";
    options.AccessDeniedPath = "/AccessDenied";
    options.Cookie.Name = ".Elearning.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
    options.Cookie.IsEssential = true;
    
    // Add event handlers for claims transformation
    options.Events = new CookieAuthenticationEvents
    {
        OnValidatePrincipal = async context =>
        {
            // Ensure we always have both session and claims data
            var userIdClaim = context.Principal?.FindFirst("UserId")?.Value;
            if (!string.IsNullOrEmpty(userIdClaim))
            {
                context.HttpContext.Session.SetInt32("UserId", int.Parse(userIdClaim));
                var roleClaim = context.Principal?.FindFirst(ClaimTypes.Role)?.Value;
                if (!string.IsNullOrEmpty(roleClaim))
                {
                    context.HttpContext.Session.SetString("UserRole", roleClaim);
                }
            }
        }
    };
});

// Authorization setup
builder.Services.AddAuthorization(options =>
{
    // User permissions (admin only)
    options.AddPolicy("Permission_USER.VIEW", policy => 
        policy.Requirements.Add(new PermissionRequirement { Permission = "USER.VIEW" }));
    options.AddPolicy("Permission_USER.MANAGE", policy => 
        policy.Requirements.Add(new PermissionRequirement { Permission = "USER.MANAGE" }));
    options.AddPolicy("Permission_USER.CREATE", policy => 
        policy.Requirements.Add(new PermissionRequirement { Permission = "USER.CREATE" }));
    options.AddPolicy("Permission_USER.EDIT", policy => 
        policy.Requirements.Add(new PermissionRequirement { Permission = "USER.EDIT" }));
    options.AddPolicy("Permission_USER.DELETE", policy => 
        policy.Requirements.Add(new PermissionRequirement { Permission = "USER.DELETE" }));
    
    // Course permissions (admin only)
    options.AddPolicy("Permission_COURSE.VIEW", policy => 
        policy.Requirements.Add(new PermissionRequirement { Permission = "COURSE.VIEW" }));
    options.AddPolicy("Permission_COURSE.MANAGE", policy => 
        policy.Requirements.Add(new PermissionRequirement { Permission = "COURSE.MANAGE" }));
    
    // Admin access
    options.AddPolicy("Permission_ADMIN.ACCESS", policy => 
        policy.Requirements.Add(new PermissionRequirement { Permission = "ADMIN.ACCESS" }));

    // Add authorization policies
    options.AddPolicy("StudentOnly", policy =>
        policy.RequireRole("STUDENT"));
    
    options.AddPolicy("InstructorOnly", policy =>
        policy.RequireRole("INSTRUCTOR"));
});

// Razor Pages configuration
builder.Services.AddRazorPages(options =>
{
    // Default authorization for all pages
    options.Conventions.AuthorizeFolder("/");
    
    // Allow anonymous access to specific pages
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/SignupPage");
    options.Conventions.AllowAnonymousToPage("/ForgotPassword");
    options.Conventions.AllowAnonymousToPage("/ResetPassword");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AllowAnonymousToPage("/AccessDenied");
    options.Conventions.AllowAnonymousToPage("/HomePage");

    // Secure admin pages with permissions
    options.Conventions.AuthorizePage("/Admin/Dashboard", "Permission_ADMIN.ACCESS");
    options.Conventions.AuthorizePage("/UsersInfo", "Permission_USER.VIEW");
    options.Conventions.AuthorizePage("/RoleManagement", "Permission_USER.MANAGE");
    options.Conventions.AuthorizePage("/PermissionsInfo", "Permission_USER.MANAGE");
    options.Conventions.AuthorizePage("/UserPermissions", "Permission_USER.MANAGE");
    options.Conventions.AuthorizePage("/PermissionAuditLog", "Permission_USER.MANAGE");
    
    // Course-related pages
    options.Conventions.AuthorizePage("/CourseInfo", "Permission_COURSE.VIEW");
    
    // Regular authorization for instructor and student pages
    options.Conventions.AuthorizeFolder("/Instructor");
    options.Conventions.AuthorizeFolder("/Student");
});

// Configure QuestPDF
QuestPDF.Settings.License = LicenseType.Community;

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddScoped<PdfReportService>();

// Build the application - after this point, service collection becomes read-only
var app = builder.Build();

// Configure the HTTP request pipeline with security headers
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    
    // Add security headers middleware
    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: https:; " +
            "font-src 'self'; " +
            "connect-src 'self';";
        await next();
    });
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Add session before authentication
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

// Map SignalR hubs
app.MapHub<InfoHub>("/infoHub");
app.MapHub<DashboardHub>("/dashboardHub");

app.MapRazorPages();
app.MapControllers();

// Add default route mapping
app.MapGet("/", context => {
    context.Response.Redirect("/HomePage");
    return Task.CompletedTask;
});

app.Run();