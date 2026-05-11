using SB.Framework.Services;

namespace SB.Framework.WebApi.Extensions;

public static class EmailSetup
{
    public static IServiceCollection AddEmailServices(this IServiceCollection services)
    {
        services.AddScoped<IEmailRateLimitService, EmailRateLimitService>();
        services.AddScoped<IEmailProviderFactory, EmailProviderFactory>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IVerificationCodeService, VerificationCodeService>();
        return services;
    }
}
