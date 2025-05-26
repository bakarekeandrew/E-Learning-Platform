using System.Threading.Tasks;

namespace E_Learning_Platform.Services
{
    public interface IUserService
    {
        Task<bool> ValidateUserAsync(string email, string password);
        Task<(bool success, string userId)> GetUserIdByEmailAsync(string email);
    }
}