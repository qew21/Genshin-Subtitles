using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WixSharp;
using WixSharp.Forms;

namespace WixSharp_Setup
{
    public class Program
    {
        static void Main()
        {
            var myAppExe = Path.Combine(
            Environment.CurrentDirectory, @"bin\Files\GI-Subtitles.exe");

            // 2. 读出版本号（AssemblyVersion 或 AssemblyFileVersion）
            var asmVersion = AssemblyName.GetAssemblyName(myAppExe).Version.ToString();
            var project = new ManagedProject("GI-Subtitles",
                             new Dir(@"%ProgramFiles%\GI-Subtitles",
                                 new Files(@"bin\Files\*.*")))
            {
                Version = new Version(asmVersion),   // 关键行
                ProductId = Guid.NewGuid(),          // 每次版本升级换 GUID
                UpgradeCode = new Guid("6ee5aa0c-5e19-4e14-b585-7334de8c81a2"),
            };


            //custom set of standard UI dialogs
            project.ManagedUI = new ManagedUI();

            project.ManagedUI.InstallDialogs.Add(Dialogs.Welcome)
                                            .Add(Dialogs.InstallDir)
                                            .Add(Dialogs.Progress)
                                            .Add(Dialogs.Exit);

            project.ManagedUI.ModifyDialogs.Add(Dialogs.MaintenanceType)
                                           .Add(Dialogs.Progress)
                                           .Add(Dialogs.Exit);

            project.Load += Msi_Load;
            project.BeforeInstall += Msi_BeforeInstall;
            project.AfterInstall += Msi_AfterInstall;

            project.BuildMsi();
        }

        static void Msi_Load(SetupEventArgs e)
        {
            if (!e.IsUISupressed && !e.IsUninstalling)
                MessageBox.Show(e.ToString(), "Load");
        }

        static void Msi_BeforeInstall(SetupEventArgs e)
        {
            if (!e.IsUISupressed && !e.IsUninstalling)
                MessageBox.Show(e.ToString(), "BeforeInstall");
        }

        static void Msi_AfterInstall(SetupEventArgs e)
        {
            if (!e.IsUISupressed && !e.IsUninstalling)
                MessageBox.Show(e.ToString(), "AfterExecute");
        }
    }
}