// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using OpenDataHubVectorTileApi.Services;

// Only load .env file if NOT running in Docker
// Docker containers get environment variables from docker-compose.yml
var envFile = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envFile) && !IsRunningInDocker())
{
    DotNetEnv.Env.Load();
    Console.WriteLine("Loaded .env file for local development");
}
else if (IsRunningInDocker())
{
    Console.WriteLine("Running in Docker - using environment variables from container");
}

var builder = WebApplication.CreateBuilder(args);

// Add environment variables to configuration
builder.Configuration.AddEnvironmentVariables();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register the VectorTileService
builder.Services.AddScoped<IVectorTileService, VectorTileService>();

// Add CORS if needed for web clients
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");

app.UseAuthorization();

app.MapControllers();

app.Run();


static bool IsRunningInDocker()
{
    // Method 1: Check for .NET-specific environment variable (set in Dockerfile)
    if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        return true;
    
    // Method 2: Check for .dockerenv file (exists in all Docker containers)
    if (File.Exists("/.dockerenv"))
        return true;
    
    // Method 3: Check /proc/1/cgroup (Linux containers)
    try
    {
        if (File.Exists("/proc/1/cgroup"))
        {
            var content = File.ReadAllText("/proc/1/cgroup");
            if (content.Contains("/docker") || content.Contains("/kubepods"))
                return true;
        }
    }
    catch
    {
        // Ignore errors (file might not exist on Windows)
    }
    
    return false;
}