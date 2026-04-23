using System.ComponentModel.DataAnnotations;

namespace DigitalScrumBoard1.DTOs.Authentication
{
    public sealed class UpdateUserAccessDto
    {
        // Nullable = optional (partial update)
        [Range(1, int.MaxValue)]
        public int? RoleID { get; set; }

        [Range(1, int.MaxValue)]
        public int? TeamID { get; set; }

        /// <summary>When true, clears the user's team assignment (cannot be combined with TeamID).</summary>
        public bool? RemoveFromTeam { get; set; }
    }
}