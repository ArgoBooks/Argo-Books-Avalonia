using System.ComponentModel;
using System.Reflection;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

// Swaps App.ErrorLogger, which is process-wide.
[Collection("ModalViewModels")]
public class CheckForUpdateModalViewModelTests
{
    private static readonly PropertyInfo ErrorLoggerProperty =
        typeof(App).GetProperty(nameof(App.ErrorLogger), BindingFlags.Public | BindingFlags.Static)!;

    // 225 is what Windows returns when antivirus blocks the file.
    private sealed class InstallerThatWontStart : IUpdateService
    {
        public UpdateState State => UpdateState.ReadyToInstall;
        public UpdateInfo? AvailableUpdate => null;
        public string? LastError => null;
        public string? InstallerPath => null;
        public Task<UpdateInfo?> CheckForUpdateAsync() => Task.FromResult<UpdateInfo?>(null);
        public Task DownloadUpdateAsync() => Task.CompletedTask;
        public void ApplyUpdateAndRestart() => throw new Win32Exception(225);
        public event EventHandler<UpdateState>? StateChanged { add { } remove { } }
        public event EventHandler<int>? DownloadProgressChanged { add { } remove { } }
        public event EventHandler<ApplyingUpdateEventArgs>? ApplyingUpdate { add { } remove { } }
    }

    private sealed class RecordingErrorLogger : IErrorLogger
    {
        public List<(Exception Exception, string? Context)> Errors { get; } = [];
        public void LogError(Exception exception, ErrorCategory category, string? context = null, string callerFile = "", int callerLine = 0, string callerMember = "") => Errors.Add((exception, context));
        public void LogError(string message, ErrorCategory category, string? context = null, string? code = null, string callerFile = "", int callerLine = 0, string callerMember = "") { }
        public void LogWarning(string message, string? context = null, ErrorCategory category = ErrorCategory.Unknown, string? code = null, string callerFile = "", int callerLine = 0, string callerMember = "") { }
        public void LogInfo(string message) { }
        public void LogDebug(string message) { }
        public IReadOnlyList<ErrorLogEntry> GetRecentErrors(int count = 50) => [];
        public event EventHandler<ErrorLogEntry>? ErrorLogged { add { } remove { } }
    }

    // Telemetry keeps an error's type and context but not its message, so the Windows code has to be in the context.
    [Fact]
    public async Task AnUpdateThatDoesNotStart_IsLoggedWithTheWindowsCode()
    {
        var logger = new RecordingErrorLogger();
        var prior = ErrorLoggerProperty.GetValue(null);
        ErrorLoggerProperty.SetValue(null, logger);
        try
        {
            var viewModel = new CheckForUpdateModalViewModel(new InstallerThatWontStart());

            await viewModel.ApplyUpdateCommand.ExecuteAsync(null);

            var (exception, context) = Assert.Single(logger.Errors);
            Assert.IsType<Win32Exception>(exception);
            Assert.Contains("225", context);
            Assert.True(viewModel.HasError);
        }
        finally
        {
            ErrorLoggerProperty.SetValue(null, prior);
        }
    }
}
