using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using PostProcessingServer.Filters;
using PostProcessingServer.Models;

namespace PostProcessingServer.Controllers
{
    [ChatAuditAuthorize]
    public class RoleManagementController : Controller
    {
        private readonly PostProcessorApplicationDbContext _db = new PostProcessorApplicationDbContext();
        private RoleManager<IdentityRole> _roleManager;

        private RoleManager<IdentityRole> RoleMgr
        {
            get
            {
                return _roleManager
                    ?? (_roleManager = new RoleManager<IdentityRole>(new RoleStore<IdentityRole>(_db)));
            }
        }

        private string CurrentUser => ChatAuditAuthorizeAttribute.GetCurrentUsername(HttpContext);

        // GET: /RoleManagement/
        public async Task<ActionResult> Index()
        {
            ViewBag.CurrentUser = CurrentUser;

            var roles = await _db.Roles
                .OrderBy(r => r.Name)
                .Select(r => new RoleRow
                {
                    Id = r.Id,
                    Name = r.Name,
                    MemberCount = r.Users.Count,
                })
                .ToListAsync();

            return View(new RoleListViewModel { Roles = roles });
        }

        // POST: /RoleManagement/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(string roleName)
        {
            if (string.IsNullOrWhiteSpace(roleName))
            {
                TempData["Error"] = "Role name is required.";
                return RedirectToAction("Index");
            }

            var trimmed = roleName.Trim();
            if (await RoleMgr.RoleExistsAsync(trimmed))
            {
                TempData["Error"] = $"Role '{trimmed}' already exists.";
                return RedirectToAction("Index");
            }

            var result = await RoleMgr.CreateAsync(new IdentityRole(trimmed));
            if (!result.Succeeded)
                TempData["Error"] = string.Join(", ", result.Errors);

            return RedirectToAction("Index");
        }

        // POST: /RoleManagement/Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Delete(string roleId)
        {
            var role = await RoleMgr.FindByIdAsync(roleId);
            if (role != null)
            {
                var result = await RoleMgr.DeleteAsync(role);
                if (!result.Succeeded)
                    TempData["Error"] = string.Join(", ", result.Errors);
            }
            return RedirectToAction("Index");
        }

        // GET: /RoleManagement/Members?roleId=...&search=...
        public async Task<ActionResult> Members(string roleId, string search = null)
        {
            ViewBag.CurrentUser = CurrentUser;

            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
            if (role == null) return HttpNotFound();

            // Get member IDs via junction table (no lazy load dependency)
            var memberIds = await _db.Roles
                .Where(r => r.Id == roleId)
                .SelectMany(r => r.Users)
                .Select(ur => ur.UserId)
                .ToListAsync();

            var members = await _db.Users
                .Where(u => memberIds.Contains(u.Id))
                .OrderBy(u => u.UserName)
                .Select(u => new UserRow { Id = u.Id, UserName = u.UserName, Email = u.Email })
                .ToListAsync();

            var vm = new RoleMembersViewModel
            {
                RoleId = roleId,
                RoleName = role.Name,
                Members = members,
                SearchQuery = search,
            };

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim();
                vm.SearchResults = await _db.Users
                    .Where(u => !memberIds.Contains(u.Id) &&
                                (u.UserName.Contains(q) || u.Email.Contains(q)))
                    .OrderBy(u => u.UserName)
                    .Take(20)
                    .Select(u => new UserRow { Id = u.Id, UserName = u.UserName, Email = u.Email })
                    .ToListAsync();
            }

            return View(vm);
        }

        // POST: /RoleManagement/AddUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> AddUser(string roleId, string userId)
        {
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
            if (role == null) return HttpNotFound();

            var userExists = await _db.Users.AnyAsync(u => u.Id == userId);
            if (!userExists) return HttpNotFound();

            var alreadyInRole = await _db.Set<IdentityUserRole>()
                .AnyAsync(ur => ur.RoleId == roleId && ur.UserId == userId);
            if (!alreadyInRole)
            {
                _db.Set<IdentityUserRole>().Add(new IdentityUserRole { RoleId = roleId, UserId = userId });
                await _db.SaveChangesAsync();
            }

            return RedirectToAction("Members", new { roleId });
        }

        // POST: /RoleManagement/RemoveUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> RemoveUser(string roleId, string userId)
        {
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
            if (role == null) return HttpNotFound();

            var userRole = await _db.Set<IdentityUserRole>()
                .FirstOrDefaultAsync(ur => ur.RoleId == roleId && ur.UserId == userId);
            if (userRole != null)
            {
                _db.Set<IdentityUserRole>().Remove(userRole);
                await _db.SaveChangesAsync();
            }

            return RedirectToAction("Members", new { roleId });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _roleManager?.Dispose();
                _db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
