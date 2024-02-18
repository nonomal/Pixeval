#region Copyright (c) Pixeval/Pixeval
// GPL v3 License
// 
// Pixeval/Pixeval
// Copyright (c) 2023 Pixeval/SettingsPageViewModel.cs
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Text;
using System.Globalization;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net.Http;
using Windows.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pixeval.AppManagement;
using Pixeval.Controls;
using Pixeval.Database.Managers;
using Pixeval.Download;
using Pixeval.Download.MacroParser;
using Pixeval.Download.Models;
using Pixeval.Misc;
using Pixeval.Options;
using Pixeval.Util;
using Pixeval.Util.ComponentModels;
using Pixeval.Util.IO;
using Pixeval.Util.UI;
using Pixeval.Utilities;
using WinUI3Utilities;
using WinUI3Utilities.Attributes;
using System.ComponentModel;
using System.Collections.ObjectModel;

namespace Pixeval.Pages.Misc;

[SettingsViewModel<AppSettings>(nameof(AppSetting))]
public partial class SettingsPageViewModel(FrameworkElement frameworkElement) : UiObservableObject(frameworkElement)
{
    public static IEnumerable<string> AvailableFonts { get; }

    public static ICollection<Token> AvailableIllustMacros { get; }

    public static IEnumerable<CultureInfo> AvailableCultures { get; }

    public static IEnumerable<LanguageModel> AvailableLanguages { get; } = [LanguageModel.DefaultLanguage, new("简体中文", "zh-cn")];

    public ObservableCollection<string> PixivApiNameResolver { get; set; } = [.. App.AppViewModel.AppSettings.PixivApiNameResolver];

    public ObservableCollection<string> PixivImageNameResolver { get; set; } = [.. App.AppViewModel.AppSettings.PixivImageNameResolver];

    public AppSettings AppSetting { get; set; } = App.AppViewModel.AppSettings with { };

    [ObservableProperty] private bool _checkingUpdate;

    [ObservableProperty] private bool _downloadingUpdate;

    [ObservableProperty] private double _downloadingUpdateProgress;

    [ObservableProperty] private string _updateMessage = "";

    public string UpdateInfo => AppInfo.AppVersion.UpdateState switch
    {
        UpdateState.MajorUpdate => SettingsPageResources.MajorUpdateAvailable.Format(AppInfo.AppVersion.NewestVersion),
        UpdateState.MinorUpdate => SettingsPageResources.MinorUpdateAvailable.Format(AppInfo.AppVersion.NewestVersion),
        UpdateState.PatchUpdate or UpdateState.SpecifierUpdate => SettingsPageResources.PatchUpdateAvailable.Format(AppInfo.AppVersion.NewestVersion),
        UpdateState.Insider => SettingsPageResources.IsInsider,
        UpdateState.UpToDate => SettingsPageResources.IsUpToDate,
        _ => SettingsPageResources.UnknownUpdateState
    };

    public InfoBarSeverity UpdateInfoSeverity => AppInfo.AppVersion.UpdateState switch
    {
        UpdateState.MajorUpdate or UpdateState.MinorUpdate => InfoBarSeverity.Warning,
        UpdateState.UpToDate => InfoBarSeverity.Success,
        _ => InfoBarSeverity.Informational
    };

    public async void CheckForUpdate()
    {
        if (CheckingUpdate)
            return;
        try
        {
            CheckingUpdate = true;
            UpdateMessage = SettingsPageResources.CheckingForUpdate;
            using var client = new HttpClient();
            var appReleaseModel = await AppInfo.AppVersion.CheckForUpdateAsync(client);
            OnPropertyChanged(nameof(UpdateInfo));
            OnPropertyChanged(nameof(UpdateInfoSeverity));
            OnPropertyChanged(nameof(LastCheckedUpdate));
            if (AppInfo.AppVersion.UpdateAvailable)
            {
                DownloadingUpdate = true;
                UpdateMessage = SettingsPageResources.DownloadingUpdate;
                var result = await client.DownloadStreamAsync(appReleaseModel!.ReleaseUri, new Progress<double>(progress => DownloadingUpdateProgress = progress));
                // ReSharper disable once DisposeOnUsingVariable
                client.Dispose();

                DownloadingUpdate = false;

                if (result is Result<Stream>.Success { Value: var value })
                {
                    UpdateMessage = SettingsPageResources.Unzipping;
                    using var archive = new ZipArchive(value, ZipArchiveMode.Read);
                    var destUrl = null as string;
                    foreach (var entry in archive.Entries)
                    {
                        var path = AppKnownFolders.Local.Self.Path + entry.FullName.Replace('/', '\\');
                        if (entry.FullName.EndsWith('/'))
                            continue;

                        if (entry.FullName.EndsWith("Install.ps1"))
                            destUrl = path;
                        IoHelper.CreateParentDirectories(path);
                        await using var stream = entry.Open();
                        await using var fileStream = File.OpenWrite(path);
                        await stream.CopyToAsync(fileStream);
                    }

                    if (destUrl is not null
                        && await FrameworkElement.CreateOkCancelAsync(SettingsPageResources.UpdateApp,
                            SettingsPageResources.DownloadedAndWaitingToInstall) is ContentDialogResult.Primary)
                    {
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo("powershell", $"-ExecutionPolicy Unrestricted -noexit \"&'{destUrl}'\"")
                            {
                                UseShellExecute = false,
                                Verb = "runas"
                            }
                        };
                        _ = process.Start();
                        await process.WaitForExitAsync();
                    }
                }
            }
        }
        finally
        {
            CheckingUpdate = false;
        }
    }

    static SettingsPageViewModel()
    {
        AvailableCultures = [CultureInfo.GetCultureInfo("zh-cn")];
        using var collection = new InstalledFontCollection();
        AvailableFonts = collection.Families.Select(t => t.Name);
        using var scope = App.AppViewModel.AppServicesScope;
        var factory = scope.ServiceProvider.GetRequiredService<IDownloadTaskFactory<IllustrationItemViewModel, IllustrationDownloadTask>>();
        AvailableIllustMacros = factory.PathParser.MacroProvider.AvailableMacros
            .Select(m => $"@{{{(m is IMacro<IllustrationItemViewModel>.IPredicate ? $"{m.Name}=" : m.Name)}}}")
            .Select(s => new Token(s, false, false))
            .ToList();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        switch (e.PropertyName)
        {
            case nameof(DisableDomainFronting):
                App.AppViewModel.MakoClient.Configuration.Bypass = !DisableDomainFronting;
                break;
            case nameof(MirrorHost):
                App.AppViewModel.MakoClient.Configuration.MirrorHost = MirrorHost;
                break;
            case nameof(MaxDownloadTaskConcurrencyLevel):
                App.AppViewModel.DownloadManager.ConcurrencyDegree = MaxDownloadTaskConcurrencyLevel;
                break;
            case nameof(FiltrateRestrictedContent):
                // TODO: R18 Settings
                break;
        }
    }

    public LanguageModel AppLanguage
    {
        get => AvailableLanguages.FirstOrDefault(t => t.Name == ApplicationLanguages.PrimaryLanguageOverride) ?? LanguageModel.DefaultLanguage;
        set
        {
            if (ApplicationLanguages.PrimaryLanguageOverride == value.Name)
                return;
            ApplicationLanguages.PrimaryLanguageOverride = value.Name;
            OnPropertyChanged();
        }
    }

    public void ResetDefault()
    {
        AppSetting = new() { LastCheckedUpdate = AppSetting.LastCheckedUpdate };
        PixivApiNameResolver = [.. App.AppViewModel.AppSettings.PixivApiNameResolver];
        PixivImageNameResolver = [.. App.AppViewModel.AppSettings.PixivImageNameResolver];
        // see OnPropertyChanged
        OnPropertyChanged(nameof(DisableDomainFronting));
        OnPropertyChanged(nameof(MirrorHost));
        OnPropertyChanged(nameof(MaxDownloadTaskConcurrencyLevel));
        AppLanguage = LanguageModel.DefaultLanguage;
    }

    public DateTimeOffset GetMinSearchEndDate(DateTimeOffset startDate)
    {
        return startDate.AddDays(1);
    }

    public string GetLastUpdateCheckDisplayString(DateTimeOffset lastChecked)
    {
        return SettingsPageResources.LastCheckedPrefix + lastChecked.ToString(CultureInfo.CurrentUICulture);
    }

    public void ClearData<T, TModel>(ClearDataKind kind, IPersistentManager<T, TModel> manager) where T : new()
    {
        manager.Clear();
        FrameworkElement.ShowTeachingTipAndHide(kind.GetLocalizedResourceContent()!);
    }

    public void SaveCollections()
    {
        AppSetting.PixivApiNameResolver = [.. PixivApiNameResolver];
        AppSetting.PixivImageNameResolver = [.. PixivImageNameResolver];
    }
}
