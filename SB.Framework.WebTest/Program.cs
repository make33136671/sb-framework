using SB.Framework.WebApi.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddSbFramework();

var app = builder.Build();

// 默认文件映射必须先于框架的 UseStaticFiles 注册(UseSbFramework 内部挂载),
// 使 / 直接打开测试台 wwwroot/test.html
app.UseDefaultFiles();

app.UseSbFramework();
await app.RunAsync();
