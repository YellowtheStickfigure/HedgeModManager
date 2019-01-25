﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using HMMResources = HedgeModManager.Properties.Resources;

namespace HedgeModManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {

        public static string StartDirectory = AppDomain.CurrentDomain.BaseDirectory;
        public static string ProgramName = "HedgeModManager";
        public static string VersionString = "7.0-dev";
        public static string ModsDbPath;
        public static string ConfigPath;
        public static string[] Args;
        public static Game CurrentGame = Games.Unknown;
        public static CPKREDIRConfig Config;
        public static List<SteamGame> SteamGames = null;
        public static bool Restart = false;

        public const string WebRequestUserAgent =
            "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; .NET CLR 1.0.3705;)";

        public static byte[] CPKREDIR = new byte[] { 0x63, 0x70, 0x6B, 0x72, 0x65, 0x64, 0x69, 0x72 };
        public static byte[] IMAGEHLP = new byte[] { 0x69, 0x6D, 0x61, 0x67, 0x65, 0x68, 0x6C, 0x70 };

        [STAThread]
        public static void Main(string[] args)
        {
            // Use TLSv1.2
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            var application = new App();
            Args = args;

#if !DEBUG
            // Enable our Crash Window if Compiled in Release
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                ExceptionWindow.UnhandledExceptionEventHandler(e.ExceptionObject as Exception, e.IsTerminating);
            };
#endif


            Steam.Init();
#if DEBUG
            // Find a Steam Game
            SteamGames = Steam.SearchForGames("Sonic Forces");
            var steamGame = SteamGames.FirstOrDefault();
            SelectSteamGame(steamGame);
            StartDirectory = steamGame.RootDirectory;
#else
            SteamGames = Steam.SearchForGames();
            foreach (var game in Games.GetSupportedGames())
            {
                if (File.Exists(Path.Combine(StartDirectory, game.ExecuteableName)))
                {
                    CurrentGame = game;
                }
            }
#endif
            ModsDbPath = Path.Combine(StartDirectory, "Mods");
            ConfigPath = Path.Combine(StartDirectory, "cpkredir.ini");

            if (CurrentGame == Games.Unknown)
            {
                var box = new HedgeMessageBox("No Game Detected!", HMMResources.STR_MSG_NOGAME);

                box.AddButton("  Cancel  ", () =>
                {
                    box.Close();
                });
                box.AddButton("  Run Installer  ", () =>
                {
                    box.Visibility = Visibility.Collapsed;
                    new InstallWindow().ShowDialog();
                    box.Close();
                });
                box.ShowDialog();
                return;
            }

            if (CurrentGame.SupportsCPKREDIR)
            {
                if (!File.Exists(Path.Combine(StartDirectory, "cpkredir.dll")))
                {
                    File.WriteAllBytes(Path.Combine(StartDirectory, "cpkredir.dll"), HMMResources.DAT_CPKREDIR_DLL);
                    File.WriteAllBytes(Path.Combine(StartDirectory, "cpkredir.txt"), HMMResources.DAT_CPKREDIR_TXT);
                }
            }

            do
            {
                Config = new CPKREDIRConfig(ConfigPath);
                Restart = false;
                application.InitializeComponent();
                application.Run();
            }
            while (Restart);
        }

        /// <summary>
        /// Sets the Current Game to the passed Steam Game
        /// </summary>
        /// <param name="steamGame">Steam Game to select</param>
        public static void SelectSteamGame(SteamGame steamGame)
        {
            if (steamGame == null)
                return;
            StartDirectory = steamGame.RootDirectory;
            if (steamGame.GameID == "329440")
                CurrentGame = Games.SonicLostWorld;
            if (steamGame.GameID == "71340")
                CurrentGame = Games.SonicGenerations;
            if (steamGame.GameID == "637100")
                CurrentGame = Games.SonicForces;
        }

        /// <summary>
        /// Finds and returns an instance of SteamGame from a HMM Game
        /// </summary>
        /// <param name="game">HMM Game</param>
        /// <returns>Steam Game</returns>
        public static SteamGame GetSteamGame(Game game)
        {
            return SteamGames.FirstOrDefault(t => t.GameName == game.GameName);
        }

        /// <summary>
        /// Checks if CPKREDIR is currently Installed
        /// </summary>
        /// <param name="executablePath">Path to the executable</param>
        /// <returns>
        /// TRUE: CPKREDIR is installed
        /// FALSE: CPKREDIR is not Installed
        /// </returns>
        public static bool IsCPKREDIRInstalled(string executablePath)
        {
            var data = File.ReadAllBytes(executablePath);
            var installed = BoyerMooreSearch(data, CPKREDIR) > 0;

            data = null;
            return installed;
        }

        /// <summary>
        /// Installs or Uninstalls CPKREDIR
        /// </summary>
        /// <param name="executablePath">Path to the executable</param>
        /// <param name="install">
        /// TRUE: Installs CPKREDIR (default)
        /// FALSE: Uninstalls CPKREDIR
        /// NULL: Toggle
        /// </param>
        public static void InstallCPKREDIR(string executablePath, bool? install = true)
        {
            // Backup Executable
            File.Copy(executablePath, $"{executablePath}.bak", true);

            // Read Executable
            var data = File.ReadAllBytes(executablePath);
            var offset = -1;

            // Search for the .rdata entry
            byte[] rdata = Encoding.ASCII.GetBytes(".rdata");
            byte[] buff = new byte[0x300 - 0x160];
            Array.Copy(data, 0x160, buff, 0, buff.Length);
            offset = BoyerMooreSearch(buff, rdata) + 0x160;

            // Read Segment Entry Data
            int size = BitConverter.ToInt32(data, offset + 0x10);
            int offset_ = BitConverter.ToInt32(data, offset + 0x14);
            
            // Read Segment
            buff = new byte[size];
            Array.Copy(data, offset_, buff, 0, buff.Length);

            bool IsCPKREDIR = false;
            offset = BoyerMooreSearch(buff, IMAGEHLP);
            IsCPKREDIR = offset == -1;
            if (offset == -1)
                offset = BoyerMooreSearch(buff, CPKREDIR);
            offset += offset_;
            byte[] buffer = null;
            // Toggle
            if (install == null)
                buffer = IsCPKREDIR ? IMAGEHLP : CPKREDIR;
            else
                buffer = install == true ? IMAGEHLP : CPKREDIR;

            // Write Patch to file
            using (var stream = File.OpenWrite(executablePath))
            {
                stream.Seek(offset, SeekOrigin.Begin);
                stream.Write(buffer, 0, CPKREDIR.Length);
            }
        }

        public static void InstallOtherLoader(bool toggle = true)
        {
            string DLLFileName = Path.Combine(StartDirectory, $"d3d{CurrentGame.DirectXVersion}.dll");

            if (File.Exists(DLLFileName) && toggle)
            {
                File.Delete(DLLFileName);
                return;
            }

            // Downloads the Loader
            using (var client = new WebClient())
                client.DownloadFile(CurrentGame.ModLoaderDownloadURL, DLLFileName);
            var ini = new IniFile();
            // Get ModLoader List
            using (var stream = WebRequest.Create(HMMResources.URL_HMM_LOADERS).GetResponse().GetResponseStream())
                ini.Read(stream);
            
            if (ini.Groups.ContainsKey(CurrentGame.GameName))
            {
                var group = ini[CurrentGame.GameName];

                if (group.Params.ContainsKey("LoaderVersion"))
                    Config.ModLoaderVersion = group["LoaderVersion"];
                if (group.Params.ContainsKey("LoaderName"))
                    Config.ModLoaderName = group["LoaderName"];
                if (group.Params.ContainsKey("LoaderName2"))
                    Config.ModLoaderNameWithVersion = group["LoaderName2"];
            }

            // Checks if the loader is downloaded and saved, If it isn't then write the local copy
            if (!File.Exists(DLLFileName))
            {
                // Install local copy
                var loader = CurrentGame.ModLoaderData;
                if (loader != null)
                    File.WriteAllBytes(DLLFileName, loader);
                else
                    throw new NotImplementedException("No Loader is available!");
            }
        }

        public static int BoyerMooreSearch(byte[] haystack, byte[] needle)
        {
            int[] lookup = new int[256];
            for (int i = 0; i < lookup.Length; i++) { lookup[i] = needle.Length; }

            for (int i = 0; i < needle.Length; i++)
            {
                lookup[needle[i]] = needle.Length - i - 1;
            }

            int index = needle.Length - 1;
            var lastByte = needle.Last();
            while (index < haystack.Length)
            {
                var checkByte = haystack[index];
                if (haystack[index] == lastByte)
                {
                    bool found = true;
                    for (int j = needle.Length - 2; j >= 0; j--)
                    {
                        if (haystack[index - needle.Length + j + 1] != needle[j])
                        {
                            found = false;
                            break;
                        }
                    }

                    if (found)
                        return index - needle.Length + 1;
                    else
                        index++;
                }
                else
                {
                    index += lookup[checkByte];
                }
            }
            return -1;
        }

        // https://stackoverflow.com/questions/11660184/c-sharp-check-if-run-as-administrator
        public static bool RunningAsAdmin()
        {
            return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        }

    }
}
