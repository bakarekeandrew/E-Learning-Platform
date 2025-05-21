using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace E_Learning_Platform.Pages.Services
{
    public class EmailService
    {
        // Replace with actual Gmail SMTP settings
        private string SmtpServer => "smtp.gmail.com";
        private int SmtpPort => 587;
        private string SmtpUsername => "bakarekeandrew@gmail.com";
        // Use app password for increased security rather than actual account password
        private string SmtpPassword => "nclc evob tgdr wvuf"; // Use an app password for Gmail
        private string FromEmail => "bakarekeandrew@gmail.com";
        private string FromName => "E-Learning Platform";

        private readonly LoggingService _logger;

        public EmailService()
        {
            _logger = new LoggingService();
        }

        public bool SendOtpEmail(string recipientEmail, string otpCode)
        {
            try
            {
                _logger.LogInfo("EmailService", $"Attempting to send OTP email to {recipientEmail}");

                // Create mail message
                var mail = new MailMessage
                {
                    From = new MailAddress(FromEmail, FromName),
                    Subject = "Your E-Learning Platform Verification Code",
                    IsBodyHtml = true,
                    Body = GetOtpEmailBody(otpCode)
                };

                mail.To.Add(recipientEmail);

                // Configure SMTP client
                var smtpClient = new SmtpClient(SmtpServer)
                {
                    Port = SmtpPort,
                    Credentials = new NetworkCredential(SmtpUsername, SmtpPassword),
                    EnableSsl = true
                };

                // Send email
                smtpClient.Send(mail);

                _logger.LogInfo("EmailService", $"Successfully sent OTP email to {recipientEmail}");
                return true;
            }
            catch (Exception ex)
            {
                // Log exception with detailed information
                _logger.LogError("EmailService", $"Failed to send email to {recipientEmail}", ex);
                // Don't throw the exception - return false instead
                return false;
            }
        }

        // Improved async method that returns success/failure status
        public async Task<bool> SendOtpEmailAsync(string recipientEmail, string otpCode)
        {
            try
            {
                _logger.LogInfo("EmailService", $"Attempting to send async OTP email to {recipientEmail}");

                // Create mail message
                var mail = new MailMessage
                {
                    From = new MailAddress(FromEmail, FromName),
                    Subject = "Your E-Learning Platform Verification Code",
                    IsBodyHtml = true,
                    Body = GetOtpEmailBody(otpCode)
                };

                mail.To.Add(recipientEmail);

                // Configure SMTP client
                var smtpClient = new SmtpClient(SmtpServer)
                {
                    Port = SmtpPort,
                    Credentials = new NetworkCredential(SmtpUsername, SmtpPassword),
                    EnableSsl = true
                };

                // Send email asynchronously
                await smtpClient.SendMailAsync(mail);

                _logger.LogInfo("EmailService", $"Successfully sent async OTP email to {recipientEmail}");
                return true;
            }
            catch (Exception ex)
            {
                // Log exception with detailed information
                _logger.LogError("EmailService", $"Failed to send async email to {recipientEmail}", ex);
                return false;
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
    }
}