using Avalonia.Platform.Storage;
using Serilog;

namespace DirectorPrompt.Services;

public sealed class FilePickerService : IFilePickerService
{
    public async Task<string?> OpenAsync(string displayName, string pattern)
    {
        var storageProvider = App.GetActiveWindow()?.StorageProvider;

        if (storageProvider is null)
        {
            Log.Warning("无法打开文件选择器: 当前活动窗口没有存储提供程序");
            return null;
        }

        Log.Debug("打开文件选择器: 类型={DisplayName}, 模式={Pattern}", displayName, pattern);

        var files = await storageProvider.OpenFilePickerAsync
                    (
                        new FilePickerOpenOptions
                        {
                            AllowMultiple  = false,
                            FileTypeFilter = [CreateFileType(displayName, pattern)]
                        }
                    );

        var filePath = files.Count == 0 ?
                           null :
                           files[0].TryGetLocalPath();

        Log.Information("文件选择器已关闭: 已选择={Selected}, 路径={FilePath}", filePath is not null, filePath);
        return filePath;
    }

    public async Task<string?> SaveAsync(string displayName, string pattern, string suggestedFileName)
    {
        var storageProvider = App.GetActiveWindow()?.StorageProvider;

        if (storageProvider is null)
        {
            Log.Warning("无法打开保存文件对话框: 当前活动窗口没有存储提供程序");
            return null;
        }

        Log.Debug("打开保存文件对话框: 类型={DisplayName}, 模式={Pattern}, 建议文件名={SuggestedFileName}", displayName, pattern, suggestedFileName);

        var file = await storageProvider.SaveFilePickerAsync
                   (
                       new FilePickerSaveOptions
                       {
                           SuggestedFileName = suggestedFileName,
                           FileTypeChoices   = [CreateFileType(displayName, pattern)]
                       }
                   );

        var filePath = file?.TryGetLocalPath();

        Log.Information("保存文件对话框已关闭: 已选择={Selected}, 路径={FilePath}", filePath is not null, filePath);
        return filePath;
    }

    private static FilePickerFileType CreateFileType(string displayName, string pattern) =>
        new(displayName) { Patterns = [pattern] };
}
