using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using E_Learning_Platform.Hubs;

namespace E_Learning_Platform.Pages.Analytics
{
    public class UserEngagementModel : PageModel
    {
        private readonly string _connectionString;
        private readonly IHubContext<DashboardHub> _hubContext;

        public UserEngagementModel(IHubContext<DashboardHub> hubContext)
        {
            _hubContext = hubContext;
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";

            // Initialize non-nullable properties to default values
            TimeLabels = new List<string>();
            ActiveUsersData = new List<int>();
            SessionDurationData = new List<int>();
            ContentViewsData = new List<int>();
            PopularContent = new List<PopularContentData>();
        }

        // User Engagement Metrics
        public int ActiveUsers { get; set; }
        public int TotalSessions { get; set; }
        public double AverageSessionDuration { get; set; }
        public int TotalInteractions { get; set; }
        public List<string> TimeLabels { get; set; }
        public List<int> ActiveUsersData { get; set; }
        public List<int> SessionDurationData { get; set; }
        public List<int> ContentViewsData { get; set; }
        public List<PopularContentData> PopularContent { get; set; }

        public async Task OnGetAsync()
        {
            await LoadUserEngagementData();

            // Send real-time update to connected clients
            await _hubContext.Clients.Group("userEngagement").SendAsync("UserEngagementUpdated", new
            {
                ActiveUsers,
                TotalSessions,
                AverageSessionDuration,
                TotalInteractions,
                TimeLabels,
                ActiveUsersData,
                SessionDurationData,
                ContentViewsData,
                PopularContent
            });
        }

        private async Task LoadUserEngagementData()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Load core metrics
            ActiveUsers = await GetActiveUsersAsync(connection);
            TotalSessions = await GetTotalSessionsAsync(connection);
            AverageSessionDuration = await GetAverageSessionDurationAsync(connection);
            TotalInteractions = await GetTotalInteractionsAsync(connection);

            // Load time series data
            TimeLabels = new List<string> { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            ActiveUsersData = await GetActiveUsersDataAsync(connection);
            SessionDurationData = await GetSessionDurationDataAsync(connection);
            ContentViewsData = await GetContentViewsDataAsync(connection);

            // Load popular content
            PopularContent = await GetPopularContentAsync(connection);
        }

        #region Database Queries
        private async Task<int> GetActiveUsersAsync(SqlConnection connection)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(DISTINCT UserId) 
                  FROM UserActivities 
                  WHERE CAST(ActivityDate AS DATE) = CAST(GETDATE() AS DATE)");
        }

        private async Task<int> GetTotalSessionsAsync(SqlConnection connection)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) 
                  FROM UserSessions 
                  WHERE CAST(SessionStart AS DATE) = CAST(GETDATE() AS DATE)");
        }

        private async Task<double> GetAverageSessionDurationAsync(SqlConnection connection)
        {
            var result = await connection.ExecuteScalarAsync<decimal>(
                @"SELECT CAST(AVG(DATEDIFF(MINUTE, SessionStart, SessionEnd)) AS FLOAT)
                  FROM UserSessions
                  WHERE CAST(SessionStart AS DATE) = CAST(GETDATE() AS DATE)");
            return (double)result;
        }

        private async Task<int> GetTotalInteractionsAsync(SqlConnection connection)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) 
                  FROM UserActivities 
                  WHERE CAST(ActivityDate AS DATE) = CAST(GETDATE() AS DATE)");
        }

        private async Task<List<int>> GetActiveUsersDataAsync(SqlConnection connection)
        {
            // For now, return sample data
            return await Task.FromResult(new List<int> { 120, 150, 180, 200, 220, 190, 170 });
        }

        private async Task<List<int>> GetSessionDurationDataAsync(SqlConnection connection)
        {
            // Replace the placeholder data with an actual database query
            var result = await connection.QueryAsync<int>(
                @"SELECT DATEDIFF(MINUTE, SessionStart, SessionEnd) AS SessionDuration
          FROM UserSessions
          WHERE CAST(SessionStart AS DATE) >= DATEADD(DAY, -7, CAST(GETDATE() AS DATE))");

            return result.AsList();
        }


        private async Task<List<int>> GetContentViewsDataAsync(SqlConnection connection)
        {
            // For now, return sample data
            return new List<int> { 300, 350, 400, 450, 500, 480, 460 };
        }

        private async Task<List<PopularContentData>> GetPopularContentAsync(SqlConnection connection)
        {
            return (await connection.QueryAsync<PopularContentData>(
                @"SELECT TOP 5 
                    c.Title,
                    COUNT(ua.ActivityId) as ViewCount,
                    CAST(AVG(DATEDIFF(MINUTE, ua.ActivityStart, ua.ActivityEnd)) AS FLOAT) as AverageTimeSpent
                  FROM Courses c
                  JOIN UserActivities ua ON c.CourseId = ua.CourseId
                  WHERE ua.ActivityType = 'View'
                  GROUP BY c.Title
                  ORDER BY ViewCount DESC")).AsList();
        }
        #endregion
    }

    public class PopularContentData
    {
        public required string Title { get; set; }
        public int ViewCount { get; set; }
        public double AverageTimeSpent { get; set; }
    }
} 