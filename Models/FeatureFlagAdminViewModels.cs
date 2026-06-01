using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;

namespace PostProcessingServer.Models
{
    // ─── Flag list ────────────────────────────────────────────────────────────

    public class FlagListViewModel
    {
        public List<FlagRow> Flags { get; set; } = new List<FlagRow>();
    }

    public class FlagRow
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Owner { get; set; }
        public bool IsEnabledGlobal { get; set; }
        public int? RolloutPercent { get; set; }
        public bool UserToggleable { get; set; }
        public DateTimeOffset? ExpiresAtUtc { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }

    // ─── Flag create / edit ───────────────────────────────────────────────────

    public class FlagEditViewModel
    {
        /// <summary>Null on create; populated on edit (PK, cannot change).</summary>
        public string Id { get; set; }

        //[Required(ErrorMessage = "Flag ID is required.")]
        //[StringLength(100, ErrorMessage = "Flag ID must be 100 characters or fewer.")]
        //[RegularExpression(@"^[a-z0-9_\-\.]+$", ErrorMessage = "Flag ID may only contain lowercase letters, digits, hyphens, underscores and dots.")]
        //[Display(Name = "Flag ID (slug)")]
        //public string FlagId { get; set; }

        [Required(ErrorMessage = "Name is required.")]
        [StringLength(200, ErrorMessage = "Name must be 200 characters or fewer.")]
        public string Name { get; set; }

        public string Description { get; set; }

        [StringLength(200, ErrorMessage = "Owner must be 200 characters or fewer.")]
        public string Owner { get; set; }

        [Display(Name = "Globally enabled")]
        public bool IsEnabledGlobal { get; set; }

        [Range(0, 100, ErrorMessage = "Rollout percent must be between 0 and 100.")]
        [Display(Name = "Rollout % (optional)")]
        public int? RolloutPercent { get; set; }

        [Display(Name = "User-toggleable")]
        public bool UserToggleable { get; set; }

        [Display(Name = "Expires at (UTC, optional)")]
        [DataType(DataType.DateTime)]
        public DateTimeOffset? ExpiresAtUtc { get; set; }

        public bool IsNew => string.IsNullOrEmpty(Id);
    }

    // ─── Permissions management ───────────────────────────────────────────────

    public class PermissionsViewModel
    {
        public List<PermissionRow> Permissions { get; set; } = new List<PermissionRow>();
        public List<SelectListItem> AvailableRoles { get; set; } = new List<SelectListItem>();
    }

    public class PermissionRow
    {
        public string Id { get; set; }
        public string Key { get; set; }
        public string Description { get; set; }
        public List<RoleAssignmentRow> Assignments { get; set; } = new List<RoleAssignmentRow>();
    }

    public class RoleAssignmentRow
    {
        public string RolePermissionId { get; set; }
        public string RoleId { get; set; }
        public string RoleName { get; set; }
    }

    // ─── Audit log ────────────────────────────────────────────────────────────

    public class AuditLogViewModel
    {
        public List<AuditLogEntry> Entries { get; set; } = new List<AuditLogEntry>();
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public int TotalCount { get; set; }
        public string FilterFlagName { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }

    public class AuditLogEntry
    {
        public string Id { get; set; }
        public string FlagName { get; set; }
        public string Action { get; set; }
        public string OldValueJson { get; set; }
        public string NewValueJson { get; set; }
        public string ActorUserId { get; set; }
        public string ActorSource { get; set; }
        public DateTime Utc { get; set; }
    }
}
