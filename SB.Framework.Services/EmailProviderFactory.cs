using Microsoft.Extensions.Configuration;
using SB.Framework.Core.Attribute;

namespace SB.Framework.Services
{
    public enum EmailProviderType
    {
        GoogleSmtp = 0,
        Resend = 1
    }

    public interface IEmailProviderFactory
    {
        IEmailProvider GetProvider(EmailProviderType providerType);
        IEmailProvider GetCurrentProvider();
        EmailProviderType GetCurrentProviderType();
    }

    [AutoDependencyInjection]
    public class EmailProviderFactory : IEmailProviderFactory
    {
        private readonly GoogleSmtpEmailProvider _googleSmtpProvider;
        private readonly ResendEmailProvider _resendProvider;
        private readonly IConfiguration _configuration;

        public EmailProviderFactory(
            GoogleSmtpEmailProvider googleSmtpProvider,
            ResendEmailProvider resendProvider,
            IConfiguration configuration)
        {
            _googleSmtpProvider = googleSmtpProvider;
            _resendProvider = resendProvider;
            _configuration = configuration;
        }

        public IEmailProvider GetProvider(EmailProviderType providerType)
        {
            return providerType switch
            {
                EmailProviderType.GoogleSmtp => _googleSmtpProvider,
                EmailProviderType.Resend => _resendProvider,
                _ => _googleSmtpProvider
            };
        }

        public IEmailProvider GetCurrentProvider()
        {
            var providerType = GetCurrentProviderType();
            return GetProvider(providerType);
        }

        public EmailProviderType GetCurrentProviderType()
        {
            var providerSetting = _configuration["Email:Provider"];
            if (string.IsNullOrEmpty(providerSetting))
                return EmailProviderType.GoogleSmtp;

            return providerSetting.ToLower() switch
            {
                "resend" => EmailProviderType.Resend,
                "google" or "googlesmtp" or "smtp" => EmailProviderType.GoogleSmtp,
                _ => EmailProviderType.GoogleSmtp
            };
        }
    }
}
