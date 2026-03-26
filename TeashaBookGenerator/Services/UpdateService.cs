using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace TeashaBookGenerator.Services;

public class UpdateService
{
    private readonly UpdateManager _updateManager;

    public UpdateService()
    {
        _updateManager = new UpdateManager(
            new GithubSource("https://github.com/codeprada/teasha-book-generator", null, false));
    }

    public bool IsInstalled => _updateManager.IsInstalled;

    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            return await _updateManager.CheckForUpdatesAsync();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task DownloadUpdateAsync(UpdateInfo updateInfo, Action<int>? progress = null)
    {
        await _updateManager.DownloadUpdatesAsync(updateInfo, p => progress?.Invoke(p));
    }

    public void ApplyUpdateAndRestart(UpdateInfo updateInfo)
    {
        _updateManager.ApplyUpdatesAndRestart(updateInfo);
    }
}
