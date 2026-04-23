using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace DigitalScrumBoard1.Services
{
    public sealed class EmailOptions
    {
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; }
        public string SmtpUser { get; set; } = "";
        public string SmtpPass { get; set; } = "";
        public string FromEmail { get; set; } = "";
        public string FromName { get; set; } = "";
        public bool UseSsl { get; set; } = true;
        public string AppBaseUrl { get; set; } = "";
        public string FrontendBaseUrl { get; set; } = "";
    }

    public sealed class SmtpEmailSender : IEmailSender
    {
        private readonly EmailOptions _opt;

        public SmtpEmailSender(IOptions<EmailOptions> opt)
        {
            _opt = opt.Value;
        }

        public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct)
        {
            using var msg = new MailMessage
            {
                From = new MailAddress(_opt.FromEmail, _opt.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            msg.To.Add(new MailAddress(toEmail));

            using var client = new SmtpClient(_opt.SmtpHost, _opt.SmtpPort)
            {
                EnableSsl = _opt.UseSsl,
                Credentials = new NetworkCredential(_opt.SmtpUser, _opt.SmtpPass)
            };

            // SmtpClient doesn't support CancellationToken directly
            ct.ThrowIfCancellationRequested();
            await client.SendMailAsync(msg);
        }
    }
}