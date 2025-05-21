using E_Learning_Platform.Data;
using Microsoft.Data.SqlClient;
using Dapper;

namespace E_Learning_Platform.Pages.Services
{
    public interface IUserService
    {
        Task<bool> IsUserActiveAsync(string userId);
    }

    public class UserService : IUserService
    {
        private readonly string _connectionString;

        public UserService()
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
        }

        public async Task<bool> IsUserActiveAsync(string userId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT IS_ACTIVE 
                    FROM USERS 
                    WHERE USER_ID = @UserId";

                var isActive = await connection.ExecuteScalarAsync<bool?>(query, new { UserId = userId });
                return isActive ?? false;
            }
            catch (Exception ex)
            {
                // Log the error if you have a logging service
                return false;
            }
        }
    }
}
