using SB.Framework.WebApi.Extensions;
using SB.Framework.WebTest.Tasks;

var builder = WebApplication.CreateBuilder(args);
builder.AddSbFramework();

// 测试项目自身的业务任务注册(衍生项目接入后台任务的标准姿势)
builder.Services.AddHostedService<WebTestTaskRegistration>();

var app = builder.Build();

// 默认文件映射必须先于框架的 UseStaticFiles 注册(UseSbFramework 内部挂载),
// 使 / 直接打开测试台 wwwroot/test.html
app.UseDefaultFiles();

app.UseSbFramework();
await app.RunAsync();
