using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Livet;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows.Markup;
/* Hello! Put the abbreviation or acronym of your name here, and then use that for further comments. Example: "NG: WHAT IS GOING ON I DON'T KNOW ANYTHING ABOUT WHAT'S GOING ON WHAT DOES THIS DO"
 * NordGeit = NG
 * 
 */

/* NG: Future quests for anyone interested in refining this project further:
 * Add the ability to choose from where to pull the package_list.json file from, or to load a local one, so that there can be multiple lists available to use. Use something like a config file, perhaps?
 * Just... Actual error messages, for Odins sake
 * Disabling the SHA-1 check, if it doesn't nuke everything. Most useful for MMAccel since it might be updated quite frequently.
 * I'd do them, but I can't code, and it's gonna take quite a while for me to learn enough to implement that kind of stuff here.
 * 
 * DeepL-san! I summon you!
 * これを読んでいる日本人の皆さんはというと...。
 * MMAccelがプラグインをどのように扱うかを知って、こういうプログラムのアイデアが浮かんだんだけど、実際に形として実装されているのを見ると...。君たちは本当に秘密だらけだね。
 * 
 * どうしても日本語訳をつけたいのなら、私は気にしない。好きにしてください。
 * 私が先駆者になれるくらい強くなるまで、あなたについていきます。
 * [NG_MSG_END]
 * 
 */

namespace MMDPluginInstallManager.Models
{

    #region JsonData

    public class MMDPluginData
    {
        public string[][] InstallDir { get; set; }

        public string Readme { get; set; }

        public string SHA1Hash { get; set; }

        public string Title { get; set; }

        public string URL { get; set; }

        public string Version { get; set; }
    }

    public class MMDPluginPackage
    {
        public string Version { get; set; }

        public string ReadMeFilePath { get; set; }

        public List<string> InstalledDLL { get; set; } = new List<string>();
    }

    #endregion JsonData

    public class Model : NotificationObject
    {
        private const string MMDPluginPackageJsonFilename = "MMDPlugin.Package.json";
        /*
         * NotificationObjectはプロパティ変更通知の仕組みを実装したオブジェクトです。
         */

        private string _installPath;

        public Dictionary<string, MMDPluginPackage> MMDInstalledPluginPackage { get; private set; }

        #region IsInstalledMMDPlugin変更通知プロパティ

        private bool _IsInstalledMMDPlugin;

        public bool IsInstalledMMDPlugin
        {
            get { return _IsInstalledMMDPlugin; }
            set
            {
                if (_IsInstalledMMDPlugin == value)
                {
                    return;
                }
                _IsInstalledMMDPlugin = value;
                RaisePropertyChanged();
            }
        }

        #endregion

        public Dictionary<string, DownloadPluginData> DownloadPluginDic { get; } =
            new Dictionary<string, DownloadPluginData>();

        private string GetMMDPluginPackageJsonFilename()
        {
            var path = Path.Combine(_installPath, "plugin/");
            Directory.CreateDirectory(path);
            return Path.Combine(path, MMDPluginPackageJsonFilename);
        }

        public async Task UninstallPlugin(string mmdPluginName)
        {
            await Task.Run(() =>
            {
                var item = MMDInstalledPluginPackage[mmdPluginName];
                foreach (var i in item.InstalledDLL)
                {
                    File.Delete(i);
                }
                MMDInstalledPluginPackage.Remove(mmdPluginName);
                RaisePropertyChanged(nameof(DownloadPluginDic));
            });
        }

        public async Task<MMDPluginPackage> InstallPlugin(string zipPath)
        {
            return await Task.Run(() =>
            {
                var hash = CreateSHA1Hash(zipPath);
                DownloadPluginData loadItem;

                if (DownloadPluginDic.TryGetValue(hash, out loadItem) == false)
                {
                    throw new ArgumentException("A hash matching the SHA1 of the zip file was not found.\n");
                }

                var packageData = new MMDPluginPackage
                {
                    Version = loadItem.LatestVersion
                };
                using (var zipArchive = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in zipArchive.Entries)
                    {
                        var filename = entry.FullName.Replace('/', '\\');
                        if (string.IsNullOrEmpty(filename) || filename[filename.Length - 1] == '\\')
                        {
                            continue;
                        }
                        string path;
                        if (!loadItem.TryGetInstallDir(filename, out path))
                        {
                            continue;
                        }

                        path = Path.Combine(_installPath, path, Path.GetFileName(filename));
                        Directory.CreateDirectory(Directory.GetParent(path).FullName);
                        entry.ExtractToFile(path, true);

                        if (Path.GetExtension(path).ToLower() == ".dll")
                        {
                            packageData.InstalledDLL.Add(path);
                        }

                        if (loadItem.IsReadMeFile(filename))
                        {
                            packageData.ReadMeFilePath = path;
                        }
                    }
                }
                MMDInstalledPluginPackage[loadItem.Title] = packageData;
                File.WriteAllText(GetMMDPluginPackageJsonFilename(),
                                  JsonConvert.SerializeObject(MMDInstalledPluginPackage));
                RaisePropertyChanged(nameof(DownloadPluginDic));
                return packageData;
            });
        }

        public void SetMMDDirectory(string installPath)
        {
            if (Path.GetFileName(installPath) == string.Empty)
            {
                installPath += @"MikuMikuDance.exe";
            }
            if (File.Exists(installPath) == false)
            {
                throw new FileNotFoundException("The MikuMikuDance.exe is not found.");
            }
            _installPath = Directory.GetParent(installPath).FullName;
        }

        private async Task<MMDPluginData[]> GetPackageList()
        {
            var text = "";
            try
            {
                using (var wc = new WebClient())
                {
                    //NG: Line below fixes the issue where the included json file gets overwritten with an empty one, because it failed to properly negotiate TLS. Thanks, the combined power of StackOverflow and Duke Nukem!
                    System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    await wc.DownloadFileTaskAsync(
                                                   "https://raw.githubusercontent.com/PTOM76/MMDPluginInstallManager/master/MMDPluginInstallManager/package_list.json",
                                                   @"package_list.json");
                    text = File.ReadAllText("package_list.json");
                }
            }
            catch (Exception)
            {
                //NG: This is the fallback, hardcoded into the program. Make sure every entry in the package_list.json has all the entries included, even if it has to resort to using a dummy or a bogus one, to prevent this fallback!
                text = "[{\"Title\":\"MMDPlugin\",\"URL\":\"https://bowlroll.net/file/121761\",\"Version\":0.41,\"Readme\":\"MMDPlugin_Readme.txt\",\"SHA1Hash\":\"682cc15082b3db2cff6808480d12f4e92413e085\",\"InstallDir\":[[\"d3d9.dll\",\"\"],[\"d3dx9_43.dll\",\"\"],[\"MMDPlugin.dll\",\"\"],[\"MMDPlugin_Readme.txt\",\"plugin/MMDPlugin/\"]]},{\"Title\":\"MMDUtility\",\"URL\":\"https://bowlroll.net/file/270417\",\"Version\":0.11,\"Readme\":\"MMDUtility_Readme.txt\",\"SHA1Hash\":\"769e0e5b0faf20328bcf3acc5e6a4e3129c80504\",\"InstallDir\":[[\"plugin/\",\"\"],[\"MMDUtility_Readme.txt\",\"plugin/MMDUtility\"]]},{\"Title\":\"EffekseerforMMD\",\"URL\":\"https://bowlroll.net/file/121167\",\"Version\":0.25,\"Readme\":\"EffekseerForMMD_Readme.txt\",\"SHA1Hash\":\"ab16e90d4b6c7bafb4505589c3ba7de864e55121\",\"InstallDir\":[[\"plugin/\",\"\"],[\"EffekseerForMMD_Readme.txt\",\"plugin/EffekseerForMMD\"]]},{\"Title\":\"MikuMikuEffect\",\"URL\":\"https://bowlroll.net/file/35013\",\"Version\":0.37,\"Readme\":\"MMEffect_x64_v037/MMEffect.txt\",\"SHA1Hash\":\"c9304108d61517e9ba47e05118dda1df6063c14c\",\"InstallDir\":[[\"MMEffect_x64_v037/\",\"../plugin/mme/\"]]},{\"Title\":\"MMAccel\",\"URL\":\"https://bowlroll.net/file/89669\",\"Version\":1.60,\"Readme\":\"MMAccel_64_v1_60/mmaccel_Readme.txt\",\"SHA1Hash\":\"aa89b9c2474d8227ca2b6ccd0120a38a491e40aa\",\"InstallDir\":[[\"MMAccel_64_v1_60/mmaccel/\",\"../../plugin/mmaccel/\"],[\"MMAccel_64_v1_60/mmaccel_Readme.txt\",\"../plugin/mmaccel/\"]]},{\"Title\":\"MMPlus\",\"URL\":\"https://bowlroll.net/file/192172\",\"Version\":\"1.6.5.14\",\"Readme\":\"MMPlus_ver1.6.5.14/MMPlus_readme.txt\",\"SHA1Hash\":\"bbad109b1bd1855aebc216c9f155f24431be83d6\",\"InstallDir\":[[\"MMPlus_ver1.6.5.14/\",\"../plugin/MMPlus/\"]]},{\"Title\":\"MMDDiscordRPC\",\"URL\":\"https://bowlroll.net/file/270406\",\"Version\":1.0,\"Readme\":\"readme.md\",\"SHA1Hash\":\"161ed4b685d445337cde92092612fcb546c23bd0\",\"InstallDir\":[[\"lib/\",\"../plugin/MMDPluginDLL/\"],[\"MMDDiscordRPC.dll\",\"plugin/MMDPluginDLL/\"],[\"readme.md\",\"plugin/MMDPluginDLL/\"]]},{\"Title\":\"MMDPluginDLL\",\"URL\":\"https://bowlroll.net/file/270418\",\"Version\":\"1.0.0.1\",\"Readme\":\"README.md\",\"SHA1Hash\":\"54bb8b28327c78e1593c049fba9b3fd7973bbc98\",\"InstallDir\":[[\"qSetCameraFollowBone.dll\",\"plugin/MMDPluginDLL/\"],[\"qDispPlayingFrame.dll\",\"plugin/MMDPluginDLL/\"],[\"qCameraModeUndo.ini\",\"plugin/MMDPluginDLL/\"],[\"qCameraModeUndo.dll\",\"plugin/MMDPluginDLL/\"],[\"README.md\",\"plugin/MMDPluginDLL/\"]]}]";
            }
            return JsonConvert.DeserializeObject<MMDPluginData[]>(text);
        }

        public async Task LoadPluginData()
        {
            try
            {
                var mmdpluginPackageJsonText = File.ReadAllText(GetMMDPluginPackageJsonFilename());
                MMDInstalledPluginPackage =
                    JsonConvert.DeserializeObject<Dictionary<string, MMDPluginPackage>>(mmdpluginPackageJsonText);
            }
            catch (Exception)
            {
                MMDInstalledPluginPackage = new Dictionary<string, MMDPluginPackage>();
            }

            float mmdPluginVersion = -1;
            var jsonData = await GetPackageList();
            foreach (var item in jsonData)
            {
                MMDPluginPackage package = null;
                MMDInstalledPluginPackage.TryGetValue(item.Title, out package);
                if (string.IsNullOrEmpty(item.SHA1Hash))
                {
                    // TODO エラーログの追加
                    continue;
                }
                DownloadPluginDic.Add(item.SHA1Hash, new DownloadPluginData(item.InstallDir, item.Readme)
                {
                    Url = item.URL,
                    LatestVersion = item.Version,
                    Title = item.Title,
                });
                if (item.Title == "MMDPlugin")
                {
                    mmdPluginVersion = float.Parse(item.Version);
                }
            }
            RaisePropertyChanged(nameof(DownloadPluginDic));


            MMDPluginPackage mmdPluginPackage;
            if (MMDInstalledPluginPackage.TryGetValue("MMDPlugin", out mmdPluginPackage))
            {
                if (Math.Abs(float.Parse(mmdPluginPackage.Version) - mmdPluginVersion) < 1e-5f)
                {
                    IsInstalledMMDPlugin = true;
                }
            }
        }

        private string CreateSHA1Hash(string zipPath)
        {
            using (var fs = new FileStream(zipPath, FileMode.Open))
            {
                var provider = new SHA1CryptoServiceProvider();
                var hash = provider.ComputeHash(fs);
                return BitConverter.ToString(hash).ToLower().Replace("-", "");
            }
        }

        public class DownloadPluginData
        {
            public string LatestVersion { get; set; }

            public string Title { get; set; }

            public string Url { get; set; }

            private readonly string[][] _installDir;

            private readonly string _readme;

            public DownloadPluginData(string[][] installDir, string readMe)
            {
                _installDir = installDir;
                foreach (var t in _installDir)
                {
                    t[0] = t[0].Replace('/', '\\');
                }
                _readme = readMe.Replace('/', '\\');
            }

            public bool TryGetInstallDir(string filename, out string path)
            {
                foreach (var item in _installDir)
                {
                    if (filename.StartsWith(item[0], StringComparison.OrdinalIgnoreCase))
                    {
                        path = Path.Combine(Path.GetDirectoryName(filename), item[1]);
                        return true;
                    }
                }
                path = null;
                return false;
            }

            public bool IsReadMeFile(string filename)
            {
                return string.Compare(filename, _readme, StringComparison.OrdinalIgnoreCase) == 0;
            }
        }
    }
}