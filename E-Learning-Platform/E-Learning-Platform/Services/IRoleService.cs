using System.Threading.Tasks;
using System.Collections.Generic;

namespace E_Learning_Platform.Services
{
    public interface IRoleService
    {
        Task<int> GetDefaultRoleIdAsync();
        Task<string> GetRoleNameAsync(int roleId);
        Task<IEnumerable<(int Id, string Name)>> GetAllRolesAsync();
        Task<bool> AssignRoleAsync(int userId, int roleId);
        Task<bool> RemoveRoleAsync(int userId, int roleId);
        Task<bool> HasRoleAsync(int userId, string roleName);
        Task AssignRoleToUserAsync(int userId, int roleId, int assignedBy, string reason);
        Task<(int RoleId, string RoleName)> GetUserRoleAsync(int userId);
        Task<IEnumerable<int>> GetUsersInRoleAsync(int roleId);
    }
} 