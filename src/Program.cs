using System;
using System.IO;
using System.Windows;
using MarkdownEditor.Themes;

namespace MarkdownEditor
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (s, e) =>
            {
                MessageBox.Show("Unerwarteter Fehler:\n\n" + e.Exception.Message, "Markdown Editor",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true; // weiterarbeiten statt Datenverlust
            };
            ThemeManager.Initialize(app);

            if (args.Length >= 3 && args[0] == "--selftest")
            {
                Services.AppSettings.ReadOnly = true;
                var test = new MainWindow(args[2]);
                MainWindow.PrepareSelfTest(test);
                test.RunSelfTestWhenReady(args[1]);
                return app.Run(test);
            }

            string file = args.Length > 0 && File.Exists(args[0]) ? args[0] : null;
            return app.Run(new MainWindow(file));
        }
    }
}
