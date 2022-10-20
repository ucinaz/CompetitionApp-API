using System;
namespace CompetitionApp.Models
{
    public class UserInfo
    {
        public int user_id { get; set; }
        public string username { get; set; }
        public string password { get; set; }
        public string firstName { get; set; }
        public string lastName { get; set; }
        public string email { get; set; }
        public DateTime registerDate { get; set; }
        public string activationGUID { get; set; }
        public int contestRegistered { get; set; }
        public string jwt_key { get; set; }
        public int isAdmin { get; set; }
        public int userActivated { get; set; }
    }
}
