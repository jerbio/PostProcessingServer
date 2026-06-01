using System.Collections.Generic;

namespace PostProcessingServer.Models
{
    public class RoleListViewModel
    {
        public List<RoleRow> Roles { get; set; } = new List<RoleRow>();
    }

    public class RoleRow
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int MemberCount { get; set; }
    }

    public class RoleMembersViewModel
    {
        public string RoleId { get; set; }
        public string RoleName { get; set; }
        public List<UserRow> Members { get; set; } = new List<UserRow>();
        public List<UserRow> SearchResults { get; set; } = new List<UserRow>();
        public string SearchQuery { get; set; }
    }

    public class UserRow
    {
        public string Id { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
    }
}
