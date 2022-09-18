﻿using amethyst_installer_gui.Controls;
using amethyst_installer_gui.PInvoke;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace amethyst_installer_gui.Installer.Modules {
    public class AmethystModule : ModuleBase {
        public AmethystModule() { }

        /*
         
if upgrade yes
   delete old files in folder according to funny filelist
   extract new ame zip
   update uninstall entry version number
if upgrade no
   extract ame zip to new folder
   register uninstall entry
   verify that there are no other entries in vrpaths named amethyst, if so remove them
   register ame driver entry in steamvr
   register tracker roles in vrsettings
         
         */

        public override bool Install(string sourceFile, string path, ref InstallModuleProgress control, out TaskState state) {

            // TODO: Kill Amethyst, SteamVR
            InstallUtil.TryKillingConflictingProcesses();

            // TODO: Check for previous install of Amethyst, and if present, soft-uninstall (soft means only get rid of the install itself, don't touch configs or SteamVR)

            if ( ExtractAmethyst(sourceFile, path, ref control) ) {

                if ( !HandleInstallerPersistence(ref control) ) {
                    // If we can't install to appdata you have bigger problems to deal with
                    state = TaskState.Error;
                    return false;
                }

                bool overallSuccess = true;
                if ( !InstallerStateManager.IsUpdating ) {
                    overallSuccess = HandleDrivers(path, ref control);
                    // In some cases vrpathreg might open SteamVR, which we don't want; Kill it instantly!
                    InstallUtil.TryKillingConflictingProcesses();
                }
                overallSuccess      = overallSuccess && CreateRegistryEntry(path, ref control);
                bool sucessMinor    =                   CreateUninstallEntry(path, ref control);
                sucessMinor         = sucessMinor    && AssignTrackerRoles(ref control);
                sucessMinor         = sucessMinor    && RegisterProtocolLink(path, ref control);
                
                if ( !InstallerStateManager.IsUpdating ) {
                    sucessMinor         = sucessMinor    && AdjustSteamVrSettings(ref control);
                    // Don't recreate shortcuts during an update!
                    overallSuccess      = overallSuccess && CreateShortcuts(path, ref control);
                }

                // TODO: If this is an upgrade change the message to a different one
                Logger.Info(LogStrings.InstalledAmethystSuccess);
                control.LogInfo(LogStrings.InstalledAmethystSuccess);
                state = sucessMinor ? TaskState.Checkmark : TaskState.Warning;
                return overallSuccess;
            }

            state = TaskState.Error;
            return false;
        }

        private bool ExtractAmethyst(string archive, string target, ref InstallModuleProgress control) {
            try {

                Logger.Info(string.Format(LogStrings.ExtractingAmethyst, target));
                control.LogInfo(string.Format(LogStrings.ExtractingAmethyst, target));

                if ( InstallUtil.IsAmethystInstalledInDirectory(target) ) {
                    UninstallUtil.UninstallAmethyst(false, target);
                }

                string ameArchive = Path.GetFullPath(Path.Combine(Constants.AmethystTempDirectory, archive));
                if ( File.Exists(ameArchive) ) {

                    if ( !Directory.Exists(target) )
                        Directory.CreateDirectory(target);

                    if ( Module.Install.Format != null && Module.Install.Format == "k2a" ) {
                        K2Archive.ExtractArchive(ameArchive, target);
                    } else {
                        InstallUtil.ExtractZipToDirectory(ameArchive, target);
                    }
                    return true;
                }

            } catch ( Exception e ) {
                Logger.Fatal($"{LogStrings.ExtractAmethystFailed}:\n{Util.FormatException(e)})");
                control.LogError($"{LogStrings.ExtractAmethystFailed}! {LogStrings.ViewLogs}");
            }
            return false;
        }

        private bool HandleDrivers(string path, ref InstallModuleProgress control) {

            string driverPath = Path.Combine(path, "Amethyst");

            // TODO: Skip this step during an upgrade
            Logger.Info(LogStrings.CheckingAmethystDriverConflicts);
            control.LogInfo(LogStrings.CheckingAmethystDriverConflicts);

            // Check for existing Amethyst driver entries
            if ( Directory.Exists(OpenVRUtil.GetDriverPath("Amethyst")) ) {
                OpenVRUtil.RemoveDriversWithName("Amethyst");
            }

            // Check for K2EX driver
            if ( Directory.Exists(OpenVRUtil.GetDriverPath("KinectToVR")) ) {
                OpenVRUtil.ForceDisableDriver("KinectToVR");
                OpenVRUtil.RemoveDriversWithName("KinectToVR");
            }

            // TODO: Skip this step during an upgrade
            Logger.Info(LogStrings.RegisteringAmethystDriver);
            control.LogInfo(LogStrings.RegisteringAmethystDriver);

            OpenVRUtil.RegisterSteamVrDriver(driverPath);
            OpenVRUtil.ForceEnableDriver("Amethyst");

            InstallUtil.TryKillingConflictingProcesses();

            return true;
        }

        private bool CreateRegistryEntry(string path, ref InstallModuleProgress control) {

            try {

                Logger.Info(string.Format(LogStrings.CreatingAmethystRegistryEntry, path));
                control.LogInfo(string.Format(LogStrings.CreatingAmethystRegistryEntry, path));

                // Open or create registry entry
                var HKLM = Registry.LocalMachine.CreateSubKey(Constants.AmethystRegsitryKey, true);
                HKLM.SetValue("DisplayVersion", Module.DisplayVersion);
                HKLM.SetValue("Version", Module.InternalVersion, RegistryValueKind.DWord);
                HKLM.SetValue("Path", path);

                return true;

            } catch ( Exception e ) {
                Logger.Fatal($"{LogStrings.ExtractAmethystFailed}:\n{Util.FormatException(e)})");
                control.LogError($"{LogStrings.ExtractAmethystFailed}! {LogStrings.ViewLogs}");
            }
            return false;
        }

        private bool AssignTrackerRoles(ref InstallModuleProgress control) {

            bool success = true;
            {
                Logger.Info(LogStrings.AssigningTrackerRoles);
                control.LogInfo(LogStrings.AssigningTrackerRoles);

                try {
                    // Assign all Amethyst tracker roles
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-00WAIST0",  TrackerRole.TrackerRole_Waist);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-00WAIST00", TrackerRole.TrackerRole_Waist);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-CHEST",     TrackerRole.TrackerRole_Chest);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-L0ELBOW0",  TrackerRole.TrackerRole_LeftElbow);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-L0FOOT00",  TrackerRole.TrackerRole_LeftFoot);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-L0KNEE00",  TrackerRole.TrackerRole_LeftKnee);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-LELBOW",    TrackerRole.TrackerRole_LeftElbow);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-LFOOT",     TrackerRole.TrackerRole_LeftFoot);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-LKNEE",     TrackerRole.TrackerRole_LeftKnee);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-R0ELBOW0",  TrackerRole.TrackerRole_RightElbow);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-R0FOOT00",  TrackerRole.TrackerRole_RightFoot);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-R0KNEE00",  TrackerRole.TrackerRole_RightKnee);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-RELBOW",    TrackerRole.TrackerRole_RightElbow);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-RFOOT",     TrackerRole.TrackerRole_RightFoot);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-RKNEE",     TrackerRole.TrackerRole_RightKnee);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-WAIST",     TrackerRole.TrackerRole_Waist);
                } catch ( Exception e ) {
                    control.LogError($"{LogStrings.FailAssignTrackerRoles}! {LogStrings.ViewLogs}");
                    Logger.Fatal($"{LogStrings.FailAssignTrackerRoles}:\n{Util.FormatException(e)})");
                    success = success && false;
                }
            }

            {
                Logger.Info(LogStrings.RemovingConflictingTrackerRoles);
                control.LogInfo(LogStrings.RemovingConflictingTrackerRoles);

                try {
                    // Ancient versions of KinectToVR
                    OpenVRUtil.RemoveTrackerRole("/devices/00vrinputemulator/0");
                    OpenVRUtil.RemoveTrackerRole("/devices/00vrinputemulator/1");
                    OpenVRUtil.RemoveTrackerRole("/devices/00vrinputemulator/2");
                    OpenVRUtil.RemoveTrackerRole("/devices/00vrinputemulator/3");

                    // For most of K2EX's lifespan we tried mimicking Vive Trackers
                    OpenVRUtil.RemoveTrackerRole("/devices/htc/vive_trackerLHR-CB11ABEC");
                    OpenVRUtil.RemoveTrackerRole("/devices/htc/vive_trackerLHR-CB1441A7");
                    OpenVRUtil.RemoveTrackerRole("/devices/htc/vive_trackerLHR-CB9AD1T0");
                    OpenVRUtil.RemoveTrackerRole("/devices/htc/vive_trackerLHR-CB9AD1T1");
                    OpenVRUtil.RemoveTrackerRole("/devices/htc/vive_trackerLHR-CB9AD1T2");

                    // In K2EX 0.9.1 we use custom serial ids
                    OpenVRUtil.RemoveTrackerRole("/devices/KinectToVR/Puck_HIP");
                    OpenVRUtil.RemoveTrackerRole("/devices/KinectToVR/Puck_LFOOT");
                    OpenVRUtil.RemoveTrackerRole("/devices/KinectToVR/Puck_RFOOT");
                } catch ( Exception e ) {
                    control.LogError($"{LogStrings.FailRemoveConflictingTrackerRoles}! {LogStrings.ViewLogs}");
                    Logger.Fatal($"{LogStrings.FailRemoveConflictingTrackerRoles}:\n{Util.FormatException(e)})");
                    success = success && false;
                }
            }

            return success;
        }

        private bool CreateShortcuts(string path, ref InstallModuleProgress control) {

            // Start menu shortcut
            if ( InstallerStateManager.CreateStartMenuEntry ) {

                Logger.Info(LogStrings.CreatingStartMenuEntry);
                control.LogInfo(LogStrings.CreatingStartMenuEntry);

                try {
                    Util.CreateExecutableShortcut(
                        Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "..", "ProgramData", "Microsoft", "Windows", "Start Menu", "Programs", "Amethyst.lnk")),
                        path,
                        Path.Combine(path, "Amethyst.exe"),
                        "Launch Amethyst");
                } catch ( Exception e ) {
                    control.LogError($"{LogStrings.FailedCreateStartMenuEntry}! {LogStrings.ViewLogs}");
                    Logger.Fatal($"{LogStrings.FailedCreateStartMenuEntry}:\n{Util.FormatException(e)})");
                }
            }

            if ( InstallerStateManager.CreateDesktopShortcut ) {

                Logger.Info(LogStrings.CreatingDesktopEntry);
                control.LogInfo(LogStrings.CreatingDesktopEntry);

                try {
                    Util.CreateExecutableShortcut(
                        Path.GetFullPath(@"C:\Users\Public\Desktop\Amethyst.lnk"),
                        path,
                        Path.Combine(path, "Amethyst.exe"),
                        "Launch Amethyst");
                } catch ( Exception e ) {
                    control.LogError($"{LogStrings.FailedCreateDesktopEntry}! {LogStrings.ViewLogs}");
                    Logger.Fatal($"{LogStrings.FailedCreateDesktopEntry}:\n{Util.FormatException(e)})");
                }
            }

            return true;
        }

        private bool CreateUninstallEntry(string target, ref InstallModuleProgress control) {

            var amethystExecutable = Path.GetFullPath(Path.Combine(target, "Amethyst.exe"));
            var amethystInstallerExecutable = Path.GetFullPath(Path.Combine(target, "Amethyst-Installer.exe"));

            control.LogInfo(LogStrings.CreatingUninstallExecutable);
            Logger.Info(LogStrings.CreatingUninstallExecutable);

            try {
                if ( !InstallerStateManager.IsUpdating ) {
                    // We check the paths by setting to lowercase as NTFS is case insensitive
                    if ( Path.GetFullPath(target).ToLowerInvariant() != Path.GetFullPath(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)).ToLowerInvariant() ) {

                        // Try copying the installer from wherever the we are running from to the uninstall directory
                        string selfExecutable = Assembly.GetExecutingAssembly().Location;
                        Logger.Info("Installer at:: " + selfExecutable);
                        File.Copy(selfExecutable, amethystInstallerExecutable, true);
                    }
                }

                // Also unblock the installer in AME-DIR\Amethyst-Installer.exe, we have no clue whether the user
                // unblocked the installer executable or not, so let's fix that to be sure
                Shell.Unblock(amethystInstallerExecutable);

            } catch ( Exception e ) {
                control.LogError($"{LogStrings.CreateUninstallExecutableFail}! {LogStrings.ViewLogs}");
                Logger.Fatal($"{LogStrings.CreateUninstallExecutableFail}:\n{Util.FormatException(e)})");
                return true;
            }

            control.LogInfo(LogStrings.CreatingUninstallEntry);
            Logger.Info(LogStrings.CreatingUninstallEntry);

            UninstallEntry entry = new UninstallEntry(){
                DisplayName = "Amethyst",
                Publisher = "K2VR Team",
                DisplayIcon = $"{amethystExecutable},0", // something.exe,0 ; number is icon index in the binary's resources
                
                HelpLink = "https://k2vr.tech/",
                URLInfoAbout = Constants.DiscordInvite,

                DisplayVersion = Module.DisplayVersion,
                ApplicationVersion = Module.DisplayVersion,

                InstallLocation = target,
                UninstallString = amethystInstallerExecutable + " --uninstall",
                ModifyPath = amethystInstallerExecutable + " --modify"
            };

            try {
                UninstallUtil.RegisterUninstallEntry(entry, "Amethyst");
                return true;
            } catch ( Exception e ) {
                control.LogError($"{LogStrings.CreateUninstallEntryFailed}! {LogStrings.ViewLogs}");
                Logger.Fatal($"{LogStrings.CreateUninstallEntryFailed}:\n{Util.FormatException(e)})");
            }

            return true;
        }

        private bool HandleInstallerPersistence(ref InstallModuleProgress control) {

            try {

                control.LogInfo(LogStrings.CreatingUpgradeList);
                Logger.Info(LogStrings.CreatingUpgradeList);

                // Write shit to appdata so that the installer will persist
                string installerConfigPath = Path.GetFullPath(Path.Combine(Constants.AmethystConfigDirectory, "Modules.json"));
                Dictionary<string, int> moduleList = new Dictionary<string, int>();
                foreach ( var module in InstallerStateManager.ModulesToInstall ) {
                    // Only track visible modules
                    if ( module.Visible ) {
                        moduleList.Add(module.Id, module.InternalVersion);
                    }
                }

                // Concat with existing JSON
                if ( File.Exists(installerConfigPath) ) {

                    // The file exists! Try reading it
                    try {

                        string fileContents = File.ReadAllText(installerConfigPath);
                        Dictionary<string, int> config = JsonConvert.DeserializeObject<Dictionary<string, int>>(fileContents);
                        foreach (var item in config) {
                            if (!moduleList.ContainsKey(item.Key)) {
                                moduleList.Add(item.Key, item.Value);
                            }
                        }

                    } catch ( Exception e ) {
                        Logger.Fatal($"Failed to load or parse file \"{installerConfigPath}\"!");
                        Logger.Fatal(Util.FormatException(e));
                    }
                }

                string jsonModules = JsonConvert.SerializeObject(moduleList, Formatting.Indented);
                File.WriteAllText(installerConfigPath, jsonModules);

                control.LogInfo(LogStrings.CreatingUninstallList);
                Logger.Info(LogStrings.CreatingUninstallList);

                // Dump the list of files to uninstall for this specific Amethyst build for later installer builds
                // This list will be used during the uninstall process later
                // Attempt to grab the list from remote first

                string uninstallListPath = Path.GetFullPath(Path.Combine(Constants.AmethystConfigDirectory, "UninstallList.json"));
                try {
                    Logger.Info("Fetching uninstall list...");
                    var uninstallList = Download.GetStringAsync(Constants.ApiDomain + $"amethyst/{Module.InternalVersion}/items");
                    File.WriteAllText(uninstallListPath, uninstallList);
                    Logger.Info("Fetched uninstall list!");

                } catch ( Exception e ) {
                    Logger.Fatal("Failed to fetch uninstall list!");
                    Logger.Fatal(Util.FormatException(e));

                    Logger.Warn("Using fallback, possibly out to date...");
                    Util.ExtractResourceToFile("UninstallList.json", uninstallListPath);
                }

                return true;

            } catch ( Exception e ) {
                control.LogError($"{LogStrings.CreateInstallerListsFailed}! {LogStrings.ViewLogs}");
                Logger.Fatal(LogStrings.CreateInstallerListsFailed);
                Logger.Fatal(Util.FormatException(e));
                return false;
            }
        }

        private bool AdjustSteamVrSettings(ref InstallModuleProgress control) {

            bool overallSuccess = true;

            try {

                control.LogInfo(LogStrings.DisablingSteamVrHome);
                Logger.Info(LogStrings.DisablingSteamVrHome);

                OpenVRUtil.DisableSteamVrHome();
                overallSuccess = true;

            } catch ( Exception e ) {
                control.LogError($"{LogStrings.FailDisableSteamVrHome}! {LogStrings.ViewLogs}");
                Logger.Fatal(LogStrings.FailDisableSteamVrHome);
                Logger.Fatal(Util.FormatException(e));
                overallSuccess = false;
            }

            try {

                control.LogInfo(LogStrings.EnablingSteamVrAdvancedSettings);
                Logger.Info(LogStrings.EnablingSteamVrAdvancedSettings);

                OpenVRUtil.EnableAdvancedSettings();

                overallSuccess = overallSuccess && true;

            } catch ( Exception e ) {
                control.LogError($"{LogStrings.FailEnableSteamVrAdvancedSettings}! {LogStrings.ViewLogs}");
                Logger.Fatal(LogStrings.FailEnableSteamVrAdvancedSettings);
                Logger.Fatal(Util.FormatException(e));
                overallSuccess = overallSuccess && false;
            }

            return overallSuccess;
        }

        private bool RegisterProtocolLink(string target, ref InstallModuleProgress control) {

            var amethystInstallerExecutable = Path.GetFullPath(Path.Combine(target, "Amethyst-Installer.exe"));

            try {

                control.LogInfo(LogStrings.RegisteringAmethystProtocolLink);
                Logger.Info(LogStrings.RegisteringAmethystProtocolLink);

                // Get root key
                RegistryKey amethystKey = WindowsUtils.GetKey(Registry.ClassesRoot, "amethyst", true);

                // Write to root key
                amethystKey.SetValue(string.Empty,      $"URL:Amethyst Protocol",   RegistryValueKind.String);  // (Default)
                amethystKey.SetValue("ProtocolVersion", 1,                          RegistryValueKind.DWord);   // ProtocolVersion
                amethystKey.SetValue("URL Protocol",    string.Empty,               RegistryValueKind.String);

                // Icon
                RegistryKey iconKey = WindowsUtils.GetKey(amethystKey, "DefaultIcon", true);
                iconKey.SetValue(string.Empty, "Amethyst.exe,0", RegistryValueKind.String);

                // Command
                RegistryKey shellKey    = WindowsUtils.GetKey(amethystKey, "shell", true);
                RegistryKey openKey     = WindowsUtils.GetKey(shellKey, "open", true);
                RegistryKey commandKey  = WindowsUtils.GetKey(openKey, "command", true);
                commandKey.SetValue(string.Empty, $"\"{amethystInstallerExecutable}\" \"%1\" %*", RegistryValueKind.String);

                return true;

            } catch ( Exception e ) {
                control.LogError($"{LogStrings.FailRegisterAmethystProtocolLink}! {LogStrings.ViewLogs}");
                Logger.Fatal(LogStrings.FailRegisterAmethystProtocolLink);
                Logger.Fatal(Util.FormatException(e));
                return false;
            }
        }
    }
}
