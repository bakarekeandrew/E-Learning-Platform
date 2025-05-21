using Microsoft.Data.SqlClient;
using System;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace E_Learning_Platform.Pages.Services
{
    public class OtpService
    {
        private readonly string _connectionString;
        private readonly LoggingService _logger;
        private static bool _isInitialized = false;

        public OtpService(string connectionString)
        {
            _connectionString = connectionString;
            _logger = new LoggingService();
            if (!_isInitialized)
            {
                _logger.LogInfo("OtpService", "Initialized with provided connection string");
                _isInitialized = true;
            }
        }

        // Generate a 6-digit OTP code
        public string GenerateOtp()
        {
            try
            {
                // Use cryptographically secure random number generator
                using (var rng = RandomNumberGenerator.Create())
                {
                    byte[] randomNumber = new byte[4];
                    rng.GetBytes(randomNumber);
                    int value = Math.Abs(BitConverter.ToInt32(randomNumber, 0));

                    // Ensure it's exactly 6 digits
                    var otp = (value % 900000 + 100000).ToString();
                    _logger.LogInfo("OtpService", $"Generated new OTP code: {otp}");
                    return otp;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("OtpService", "Error generating OTP code", ex);
                throw new Exception($"Error generating OTP code: {ex.Message}");
            }
        }

        // Save OTP code for a user - only save to one table now
        public void SaveOtp(int userId, string otpCode)
        {
            try
            {
                _logger.LogInfo("OtpService", $"Saving OTP for user ID: {userId}, Code: {otpCode}");

                using var connection = new SqlConnection(_connectionString);
                _logger.LogInfo("OtpService", "Opening database connection");
                connection.Open();
                _logger.LogInfo("OtpService", "Database connection opened successfully");

                using var transaction = connection.BeginTransaction();
                _logger.LogInfo("OtpService", "Transaction started");

                try
                {
                    // First, invalidate any existing OTPs for this user
                    using (var command = new SqlCommand(
                        "UPDATE USER_OTPS SET IS_VALID = 0 WHERE USER_ID = @UserId",
                        connection, transaction))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        int invalidatedRows = command.ExecuteNonQuery();
                        _logger.LogInfo("OtpService", $"Invalidated {invalidatedRows} existing OTPs for user ID: {userId}");
                    }

                    // Then insert the new OTP code
                    string insertSql = @"INSERT INTO USER_OTPS (USER_ID, OTP_CODE, EXPIRATION_TIME, IS_VALID, CREATED_AT) 
                                          VALUES (@UserId, @OtpCode, DATEADD(MINUTE, 10, GETDATE()), 1, GETDATE())";
                    _logger.LogInfo("OtpService", $"Executing SQL: {insertSql} with UserId={userId}, OtpCode={otpCode}");

                    using (var command = new SqlCommand(insertSql, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        command.Parameters.AddWithValue("@OtpCode", otpCode);
                        int insertedRows = command.ExecuteNonQuery();
                        _logger.LogInfo("OtpService", $"Inserted {insertedRows} new OTP records");
                    }

                    _logger.LogInfo("OtpService", "Committing transaction");
                    transaction.Commit();
                    _logger.LogInfo("OtpService", "Transaction committed successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError("OtpService", "Error in transaction, rolling back", ex);
                    transaction.Rollback();
                    _logger.LogInfo("OtpService", "Transaction rolled back");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("OtpService", $"Error saving OTP for user ID: {userId}", ex);
                throw new Exception($"Error saving OTP: {ex.Message}");
            }
        }

        // Validate OTP for a user
        public bool ValidateOtp(int userId, string otpCode)
        {
            try
            {
                _logger.LogInfo("OtpService", $"Validating OTP for user ID: {userId}, Code: {otpCode}");

                // Add connection string verification
                _logger.LogInfo("OtpService", $"Using connection string: {_connectionString}");

                using var connection = new SqlConnection(_connectionString);
                _logger.LogInfo("OtpService", "Opening database connection for validation");
                connection.Open();
                _logger.LogInfo("OtpService", "Database connection opened successfully");

                // Check if the USER_OTPS table exists and has the expected structure
                _logger.LogInfo("OtpService", "Verifying USER_OTPS table exists");
                using (var tableCheckCommand = new SqlCommand(
                    @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                      WHERE TABLE_NAME = 'USER_OTPS'", connection))
                {
                    int tableExists = (int)tableCheckCommand.ExecuteScalar();
                    _logger.LogInfo("OtpService", $"USER_OTPS table exists: {tableExists > 0}");

                    if (tableExists == 0)
                    {
                        _logger.LogError("OtpService", "USER_OTPS table does not exist!");
                        return false;
                    }
                }

                // Add a check for any OTP records for this user
                using (var userOtpCheckCommand = new SqlCommand(
                    "SELECT COUNT(*) FROM USER_OTPS WHERE USER_ID = @UserId",
                    connection))
                {
                    userOtpCheckCommand.Parameters.AddWithValue("@UserId", userId);
                    int otpCount = (int)userOtpCheckCommand.ExecuteScalar();
                    _logger.LogInfo("OtpService", $"Found {otpCount} OTP records for user ID: {userId}");
                }

                // Simple and clear validation query, limited to one table
                string validateSql = @"SELECT COUNT(1) 
                      FROM USER_OTPS 
                      WHERE USER_ID = @UserId 
                      AND OTP_CODE = @OtpCode 
                      AND IS_VALID = 1 
                      AND EXPIRATION_TIME > GETDATE()";

                _logger.LogInfo("OtpService", $"Executing validation SQL: {validateSql}");

                using var command = new SqlCommand(validateSql, connection);

                command.Parameters.AddWithValue("@UserId", userId);
                command.Parameters.AddWithValue("@OtpCode", otpCode);

                _logger.LogInfo("OtpService", "Executing validation query");
                int count = (int)command.ExecuteScalar();
                _logger.LogInfo("OtpService", $"Validation query returned: {count}");

                bool isValid = count > 0;

                if (isValid)
                {
                    _logger.LogInfo("OtpService", $"OTP validation successful for user ID: {userId}");
                }
                else
                {
                    _logger.LogInfo("OtpService", $"OTP validation failed for user ID: {userId}. Code: {otpCode}");

                    // Add more detailed diagnostic query
                    _logger.LogInfo("OtpService", "Running diagnostic query for OTP failure analysis");
                    using var diagCommand = new SqlCommand(
                        @"SELECT OTP_CODE, EXPIRATION_TIME, IS_VALID,
                          CASE WHEN EXPIRATION_TIME < GETDATE() THEN 1 ELSE 0 END AS IS_EXPIRED,
                          GETDATE() AS CURRENT_TIME,
                          DATEDIFF(MINUTE, GETDATE(), EXPIRATION_TIME) AS MINUTES_UNTIL_EXPIRY
                          FROM USER_OTPS 
                          WHERE USER_ID = @UserId 
                          ORDER BY CREATED_AT DESC",
                        connection);

                    diagCommand.Parameters.AddWithValue("@UserId", userId);

                    using var reader = diagCommand.ExecuteReader();
                    if (reader.HasRows)
                    {
                        _logger.LogInfo("OtpService", $"Diagnostic information for user ID: {userId}");
                        while (reader.Read())
                        {
                            string dbOtp = reader.GetString(0);
                            DateTime expiration = reader.GetDateTime(1);
                            bool valid = reader.GetBoolean(2);
                            bool expired = reader.GetBoolean(3);
                            DateTime currentTime = reader.GetDateTime(4);
                            int minutesUntilExpiry = reader.GetInt32(5);

                            _logger.LogInfo("OtpService",
                                $"OTP Record: Code={dbOtp}, " +
                                $"Expires={expiration}, " +
                                $"CurrentTime={currentTime}, " +
                                $"MinutesRemaining={minutesUntilExpiry}, " +
                                $"Valid={valid}, " +
                                $"Expired={expired}, " +
                                $"CodeMatch={dbOtp == otpCode}, " +
                                $"LengthMatch={dbOtp.Length == otpCode.Length}");

                            // If codes don't match but look similar, log character by character comparison
                            if (dbOtp != otpCode && dbOtp.Length == otpCode.Length)
                            {
                                for (int i = 0; i < dbOtp.Length; i++)
                                {
                                    _logger.LogInfo("OtpService",
                                        $"Character {i}: DB={dbOtp[i]} (ASCII: {(int)dbOtp[i]}), " +
                                        $"Input={otpCode[i]} (ASCII: {(int)otpCode[i]}), " +
                                        $"Match={dbOtp[i] == otpCode[i]}");
                                }
                            }
                        }
                    }
                    else
                    {
                        _logger.LogInfo("OtpService", $"No OTP records found for user ID: {userId}");
                    }
                }

                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError("OtpService", $"Error validating OTP for user ID: {userId}", ex);
                throw new Exception($"Error validating OTP: {ex.Message}");
            }
        }

        // Mark OTP as used
        public void MarkOtpAsUsed(int userId, string otpCode)
        {
            try
            {
                _logger.LogInfo("OtpService", $"Marking OTP as used for user ID: {userId}, Code: {otpCode}");

                using var connection = new SqlConnection(_connectionString);
                _logger.LogInfo("OtpService", "Opening database connection");
                connection.Open();
                _logger.LogInfo("OtpService", "Database connection opened successfully");

                using var command = new SqlCommand(
                    @"UPDATE USER_OTPS 
                      SET IS_VALID = 0 
                      WHERE USER_ID = @UserId 
                      AND OTP_CODE = @OtpCode",
                    connection);

                command.Parameters.AddWithValue("@UserId", userId);
                command.Parameters.AddWithValue("@OtpCode", otpCode);
                int rowsAffected = command.ExecuteNonQuery();

                _logger.LogInfo("OtpService", $"Marked {rowsAffected} OTP records as used for user ID: {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError("OtpService", $"Error marking OTP as used for user ID: {userId}", ex);
                throw new Exception($"Error marking OTP as used: {ex.Message}");
            }
        }

        // Migration method to consolidate data from both tables
        public async Task<bool> ValidateTempUserOtpAsync(int tempUserId, string otpCode)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var validateSql = @"SELECT COUNT(1) 
                          FROM TEMP_USERS 
                          WHERE TEMP_USER_ID = @TempUserId 
                          AND OTP_CODE = @OtpCode 
                          AND OTP_EXPIRY > GETDATE()";

                using var command = new SqlCommand(validateSql, connection);
                command.Parameters.AddWithValue("@TempUserId", tempUserId);
                command.Parameters.AddWithValue("@OtpCode", otpCode);

                return (int)await command.ExecuteScalarAsync() > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError("OtpService", $"Error validating temp user OTP", ex);
                throw;
            }
        }
        public void MigrateOtpData()
        {
            try
            {
                _logger.LogInfo("OtpService", "Starting OTP data migration");

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
                    _logger.LogInfo("OtpService", $"Migrated {rowsAffected} OTP records from USER_MFA to USER_OTPS");

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
                _logger.LogError("OtpService", "Error during OTP data migration", ex);
                throw new Exception($"Error during OTP data migration: {ex.Message}");
            }
        }
    }
}