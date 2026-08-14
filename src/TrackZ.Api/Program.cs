using TrackZ.Application;
using TrackZ.Api.Endpoints;
using TrackZ.Api.Middleware;
using TrackZ.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<BusinessExceptionMiddleware>();
app.UseAuthentication();
app.MapIdentityEndpoints();

app.Run();

public partial class Program;
