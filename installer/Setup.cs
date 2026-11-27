using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

internal static class Setup
{
    private static int Main(string[] args)
    {
        bool whatIf = args.Length > 0 && args[args.Length - 1] == "--what-if";
        int argumentCount = args.Length - (whatIf ? 1 : 0);
        string action = argumentCount == 1 ? args[0].ToLowerInvariant() : null;
        if (argumentCount > 1 || (action != null && action != "install" && action != "modify" && action != "uninstall"))
        {
            Console.WriteLine("Usage: {0} [install|modify|uninstall] [--what-if]", Path.GetFileName(Environment.GetCommandLineArgs()[0]));
            return argumentCount == 1 && action == "--help" && !whatIf ? 0 : 2;
        }

        bool interactive = action == null;
        if (interactive)
        {
            action = ChooseAction();
            if (action == null)
                return 0;
        }

        int result = Run(action, whatIf);
        if (interactive && !Console.IsInputRedirected)
        {
            Console.WriteLine("Press Enter to close.");
            Console.ReadLine();
        }
        return result;
    }

    private static string ChooseAction()
    {
        Console.WriteLine("PaneDock Setup");
        Console.WriteLine("1. Install");
        Console.WriteLine("2. Modify (update or repair)");
        Console.WriteLine("3. Uninstall");
        Console.WriteLine("0. Exit");
        while (true)
        {
            Console.Write("Select an option: ");
            string choice = Console.ReadLine();
            switch (choice == null ? null : choice.Trim())
            {
                case "1": return "install";
                case "2": return "modify";
                case "3": return "uninstall";
                case "0":
                case null: return null;
                default: Console.WriteLine("Enter 0, 1, 2 or 3."); break;
            }
        }
    }

    private static int Run(string action, bool whatIf)
    {
        string folder = Path.Combine(Path.GetTempPath(), "PaneDock-Setup-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            Assembly assembly = Assembly.GetExecutingAssembly();
            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (name == "payload.zip" && (action == "uninstall" || whatIf))
                    continue;
                using (Stream resource = assembly.GetManifestResourceStream(name))
                using (FileStream output = File.Create(Path.Combine(folder, name)))
                    resource.CopyTo(output);
            }

            string powerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(powerShell, "-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" +
                Path.Combine(folder, "setup.ps1") + "\" -Action " + action + (whatIf ? " -WhatIf" : ""))
            {
                UseShellExecute = false,
                WorkingDirectory = folder
            };
            using (Process process = Process.Start(start))
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("PaneDock Setup failed. " + ex.Message);
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    foreach (string file in Directory.GetFiles(folder))
                        File.Delete(file);
                    Directory.Delete(folder);
                }
            }
            catch (IOException ex) { Console.Error.WriteLine(ex.Message); }
            catch (UnauthorizedAccessException ex) { Console.Error.WriteLine(ex.Message); }
        }
    }
}
