using DigitalScrumBoard1.Models;
using DigitalScrumBoard1.Data;
using System.Data;

namespace DigitalScrumBoard1.Data.Seed;

public static class RoleSeeder
{
    public static async Task SeedRolesAsync(DigitalScrumBoardContext context)
    {
        if (context.Roles.Any())
            return;

        context.Roles.AddRange(
            new Role { RoleName = "Administrator", Description = "System administrator" },
            new Role { RoleName = "Scrum Master", Description = "Manages scrum boards" },
            new Role { RoleName = "Employee", Description = "Standard user" }
        );

        await context.SaveChangesAsync();
    }
}
