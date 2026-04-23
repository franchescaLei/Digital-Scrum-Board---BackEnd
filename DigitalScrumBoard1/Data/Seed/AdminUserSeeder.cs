using DigitalScrumBoard1.Utilities;
using DigitalScrumBoard1.Data;
using DigitalScrumBoard1.Models;
using DigitalScrumBoard1.Security;
using Microsoft.EntityFrameworkCore;

namespace DigitalScrumBoard1.Data.Seed
{
    public static class AdminUserSeeder
    {
        public static async Task SeedAdminAsync(DigitalScrumBoardContext context)
        {
            if (await context.Users.AnyAsync())
                return;

            var adminRole = await context.Roles.FirstAsync(r => r.RoleName == "Administrator");

            // TeamID is required
            var team = await context.Teams.FirstOrDefaultAsync();
            if (team == null)
            {
                team = new Team
                {
                    TeamName = "Default Team",
                    Description = "System default team",
                    IsActive = true,
                    CreatedAt = DateTimeHelper.Now
                };

                context.Teams.Add(team);
                await context.SaveChangesAsync();
            }

            var admin = new User
            {
                FirstName = "System2",
                LastName = "Administrator",
                EmailAddress = "admin@company2.local",
                PasswordHash = PasswordHasher.Hash("Admin@123"),

                RoleID = adminRole.RoleID,
                TeamID = team.TeamID,

                Disabled = false,
                DisabledAt = null,

                CreatedAt = DateTimeHelper.Now,
                UpdatedAt = DateTimeHelper.Now,
                LastLogin = null,

                MustChangePassword = false,
                EmailVerified = true
            };

            context.Users.Add(admin);
            await context.SaveChangesAsync();
        }
    }
}