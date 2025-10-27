using M2Mod.Config;
using M2Mod.Dialogs;
using M2Mod.Interop;
using M2Mod.Interop.Structures;
using M2Mod.Tools;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace M2Mod
{
    public partial class M2ModForm : Form
    {
        private bool _ignoreErrors;
        private bool _ignoreWarnings;

        private IntPtr _preloadM2 = IntPtr.Zero;

        private static readonly string ListUrl = "https://github.com/wowdev/wow-listfile/releases/latest/download/community-listfile.csv";
        private static readonly string ListFilePath = Path.Combine("mappings", "listfile.csv");

        public M2ModForm()
        {
            InitializeComponent();

            Icon = Properties.Resources.Icon;

            InitializeLogger();

            ProfileManager.Load("", true);

            InitializeProfiles();
            InitializeFormData();

            CheckListFile();

            formInitialized = true;
        }

        private void InitializeFormData()
        {
            textBoxInputM2Exp.Text = ProfileManager.CurrentProfile.Configuration.InputM2Exp;
            textBoxOutputM2I.Text = ProfileManager.CurrentProfile.Configuration.OutputM2I;
            textBoxInputM2Imp.Text = ProfileManager.CurrentProfile.Configuration.InputM2Imp;
            textBoxInputM2I.Text = ProfileManager.CurrentProfile.Configuration.InputM2I;
            textBoxReplaceM2.Text = ProfileManager.CurrentProfile.Configuration.ReplaceM2;
            checkBoxReplaceM2.Checked = ProfileManager.CurrentProfile.Configuration.ReplaceM2Checked;
        }

        private void CheckListFile()
        {
            if (File.Exists(ListFilePath))
            {
                if (DateTime.Now - File.GetLastWriteTime(ListFilePath) > TimeSpan.FromDays(7))
                {
                    Log(LogLevel.Info, "Listfile is outdated. Removing it...");
                    File.Delete(ListFilePath);
                }
            }

            if (!File.Exists(ListFilePath))
            {
                Log(LogLevel.Info, "Downloading list file. Please wait...");

                using (HttpClient httpClient = new HttpClient())
                using (HttpResponseMessage response = httpClient.GetAsync(ListUrl).Result)
                using (Stream contentStream = response.Content.ReadAsStreamAsync().Result)
                using (FileStream fileStream = new FileStream(ListFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
                {
                    contentStream.CopyTo(fileStream);
                }
            }
        }

        private static string VersionString => $"v{Version.Major}.{Version.Minor}.{Version.Patch}";

        private Imports.LoggerDelegate logDelegate;

        private void InitializeLogger()
        {
            logDelegate = Log;
            Imports.AttachLoggerCallback(LogLevel.AllDefault, logDelegate);
        }

        private void ResetIgnoreWarnings()
        {
            _ignoreErrors = _ignoreWarnings = false;
        }

        private void Log(LogLevel logLevel, string message)
        {
            logTextBox.AppendLine(logLevel, message);

            if (logLevel.HasFlag(LogLevel.Error))
            {
                if (!_ignoreErrors)
                {
                    if (ErrorForm.ShowError(message) == DialogResult.Ignore)
                        _ignoreErrors = true;
                }
            }
            else if (logLevel.HasFlag(LogLevel.Warning))
            {
                if (!_ignoreWarnings)
                {
                    if (ErrorForm.ShowWarning(message) == DialogResult.Ignore)
                        _ignoreWarnings = true;
                }
            }
        }

        private void ButtonInputM2ExpBrowse_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = Filters.M2;
                dialog.FileName = Path.GetFileName(textBoxInputM2Exp.Text);
                dialog.InitialDirectory = textBoxInputM2Exp.Text.Length > 0 ? Path.GetDirectoryName(textBoxInputM2Exp.Text) : ProfileManager.CurrentProfile.Settings.WorkingDirectory;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    textBoxInputM2Exp.Text = textBoxInputM2Imp.Text = dialog.FileName;
                    textBoxOutputM2I.Text = textBoxInputM2I.Text = Path.ChangeExtension(dialog.FileName, "m2i");
                }
            }
        }

        private void ButtonOutputM2IBrowse_Click(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = Filters.M2I;
                try
                {
                    dialog.FileName = textBoxOutputM2I.Text;
                    dialog.InitialDirectory = Path.GetDirectoryName(textBoxOutputM2I.Text);
                }
                catch
                {
                    // ignored
                }

                if (dialog.ShowDialog() == DialogResult.OK)
                    textBoxOutputM2I.Text = dialog.FileName;
            }
        }

        private void ButtonInputM2IBrowse_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = Filters.M2I;
                try
                {
                    dialog.FileName = textBoxInputM2I.Text;
                    dialog.InitialDirectory = Path.GetDirectoryName(textBoxInputM2I.Text);
                }
                catch
                {
                    // ignored
                }

                if (dialog.ShowDialog() == DialogResult.OK)
                    textBoxInputM2I.Text = dialog.FileName;
            }
        }

        private async void ImportButtonPreload_Click(object sender, EventArgs e)
        {
            ResetIgnoreWarnings();

            importButtonPreload.Enabled = false;
            importButtonPreload.Refresh();
            SetStatus("Preloading...");

            if (_preloadM2 != IntPtr.Zero)
            {
                Imports.M2_Free(_preloadM2);
                _preloadM2 = IntPtr.Zero;
            }

            // Check fields.
            if (textBoxInputM2I.Text.Length == 0)
            {
                SetStatus("Error: No input M2I file Specified.");
                PreloadTransition(false);
                return;
            }

            // import M2
            if (textBoxInputM2Imp.Text.Length == 0)
            {
                SetStatus("Error: No input M2 file Specified.");
                PreloadTransition(false);
                return;
            }

            var inputM2ImpText = textBoxInputM2Imp.Text;
            var inputM2IText = textBoxInputM2I.Text;
            var replaceM2Text = textBoxReplaceM2.Text;
            var replaceM2Checked = checkBoxReplaceM2.Checked;
            var settings = ProfileManager.CurrentProfile.Settings;
            var rules = ProfileManager.CurrentProfile.Configuration.NormalizationConfig.GetRules();

            var result = await Task.Run(() =>
            {
                var preloadM2 = Imports.M2_Create(ref settings);

                var Error = Imports.M2_Load(preloadM2, inputM2ImpText);
                if (Error != M2LibError.OK)
                {
                    SetStatus(Imports.GetErrorText(Error));
                    PreloadTransition(false);
                    Imports.M2_Free(preloadM2);
                    return (IntPtr.Zero, false);
                }

                if (replaceM2Checked)
                {
                    Error = Imports.M2_SetReplaceM2(preloadM2, replaceM2Text);
                    if (Error != M2LibError.OK)
                    {
                        SetStatus(Imports.GetErrorText(Error));
                        PreloadTransition(false);
                        Imports.M2_Free(preloadM2);
                        return (IntPtr.Zero, false);
                    }
                }

                try
                {
                    foreach (var ruleSet in rules)
                    {
                        var sourceRules = ruleSet.SourceRules.Serialize().ToArray();
                        var targetRules = ruleSet.TargetRules.Serialize().ToArray();

                        Error = Imports.M2_AddNormalizationRule(preloadM2,
                            ruleSet.SourceType, sourceRules, sourceRules.Length,
                            ruleSet.TargetType, targetRules, targetRules.Length, ruleSet.PreferSourceDirection);
                        if (Error != M2LibError.OK)
                        {
                            SetStatus(Imports.GetErrorText(Error));
                            PreloadTransition(false);
                            Imports.M2_Free(preloadM2);
                            return (IntPtr.Zero, false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Invoke(new Action(() => MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                    SetStatus(ex.Message);
                    PreloadTransition(false);
                    Imports.M2_Free(preloadM2);
                    return (IntPtr.Zero, false);
                }

                // import M2I
                Error = Imports.M2_ImportM2Intermediate(preloadM2, inputM2IText);
                if (Error != M2LibError.OK)
                {
                    SetStatus(Imports.GetErrorText(Error));
                    PreloadTransition(false);
                    Imports.M2_Free(preloadM2);
                    return (IntPtr.Zero, false);
                }

                return (preloadM2, true);
            });

            _preloadM2 = result.Item1;

            if (result.Item2)
            {
                SetStatus("Preload finished.");
                PreloadTransition(true);
            }
        }

        private void PreloadTransition(bool On)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => PreloadTransition(On)));
                return;
            }

            if (On)
            {
                panelInputM2Import.Enabled = false;
                panelInputM2I.Enabled = false;

                importButtonPreload.Enabled = false;
                importButtonGo.Enabled = true;
                extraworkPanel.Enabled = true;
                importCancelButton.Enabled = true;

                checkBoxReplaceM2.Enabled = false;
                panelReplaceM2.Enabled = false;
            }
            else
            {
                panelInputM2Import.Enabled = true;
                panelInputM2I.Enabled = true;

                importButtonPreload.Enabled = true;
                importButtonGo.Enabled = false;
                extraworkPanel.Enabled = false;
                importCancelButton.Enabled = false;

                checkBoxReplaceM2.Enabled = true;
                panelReplaceM2.Enabled = checkBoxReplaceM2.Checked;

                if (_preloadM2 != IntPtr.Zero)
                {
                    Imports.M2_Free(_preloadM2);
                    _preloadM2 = IntPtr.Zero;
                }
            }
        }

        private void SettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenSettings();
        }

        private void OpenSettings()
        {
            using (var form = new SettingsForm())
                form.ShowDialog();

            InitializeProfiles();
            InitializeFormData();
        }

        private void InitializeProfiles()
        {
            var profileGuid = ProfileManager.CurrentProfile?.Id ?? Guid.Empty;

            profilesComboBox.Items.Clear();
            profilesComboBox.Items.AddRange(ProfileManager.GetProfiles().Cast<object>().ToArray());
            profilesComboBox.SelectedItem = ProfileManager.GetProfiles().FirstOrDefault(_ => _.Id == profileGuid) ??
                                            profilesComboBox.Items[0];
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void SetStatus(string status)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => SetStatus(status)));
                return;
            }

            toolStripStatusLabel1.Text = status;
            statusStrip1.Refresh();
        }

        private async void ExportButtonGo_Click(object sender, EventArgs e)
        {
            ResetIgnoreWarnings();

            exportButtonGo.Enabled = false;
            exportButtonGo.Refresh();
            SetStatus("Working...");

            // Check fields.
            if (textBoxInputM2Exp.Text.Length == 0)
            {
                SetStatus("M2LibError: No input M2 file Specified.");
                exportButtonGo.Enabled = true;
                return;
            }

            if (textBoxOutputM2I.Text.Length == 0)
            {
                SetStatus("M2LibError: No output M2I file Specified.");
                exportButtonGo.Enabled = true;
                return;
            }

            await Task.Run(() =>
            {
                var m2 = Imports.M2_Create(ref ProfileManager.CurrentProfile.Settings);

                // import M2
                var error = Imports.M2_Load(m2, textBoxInputM2Exp.Text);
                if (error != M2LibError.OK)
                {
                    SetStatus(Imports.GetErrorText(error));
                    Invoke(new Action(() => exportButtonGo.Enabled = true));
                    Imports.M2_Free(m2);
                    return;
                }

                // export M2I
                error = Imports.M2_ExportM2Intermediate(m2, textBoxOutputM2I.Text);
                if (error != M2LibError.OK)
                {
                    SetStatus(Imports.GetErrorText(error));
                    Invoke(new Action(() => exportButtonGo.Enabled = true));
                    Imports.M2_Free(m2);
                    return;
                }

                SetStatus("Export done.");

                Imports.M2_Free(m2);
            });

            exportButtonGo.Enabled = true;
        }

        private void ImportCancelButton_Click(object sender, EventArgs e)
        {
            SetStatus("Cancelled preload.");
            PreloadTransition(false);
        }

        private void CheckBoxReplaceM2_CheckedChanged(object sender, EventArgs e)
        {
            panelReplaceM2.Enabled = checkBoxReplaceM2.Checked;
        }

        private void ButtonReplaceM2Browse_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = Filters.M2;
                dialog.FileName = Path.GetFileName(textBoxReplaceM2.Text);
                dialog.InitialDirectory = textBoxReplaceM2.Text.Length > 0 ? Path.GetDirectoryName(textBoxReplaceM2.Text) : ProfileManager.CurrentProfile.Settings.WorkingDirectory;

                if (dialog.ShowDialog() == DialogResult.OK)
                    textBoxReplaceM2.Text = dialog.FileName;
            }
        }

        private void LoadListfileButton_Click(object sender, EventArgs e)
        {
            ResetIgnoreWarnings();

            using (var dialog = new FolderBrowserDialog())
            {
                dialog.ShowNewFolderButton = false;
                dialog.SelectedPath = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location ?? "");
                var result = dialog.ShowDialog();
                if (result != DialogResult.OK)
                    return;

                ProfileManager.CurrentProfile.Settings.MappingsDirectory = dialog.SelectedPath;
            }
        }

        private void TestInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
                return;

            TestFiles();
        }

        private async void TestFiles()
        {
            testOutputTextBox.Text = "Loading...";
            if (testInputTextBox.Text.Length == 0)
            {
                testOutputTextBox.Text = "";
                return;
            }

            var inputText = testInputTextBox.Text;
            var mappingsDirectory = ProfileManager.CurrentProfile.Settings.MappingsDirectory;

            var result = await Task.Run(() =>
            {
                var fileStorage = Imports.FileStorage_Get(mappingsDirectory);
                if (uint.TryParse(inputText, out var fileDataId))
                {
                    var info = Imports.FileStorage_GetFileInfoByFileDataId(fileStorage, fileDataId);
                    if (info != IntPtr.Zero)
                        return (Imports.FileInfo_GetPath(info), null, true);
                    else
                        return ("Not found in storage", null, false);
                }
                else
                {
                    var info = Imports.FileStorage_GetFileInfoByPartialPath(fileStorage, inputText);
                    if (info != IntPtr.Zero)
                    {
                        return (Imports.FileInfo_GetFileDataId(info).ToString(), Imports.FileInfo_GetPath(info), true);
                    }
                    else
                        return ("Not found in storage", null, false);
                }
            });

            testOutputTextBox.Text = result.Item1;
            if (result.Item3 && result.Item2 != null)
                testInputTextBox.Text = result.Item2;

            testOutputTextBox.Focus();
            testOutputTextBox.SelectAll();
        }

        private void FileTestButton_Click(object sender, EventArgs e)
        {
            TestFiles();
        }

        private void SaveFormDataToProfile(SettingsProfile profile)
        {
            profile.Configuration.InputM2Exp = textBoxInputM2Exp.Text;
            profile.Configuration.OutputM2I = this.textBoxOutputM2I.Text;
            profile.Configuration.InputM2Imp = this.textBoxInputM2Imp.Text;
            profile.Configuration.InputM2I = this.textBoxInputM2I.Text;
            profile.Configuration.ReplaceM2 = this.textBoxReplaceM2.Text;
            profile.Configuration.ReplaceM2Checked = this.checkBoxReplaceM2.Checked;
        }

        private void M2Mod_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveFormDataToProfile(ProfileManager.CurrentProfile);

            ProfileManager.Save();
        }

        private string GetTags(string url)
        {
            var request = (HttpWebRequest) WebRequest.Create(url);
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            var rest = (HttpWebResponse) request.GetResponse();

            using (var reader = new StreamReader(rest.GetResponseStream()))
                return reader.ReadToEnd();
        }

        private void CheckUpdates()
        {
            ResetIgnoreWarnings();

            var data = GetTags(
                @"https://bitbucket.org/!api/2.0/repositories/suncurio/m2mod/refs/tags?pagelen=30&q=name%20~%20%22v%22&sort=name");

            var json = JObject.Parse(data);

            var lastTag = json["values"]
                .Select(_ => _["name"].ToString())
                .Where(_ => Regex.IsMatch(_, @"v\d+\.\d+\.\d+"))
                .OrderBy(_ => _).Reverse()
                .FirstOrDefault();

            if (string.IsNullOrEmpty(lastTag))
                throw new Exception("Failed to get tags, empty result");

            if (!IsTagGreater(lastTag, VersionString))
            {
                ErrorForm.Show("Up to date", MessageBoxIcon.Information);
                return;
            }

            ErrorForm.ShowInfo(
                $"New version {lastTag} is available at https://bitbucket.org/suncurio/m2mod/downloads/");
        }

        private bool IsTagGreater(string source, string target)
        {
            Func<string, uint> f = tag =>
            {
                var match = Regex.Match(tag, @"(\d)+\.(\d+)\.(\d+)");
                if (!match.Success)
                    return 0;

                return uint.Parse(match.Groups[1].ToString()) << 16 |
                       uint.Parse(match.Groups[2].ToString()) << 8 |
                       uint.Parse(match.Groups[3].ToString());
            };

            return f(source) > f(target);
        }

        private Task _updateTask;
        private bool formInitialized;

        private void CheckUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_updateTask != null && !_updateTask.IsCompleted)
                return;

            _updateTask = Task.Run(() =>
            {
                try
                {
                    CheckUpdates();
                }
                catch (Exception ex)
                {
                    this.Invoke(new Action(() => {
                        Log(LogLevel.Error, $"Failed to check updates: {ex.Message}");
                    }));

                    MessageBox.Show("Failed to check updates", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            });
        }

        private void CompareModelsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ResetIgnoreWarnings();

            using (var form = new CompareModelsForm())
            {
                form.ShowDialog();
            }
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            logTextBox.Text = "";
        }

        private async void ImportButtonGo_Click(object sender, EventArgs e)
        {
            ResetIgnoreWarnings();

            importButtonPreload.Enabled = false;
            importButtonPreload.Refresh();
            SetStatus("Importing...");

            if (_preloadM2 == IntPtr.Zero)
            {
                SetStatus("Error: Model not preloaded");
                PreloadTransition(false);
                return;
            }

            var fileName = Path.GetFileName(checkBoxReplaceM2.Checked ? textBoxReplaceM2.Text : textBoxInputM2Imp.Text);
            var inputM2ImpText = textBoxInputM2Imp.Text;
            var outputDirectory = ProfileManager.CurrentProfile.Settings.OutputDirectory;
            var mappingsDirectory = ProfileManager.CurrentProfile.Settings.MappingsDirectory;
            var hasOutputDirectory = outputDirectory.Length > 0;

            string ExportFileName = null;

            if (hasOutputDirectory)
            {
                SetStatus("Loading listfile...");
                var pathInfo = await Task.Run(() =>
                {
                    var fileStorage = Imports.FileStorage_Get(mappingsDirectory);
                    var info = Imports.FileStorage_GetFileInfoByPartialPath(fileStorage, fileName);
                    if (info == IntPtr.Zero)
                        return null;

                    return Imports.FileInfo_GetPath(info);
                });

                if (pathInfo == null)
                {
                    SetStatus("Failed to determine model relative path in storage");
                    PreloadTransition(false);
                    return;
                }

                ExportFileName = Path.Combine(outputDirectory, pathInfo);
            }
            else
            {
                var exportDir = Path.Combine(Path.GetDirectoryName(inputM2ImpText), "Export");
                ExportFileName = Path.Combine(exportDir, fileName);
            }

            var directory = Directory.GetParent(ExportFileName).FullName;
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var error = Imports.M2_SetSaveMappingsCallback(_preloadM2, () =>
            {
                var dialog = new SaveFileDialog {Filter = Filters.Txt};
                try
                {
                    dialog.Title = "Select mappings file to append entries to";
                    dialog.InitialDirectory = mappingsDirectory;
                    dialog.FileName = Path.GetFileNameWithoutExtension(inputM2ImpText) + ".txt";
                }
                catch
                {
                    // ignored
                }

                if (dialog.ShowDialog() == DialogResult.OK)
                    return dialog.FileName;

                return "";
            });
            if (error != M2LibError.OK)
            {
                SetStatus(Imports.GetErrorText(error));
                PreloadTransition(false);
                return;
            }

            SetStatus("Saving M2...");
            var saveResult = await Task.Run(() =>
            {
                // export M2
                return Imports.M2_Save(_preloadM2, ExportFileName, SaveMask.All);
            });

            if (saveResult != M2LibError.OK)
            {
                SetStatus(Imports.GetErrorText(saveResult));
                PreloadTransition(false);
                return;
            }

            SetStatus("Import done.");

            File.WriteAllBytes(Path.Combine(directory, "its_reduced_m2.custom"), []);
            PreloadTransition(false);
        }

        private void ProfilesComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (formInitialized && ProfileManager.CurrentProfile != null)
                SaveFormDataToProfile(ProfileManager.CurrentProfile);

            ProfileManager.CurrentProfile = profilesComboBox.SelectedItem as SettingsProfile;
            InitializeFormData();
        }

        private void LoadMappingsButton_Click(object sender, EventArgs e)
        {
            Imports.FileStorage_Clear();
        }

        private void CustomMenuItem_Click(object sender, EventArgs e)
        {
            OpenSettings();
            ExportButtonGo_Click(this, e);
        }

        private void ToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            OpenSettings();
            ImportButtonPreload_Click(this, e);
        }

        private void LoadProfilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = Filters.Profile;
                try
                {
                    dialog.FileName = "profiles.json";
                    dialog.InitialDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                }
                catch
                {
                    // ignored
                }

                if (dialog.ShowDialog() == DialogResult.OK && ProfileManager.Load(dialog.FileName, false))
                {
                    formInitialized = false;
                    InitializeProfiles();
                    InitializeFormData();
                    formInitialized = true;
                }
            }
        }

        private void tXIDRemoverToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ResetIgnoreWarnings();

            using (var form = new TXIDRemoverForm())
            {
                form.ShowDialog();
            }
        }

        private void getLatestMappingLinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            using (var form = new GetMappingsForm())
            {
                form.ShowDialog();
            }
        }

        private void remapReferencesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ResetIgnoreWarnings();

            using (var form = new RemapReferencesForm())
            {
                form.ShowDialog();
            }
        }
    }
}
