using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace YouTubeService.Helpers;

sealed class ProcessResult
{
    public bool Completed { get; set; }
    public string Output { get; set; }
    public string Error { get; set; }
}

static class ProcessHelper
{
    public static async Task<ProcessResult> StartProcessAsync(string command, string arguments, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new ProcessResult { Completed = false, Error = "Process aborted by client" };
        }

        using var process = new Process();
        process.StartInfo.FileName = command;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.EnableRaisingEvents = true;

        var outputBuffer = new List<string>();
        var outputCloseEvent = new TaskCompletionSource<bool>();

        process.OutputDataReceived += (sender, args) =>
        {
            if (args.Data == null)
            {
                outputCloseEvent.TrySetResult(true);
            }
            else
            {
                outputBuffer.Add(args.Data);
            }
        };

        var errorBuffer = new List<string>();
        var errorCloseEvent = new TaskCompletionSource<bool>();

        process.ErrorDataReceived += (sender, args) =>
        {
            if (args.Data == null)
            {
                errorCloseEvent.TrySetResult(true);
            }
            else
            {
                errorBuffer.Add(args.Data);
            }
        };

        var processCloseEvent = new TaskCompletionSource<bool>();

        process.Exited += (sender, args) =>
        {
            processCloseEvent.TrySetResult(true);
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessResult { Completed = false, Error = "Process failed to start" };
            }
        }
        catch (Exception exception)
        {
            return new ProcessResult { Completed = false, Error = exception.Message };
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        Task processTask = Task.WhenAll(outputCloseEvent.Task, errorCloseEvent.Task, processCloseEvent.Task);

        Task cancellationTask = Task.Run(() =>
        {
            cancellationToken.WaitHandle.WaitOne();
        });

        if (await Task.WhenAny(processTask, cancellationTask) == processTask)
        {
            return new ProcessResult
            {
                Completed = true,
                Output = string.Join(' ', outputBuffer),
                Error = string.Join(' ', errorBuffer),
            };
        }
        else
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
            }

            return new ProcessResult { Completed = false, Error = "Process aborted by client" };
        }
    }
}
