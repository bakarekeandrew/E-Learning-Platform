using Microsoft.Data.SqlClient;
using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Data.Common;
using System.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dapper;

namespace E_Learning_Platform.Services
{
    public class OtpService : IOtpService
    {
        private readonly string _connectionString;
        private readonly ILogger<OtpService> _logger;
        private static bool _isInitialized = false;
        private const int OTP_LENGTH = 6;
        private const int OTP_EXPIRY_MINUTES = 10;

        public OtpService(IConfiguration configuration, ILogger<OtpService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (!_isInitialized)
            {
                _logger.LogInformation("OTP Service initialized");
                _isInitialized = true;
            }
        }

        public string GenerateOtp()
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            var otp = BitConverter.ToUInt32(bytes, 0) % (int)Math.Pow(10, OTP_LENGTH);
            return otp.ToString().PadLeft(OTP_LENGTH, '0');
        }

        public void SaveOtp(int userId, string otp)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Execute(
                    @"INSERT INTO USER_OTP (USER_ID, OTP_CODE, EXPIRY_TIME)
                      VALUES (@UserId, @Otp, DATEADD(MINUTE, @ExpiryMinutes, GETDATE()))",
                    new { UserId = userId, Otp = otp, ExpiryMinutes = OTP_EXPIRY_MINUTES });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save OTP for user {UserId}", userId);
                throw;
            }
        }

        public async Task<bool> ValidateOtpAsync(int userId, string otp)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                
                var isValid = await connection.QueryFirstOrDefaultAsync<bool>(
                    @"SELECT 1 FROM USER_OTP 
                      WHERE USER_ID = @UserId 
                      AND OTP_CODE = @Otp 
                      AND EXPIRY_TIME > GETDATE() 
                      AND IS_USED = 0",
                    new { UserId = userId, Otp = otp });

                if (isValid)
                {
                    // Mark OTP as used
                    await connection.ExecuteAsync(
                        "UPDATE USER_OTP SET IS_USED = 1 WHERE USER_ID = @UserId AND OTP_CODE = @Otp",
                        new { UserId = userId, Otp = otp });
                }

                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate OTP for user {UserId}", userId);
                return false;
            }
        }

        // Mark OTP as used
        public void MarkOtpAsUsed(int userId, string otpCode)
        {
            try
            {
                _logger.LogInformation("Marking OTP as used for user {UserId}", userId);

                using var connection = new SqlConnection(_connectionString);
                connection.Execute(
                    @"UPDATE USER_OTPS 
                      SET IS_VALID = 0 
                      WHERE USER_ID = @UserId 
                      AND OTP_CODE = @OtpCode",
                    new { UserId = userId, OtpCode = otpCode });

                _logger.LogInformation("Successfully marked OTP as used for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking OTP as used for user {UserId}", userId);
                throw;
            }
        }

        // Migration method to consolidate data from both tables
        public async Task<bool> ValidateTempUserOtpAsync(int tempUserId, string otpCode)
        {
            try
            {
                _logger.LogInformation("Validating temp user OTP for user {TempUserId}", tempUserId);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var result = await connection.QueryFirstOrDefaultAsync<(string OtpCode, DateTime Expiry, string Email)>(
                    @"SELECT OTP_CODE, OTP_EXPIRY, EMAIL
                      FROM TEMP_USERS 
                      WHERE TEMP_USER_ID = @TempUserId",
                    new { TempUserId = tempUserId });

                if (result == default)
                {
                    _logger.LogWarning("No OTP record found for temp user {TempUserId}", tempUserId);
                    return false;
                }

                if (result.OtpCode != otpCode)
                {
                    _logger.LogWarning("Invalid OTP provided for temp user {TempUserId}", tempUserId);
                    return false;
                }

                if (result.Expiry <= DateTime.UtcNow)
                {
                    _logger.LogWarning("OTP has expired for temp user {TempUserId}", tempUserId);
                    return false;
                }

                _logger.LogInformation("OTP validation successful for temp user {TempUserId}", tempUserId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating temp user OTP for user {TempUserId}", tempUserId);
                return false;
            }
        }
        public void MigrateOtpData()
        {
            try
            {
                _logger.LogInformation("Starting OTP data migration");

                using var connection = new SqlConnection(_connectionString);
                connection.Open();
                using var transaction = connection.BeginTransaction();

                try
                {
                    // Migrate data from USER_MFA to USER_OTPS
                    using var command = new SqlCommand(
                        @"INSERT INTO USER_OTPS (USER_ID, OTP_CODE, EXPIRATION_TIME, IS_VALID, CREATED_AT)
                          SELECT USER_ID, OTP_CODE, EXPIRY_TIME, 
                                 CASE WHEN IS_USED = 0 THEN 1 ELSE 0 END,
                                 CREATED_AT
                          FROM USER_MFA
                          WHERE NOT EXISTS (
                              SELECT 1 FROM USER_OTPS 
                              WHERE USER_OTPS.USER_ID = USER_MFA.USER_ID
                              AND USER_OTPS.OTP_CODE = USER_MFA.OTP_CODE
                          )",
                        connection, transaction);

                    int rowsAffected = command.ExecuteNonQuery();
                    _logger.LogInformation("Migrated {rowsAffected} OTP records from USER_MFA to USER_OTPS", rowsAffected);

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during OTP data migration");
                throw new Exception($"Error during OTP data migration: {ex.Message}");
            }
        }
    }
}