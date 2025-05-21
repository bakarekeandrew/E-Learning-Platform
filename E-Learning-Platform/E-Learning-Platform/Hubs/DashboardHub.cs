using Microsoft.AspNetCore.SignalR;

namespace E_Learning_Platform.Hubs
{
    public class DashboardHub : Hub
    {
        public async Task JoinDashboardGroup(string groupName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        public async Task LeaveDashboardGroup(string groupName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }
    }
} 