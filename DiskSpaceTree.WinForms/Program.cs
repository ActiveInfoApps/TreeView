using Microsoft.EntityFrameworkCore;
using DiskSpaceTree.Data.Persistence;

namespace DiskSpaceTree.WinForms;

static class Program
{
    [STAThread]
    static async Task Main()
    {
        ApplicationConfiguration.Initialize();

        await using (var context = new ScanDbContext())
        {
            await context.Database.MigrateAsync();
        }

        Application.Run(new MainForm());
    }
}