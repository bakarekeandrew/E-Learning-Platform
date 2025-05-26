using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace E_Learning_Platform.Services
{
    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;
        private readonly string _smtpServer;
        private readonly int _smtpPort;
        private readonly string _smtpUsername;
        private readonly string _smtpPassword;
        private readonly string _fromEmail;
        private readonly string _fromName;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _smtpServer = configuration["Email:SmtpServer"] ?? throw new ArgumentNullException("SmtpServer configuration missing");
            _smtpPort = int.Parse(configuration["Email:SmtpPort"] ?? "587");
            _smtpUsername = configuration["Email:SmtpUsername"] ?? throw new ArgumentNullException("SMTP Username missing");
            _smtpPassword = configuration["Email:SmtpPassword"] ?? throw new ArgumentNullException("SMTP Password missing");
            _fromEmail = configuration["Email:FromEmail"] ?? throw new ArgumentNullException("From Email missing");
            _fromName = configuration["Email:FromName"] ?? "E-Learning Platform";
        }

        public bool SendOtpEmail(string email, string otp)
        {
            try
            {
                var subject = "Your OTP Code";
                var body = GetOtpEmailBody(otp);
                return SendEmail(email, subject, body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send OTP email to {RecipientEmail}", email);
                return false;
            }
        }

        public async Task<bool> SendOtpEmailAsync(string email, string otp)
        {
            try
            {
                var subject = "Your OTP Code";
                var body = GetOtpEmailBody(otp);
                return await SendEmailAsync(email, subject, body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send OTP email to {RecipientEmail}", email);
                return false;
            }
        }

        public bool SendEmail(string to, string subject, string body)
        {
            try
            {
                using var client = new SmtpClient(_smtpServer, _smtpPort)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(_smtpUsername, _smtpPassword)
                };

                using var message = new MailMessage
                {
                    From = new MailAddress(_fromEmail, _fromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };
                message.To.Add(to);

                client.Send(message);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {RecipientEmail}", to);
                return false;
            }
        }

        public async Task<bool> SendEmailAsync(string to, string subject, string body)
        {
            try
            {
                using var client = new SmtpClient(_smtpServer, _smtpPort)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(_smtpUsername, _smtpPassword)
                };

                using var message = new MailMessage
                {
                    From = new MailAddress(_fromEmail, _fromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };
                message.To.Add(to);

                await client.SendMailAsync(message);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {RecipientEmail}", to);
                return false;
            }
        }

        public async Task SendPasswordResetEmailAsync(string recipientEmail, string resetToken)
        {
            try
            {
                var subject = "Password Reset Request";
                var body = GetPasswordResetEmailBody(resetToken);
                await SendEmailAsync(recipientEmail, subject, body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to {RecipientEmail}", recipientEmail);
                throw;
            }
        }

        private string GetOtpEmailBody(string otpCode)
        {
            return $@"
            <html>
            <head>
                <style>
                    body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                    .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                    .header {{ background-color: #4285f4; color: white; padding: 20px; text-align: center; }}
                    .content {{ padding: 20px; }}
                    .code {{ font-size: 32px; font-weight: bold; text-align: center; margin: 20px 0; color: #4285f4; letter-spacing: 5px; }}
                    .footer {{ font-size: 12px; color: #999; text-align: center; margin-top: 30px; }}
                </style>
            </head>
            <body>
                <div class='container'>
                    <div class='header'>
                        <h1>E-Learning Platform</h1>
                    </div>
                    <div class='content'>
                        <h2>Verification Code</h2>
                        <p>You have requested to enable two-factor authentication. Please use the following code to verify your email address:</p>
                        <div class='code'>{otpCode}</div>
                        <p>This code is valid for 10 minutes.</p>
                        <p>If you did not request this code, please ignore this email or contact support if you have concerns.</p>
                    </div>
                    <div class='footer'>
                        <p>This is an automated message, please do not reply.</p>
                        <p>&copy; {DateTime.Now.Year} E-Learning Platform. All rights reserved.</p>
                    </div>
                </div>
            </body>
            </html>";
        }

        private string GetPasswordResetEmailBody(string resetToken)
        {
            return $@"
            <html>
            <head>
                <style>
                    body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                    .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                    .header {{ background-color: #4285f4; color: white; padding: 20px; text-align: center; }}
                    .content {{ padding: 20px; }}
                    .button {{ display: inline-block; padding: 10px 20px; background-color: #4285f4; color: white; text-decoration: none; border-radius: 5px; }}
                    .footer {{ font-size: 12px; color: #999; text-align: center; margin-top: 30px; }}
                </style>
            </head>
            <body>
                <div class='container'>
                    <div class='header'>
                        <h1>E-Learning Platform</h1>
                    </div>
                    <div class='content'>
                        <h2>Password Reset Request</h2>
                        <p>You have requested to reset your password. Please use the following verification code to reset your password:</p>
                        <div class='code'>{resetToken}</div>
                        <p>This code is valid for 30 minutes.</p>
                        <p>If you did not request this password reset, please ignore this email or contact support if you have concerns.</p>
                    </div>
                    <div class='footer'>
                        <p>This is an automated message, please do not reply.</p>
                        <p>&copy; {DateTime.Now.Year} E-Learning Platform. All rights reserved.</p>
                    </div>
                </div>
            </body>
            </html>";
        }
    }
}