using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace E_Learning_Platform.Models
{
    public class UserInfo
    {
        public int USER_ID { get; set; }

        [Required(ErrorMessage = "Username is required")]
        public string USERNAME { get; set; }

        [Required(ErrorMessage = "Password is required")]
        public string PASSWORD { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string EMAIL { get; set; }

        [Required(ErrorMessage = "Full name is required")]
        public string FULL_NAME { get; set; }

        public DateTime CREATED_DATE { get; set; }
        public bool IS_ACTIVE { get; set; }
        public int ROLE_ID { get; set; }
        public string ROLE_NAME { get; set; }

        // Permission-related properties
        public List<UserPermissionInfo> Permissions { get; set; } = new();
        public bool HasManagePermissions { get; set; }
        public bool HasViewPermissions { get; set; }
    }

    public class UserPermissionInfo
    {
        public int PERMISSION_ID { get; set; }
        public string PERMISSION_NAME { get; set; }
        public string DESCRIPTION { get; set; }
        public string CATEGORY_NAME { get; set; }
        public DateTime ASSIGNED_DATE { get; set; }
        public string ASSIGNED_BY { get; set; }
        public bool IS_GRANT { get; set; }
        public DateTime? EXPIRATION_DATE { get; set; }
    }
} 