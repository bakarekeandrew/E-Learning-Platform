using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using Dapper;

namespace E_Learning_Platform.Services
{
    public class UserService : IUserService
    {
        private readonly string _connectionString;
        private readonly ILogger<UserService> _logger;

        public UserService(IConfiguration configuration, ILogger<UserService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> ValidateUserAsync(string email, string password)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var user = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT PASSWORD_HASH FROM USERS WHERE EMAIL = @Email",
                new { Email = email });

            return user != null && BCrypt.Net.BCrypt.Verify(password, user.PASSWORD_HASH);
        }

        public async Task<(bool success, string userId)> GetUserIdByEmailAsync(string email)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var userId = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT USER_ID FROM USERS WHERE EMAIL = @Email",
                new { Email = email });

            return (userId != null, userId ?? string.Empty);
        }
    }
} 