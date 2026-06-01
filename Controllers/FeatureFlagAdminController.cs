using System;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using Newtonsoft.Json;
using PostProcessingServer.Filters;
using PostProcessingServer.Models;
using TilerCrossServerResources;

namespace PostProcessingServer.Controllers
{
    [ChatAuditAuthorize]
    public class FeatureFlagAdminController : Controller
    {
        private PostProcessorApplicationDbContext db = new PostProcessorApplicationDbContext();

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }

        private string CurrentUser => ChatAuditAuthorizeAttribute.GetCurrentUsername(HttpContext);

        // ─── Feature Flags ────────────────────────────────────────────────────

        public async Task<ActionResult> Index()
        {
            ViewBag.CurrentUser = CurrentUser;
            var flags = await db.FeatureFlags
                .OrderBy(f => f.Name)
                .Select(f => new FlagRow
                {
                    Id = f.Id,
                    Name = f.Name,
                    Description = f.Description,
                    Owner = f.Owner,
                    IsEnabledGlobal = f.IsEnabledGlobal,
                    RolloutPercent = f.RolloutPercent,
                    UserToggleable = f.UserToggleable,
                    ExpiresAtUtc = f.ExpiresAtUtc,
                    UpdatedUtc = f.UpdatedUtc,
                })
                .ToListAsync();

            return View(new FlagListViewModel { Flags = flags });
        }

        public ActionResult Create()
        {
            ViewBag.CurrentUser = CurrentUser;
            return View("CreateEdit", new FlagEditViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(FlagEditViewModel model)
        {
            ViewBag.CurrentUser = CurrentUser;
            if (!ModelState.IsValid) return View("CreateEdit", model);

            //if (await db.FeatureFlags.AnyAsync(f => f.Id == model.FlagId))
            //{
            //    ModelState.AddModelError("FlagId", "A flag with this ID already exists.");
            //    return View("CreateEdit", model);
            //}

            var now = DateTime.UtcNow;
            var flag = new FeatureFlag
            {
                Id = Ulid.NewUlid().ToString(),
                Name = model.Name.Trim(),
                Description = model.Description,
                Owner = model.Owner,
                IsEnabledGlobal = model.IsEnabledGlobal,
                RolloutPercent = model.RolloutPercent,
                UserToggleable = model.UserToggleable,
                ExpiresAtUtc = model.ExpiresAtUtc,
                CreatedUtc = now,
                UpdatedUtc = now,
                UpdatedByUserId = CurrentUser,
            };
            db.FeatureFlags.Add(flag);

            db.FeatureFlagAuditLogs.Add(new FeatureFlagAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                FlagName = flag.Id,
                Action = "Create",
                NewValueJson = JsonConvert.SerializeObject(new
                {
                    flag.Name, flag.Description, flag.Owner, flag.IsEnabledGlobal,
                    flag.RolloutPercent, flag.UserToggleable, flag.ExpiresAtUtc
                }),
                ActorUserId = CurrentUser,
                ActorSource = (int)AuditActorSource.PPS,
                Utc = now,
            });

            await db.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Flag '{flag.Id}' created.";
            return RedirectToAction("Index");
        }

        public async Task<ActionResult> Edit(string id)
        {
            ViewBag.CurrentUser = CurrentUser;
            if (string.IsNullOrWhiteSpace(id)) return RedirectToAction("Index");

            var flag = await db.FeatureFlags.FindAsync(id);
            if (flag == null)
            {
                TempData["ErrorMessage"] = "Flag not found.";
                return RedirectToAction("Index");
            }

            return View("CreateEdit", new FlagEditViewModel
            {
                Name = flag.Name,
                Description = flag.Description,
                Owner = flag.Owner,
                IsEnabledGlobal = flag.IsEnabledGlobal,
                RolloutPercent = flag.RolloutPercent,
                UserToggleable = flag.UserToggleable,
                ExpiresAtUtc = flag.ExpiresAtUtc,
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(string id, FlagEditViewModel model)
        {
            ViewBag.CurrentUser = CurrentUser;
            if (!ModelState.IsValid) return View("CreateEdit", model);

            var flag = await db.FeatureFlags.FindAsync(id);
            if (flag == null)
            {
                TempData["ErrorMessage"] = "Flag not found.";
                return RedirectToAction("Index");
            }

            var oldJson = JsonConvert.SerializeObject(new
            {
                flag.Name, flag.Description, flag.Owner, flag.IsEnabledGlobal,
                flag.RolloutPercent, flag.UserToggleable, flag.ExpiresAtUtc
            });

            flag.Name = model.Name.Trim();
            flag.Description = model.Description;
            flag.Owner = model.Owner;
            flag.IsEnabledGlobal = model.IsEnabledGlobal;
            flag.RolloutPercent = model.RolloutPercent;
            flag.UserToggleable = model.UserToggleable;
            flag.ExpiresAtUtc = model.ExpiresAtUtc;
            flag.UpdatedUtc = DateTime.UtcNow;
            flag.UpdatedByUserId = CurrentUser;

            db.FeatureFlagAuditLogs.Add(new FeatureFlagAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                FlagName = flag.Id,
                Action = "Update",
                OldValueJson = oldJson,
                NewValueJson = JsonConvert.SerializeObject(new
                {
                    flag.Name, flag.Description, flag.Owner, flag.IsEnabledGlobal,
                    flag.RolloutPercent, flag.UserToggleable, flag.ExpiresAtUtc
                }),
                ActorUserId = CurrentUser,
                ActorSource = (int)AuditActorSource.PPS,
                Utc = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Flag '{flag.Id}' updated.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Toggle(string id)
        {
            var flag = await db.FeatureFlags.FindAsync(id);
            if (flag == null)
            {
                TempData["ErrorMessage"] = "Flag not found.";
                return RedirectToAction("Index");
            }

            flag.IsEnabledGlobal = !flag.IsEnabledGlobal;
            flag.UpdatedUtc = DateTime.UtcNow;
            flag.UpdatedByUserId = CurrentUser;

            db.FeatureFlagAuditLogs.Add(new FeatureFlagAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                FlagName = flag.Id,
                Action = flag.IsEnabledGlobal ? "Enable" : "Disable",
                ActorUserId = CurrentUser,
                ActorSource = (int)AuditActorSource.PPS,
                Utc = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Delete(string id)
        {
            var flag = await db.FeatureFlags.FindAsync(id);
            if (flag == null)
            {
                TempData["ErrorMessage"] = "Flag not found.";
                return RedirectToAction("Index");
            }

            db.FeatureFlagAuditLogs.Add(new FeatureFlagAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                FlagName = id,
                Action = "Delete",
                OldValueJson = JsonConvert.SerializeObject(new
                {
                    flag.Name, flag.Description, flag.Owner, flag.IsEnabledGlobal,
                    flag.RolloutPercent, flag.UserToggleable, flag.ExpiresAtUtc
                }),
                ActorUserId = CurrentUser,
                ActorSource = (int)AuditActorSource.PPS,
                Utc = DateTime.UtcNow,
            });

            // Overrides cascade via FK; remove explicitly to be safe before flag delete
            var overrides = db.FeatureFlagOverrides.Where(o => o.FlagId == id);
            db.FeatureFlagOverrides.RemoveRange(overrides);
            db.FeatureFlags.Remove(flag);

            await db.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Flag '{id}' deleted.";
            return RedirectToAction("Index");
        }

        // ─── Permissions ──────────────────────────────────────────────────────

        public async Task<ActionResult> Permissions()
        {
            ViewBag.CurrentUser = CurrentUser;

            var permissions = await db.Permissions
                .Include(p => p.RolePermissions)
                .OrderBy(p => p.Key)
                .ToListAsync();

            var roles = await db.Roles.OrderBy(r => r.Name).ToListAsync();
            var roleDict = roles.ToDictionary(r => r.Id, r => r.Name);

            var permRows = permissions.Select(p => new PermissionRow
            {
                Id = p.Id,
                Key = p.Key,
                Description = p.Description,
                Assignments = p.RolePermissions.Select(rp => new RoleAssignmentRow
                {
                    RolePermissionId = rp.Id,
                    RoleId = rp.RoleId,
                    RoleName = roleDict.ContainsKey(rp.RoleId) ? roleDict[rp.RoleId] : rp.RoleId,
                }).ToList(),
            }).ToList();

            var model = new PermissionsViewModel
            {
                Permissions = permRows,
                AvailableRoles = roles.Select(r => new SelectListItem { Value = r.Id, Text = r.Name }).ToList(),
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> CreatePermission(string key, string description)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                TempData["ErrorMessage"] = "Permission key is required.";
                return RedirectToAction("Permissions");
            }

            key = key.Trim().ToLowerInvariant();

            if (await db.Permissions.AnyAsync(p => p.Key == key))
            {
                TempData["ErrorMessage"] = $"Permission '{key}' already exists.";
                return RedirectToAction("Permissions");
            }

            db.Permissions.Add(new Permission
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 32),
                Key = key,
                Description = description?.Trim(),
            });
            await db.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Permission '{key}' created.";
            return RedirectToAction("Permissions");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> AssignPermission(string permissionId, string roleId)
        {
            if (string.IsNullOrWhiteSpace(permissionId) || string.IsNullOrWhiteSpace(roleId))
            {
                TempData["ErrorMessage"] = "Both permission and role are required.";
                return RedirectToAction("Permissions");
            }

            if (await db.RolePermissions.AnyAsync(rp => rp.PermissionId == permissionId && rp.RoleId == roleId))
            {
                TempData["ErrorMessage"] = "That role already has this permission.";
                return RedirectToAction("Permissions");
            }

            db.RolePermissions.Add(new RolePermission
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 32),
                RoleId = roleId,
                PermissionId = permissionId,
            });
            await db.SaveChangesAsync();
            TempData["SuccessMessage"] = "Permission assigned to role.";
            return RedirectToAction("Permissions");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> RemoveRolePermission(string rolePermissionId)
        {
            var rp = await db.RolePermissions.FindAsync(rolePermissionId);
            if (rp != null)
            {
                db.RolePermissions.Remove(rp);
                await db.SaveChangesAsync();
                TempData["SuccessMessage"] = "Role permission removed.";
            }
            return RedirectToAction("Permissions");
        }

        // ─── Audit Log ────────────────────────────────────────────────────────

        public async Task<ActionResult> AuditLog(string flagName = null, int page = 1)
        {
            ViewBag.CurrentUser = CurrentUser;
            const int pageSize = 50;

            var query = db.FeatureFlagAuditLogs.AsQueryable();
            if (!string.IsNullOrWhiteSpace(flagName))
                query = query.Where(a => a.FlagName.Contains(flagName));

            int total = await query.CountAsync();

            var entries = await query
                .OrderByDescending(a => a.Utc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new AuditLogEntry
                {
                    Id = a.Id,
                    FlagName = a.FlagName,
                    Action = a.Action,
                    OldValueJson = a.OldValueJson,
                    NewValueJson = a.NewValueJson,
                    ActorUserId = a.ActorUserId,
                    ActorSource = a.ActorSource == 0 ? "PPS" : a.ActorSource == 1 ? "TilerFront" : "System",
                    Utc = a.Utc,
                })
                .ToListAsync();

            return View(new AuditLogViewModel
            {
                Entries = entries,
                Page = page,
                PageSize = pageSize,
                TotalCount = total,
                FilterFlagName = flagName,
            });
        }
    }
}
