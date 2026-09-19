using Mapna.Sender.Logging;
using Mapna.Sender.Staging;
using Serilog;

namespace Mapna.Sender
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            AppSettings settings;
            try { settings = AppSettings.Load(); }
            catch (Exception ex)
            {
                MessageBox.Show($"بارگذاری فایل پیکربندی با خطا مواجه شد:\n{ex.Message}", "خطای پیکربندی",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var logger = LoggingSetup.CreateLogger(settings.LogsDirectory);
            Log.Logger = logger;

            Application.ThreadException += (_, e) =>
                logger.Error(e.Exception, "Unhandled UI thread exception - application may become unstable");
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                logger.Fatal(e.ExceptionObject as Exception, "Unhandled exception - process is terminating (IsTerminating={IsTerminating})", e.IsTerminating);

            var staging = new SqlStagingRepository(settings.AppConnectionString, logger);

            try
            {
                staging.EnsureSchemaAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                logger.Fatal(ex, "Could not verify/create the staging schema in AppDatabase on startup");
                MessageBox.Show(
                    $"اتصال به پایگاه‌داده اصلی یا ایجاد جداول Staging با خطا مواجه شد:\n{ex.Message}\n\n" +
                    "لطفاً از در دسترس بودن پایگاه‌داده و صحت رشته اتصال اطمینان حاصل کنید.",
                    "خطای راه‌اندازی", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Log.CloseAndFlush();
                return;
            }

            logger.Information("Mapna.Sender starting up");

            try { Application.Run(new Form1(staging, logger)); }
            finally
            {
                logger.Information("Mapna.Sender shutting down");
                Log.CloseAndFlush();
            }
        }
    }
}