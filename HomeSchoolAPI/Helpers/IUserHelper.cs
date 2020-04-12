using System.Collections.Generic;
using System.Threading.Tasks;
using HomeSchoolAPI.APIRespond;
using HomeSchoolAPI.Models;

namespace HomeSchoolAPI.Helpers
{
    public interface IUserHelper
    {
        UserToReturn ReturnUserToReturn(User userFromRepo);
        Task<User> ReturnUserByID(string id);
        Task<User> AddFriend(string userToAddID, User user);
    }
}