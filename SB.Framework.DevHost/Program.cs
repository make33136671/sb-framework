using SB.Framework.WebApi.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddSbFramework();
var app = builder.Build();
app.UseSbFramework();
await app.RunAsync();
