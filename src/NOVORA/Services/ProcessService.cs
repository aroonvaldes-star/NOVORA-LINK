using System.Diagnostics;
using System.IO;

namespace NOVORA.Services;

public sealed class ProcessService
{
    public Process Start(string executable, IEnumerable<string>? arguments = null, string? workingDirectory = null)
    {
        if (!File.Exists(executable))
            throw new FileNotFoundException("No se encontró el ejecutable.", executable);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (arguments is not null)
        {
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"No fue posible iniciar: {Path.GetFileName(executable)}");
    }
}