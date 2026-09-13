using System;
using System.Collections.Generic;
using System.IO;
using Forms = System.Windows.Forms;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Detecta archivos y carpetas soltados directamente
/// sobre la superficie nativa de VisionEngine.
///
/// Este componente NO transfiere archivos.
/// Únicamente recibe el Drag & Drop de Windows y
/// publica las rutas válidas.
///
/// La transferencia se realizará posteriormente por
/// ExchangeVE hacia:
///
///     /sdcard/NOVORA
///
/// Esto evita bloquear video, audio, input o renderer.
/// </summary>
internal sealed class DropExchangeVE : IDisposable
{
    private readonly Forms.Control _targetVE;

    private bool _attachedVE;
    private bool _disposedVE;

    public DropExchangeVE(
        Forms.Control target)
    {
        ArgumentNullException.ThrowIfNull(
            target);

        _targetVE =
            target;
    }

    // ============================================================
    // EVENTS
    // ============================================================

    public event EventHandler<FilesDroppedEventArgsVE>?
        FilesDroppedVE;

    public event EventHandler?
        DragEnteredVE;

    public event EventHandler?
        DragEndedVE;

    // ============================================================
    // ATTACH
    // ============================================================

    public void AttachVE()
    {
        ThrowIfDisposedVE();

        if (_attachedVE)
        {
            return;
        }

        _targetVE.AllowDrop =
            true;

        _targetVE.DragEnter +=
            TargetVE_DragEnter;

        _targetVE.DragOver +=
            TargetVE_DragOver;

        _targetVE.DragLeave +=
            TargetVE_DragLeave;

        _targetVE.DragDrop +=
            TargetVE_DragDrop;

        _attachedVE =
            true;
    }

    // ============================================================
    // DETACH
    // ============================================================

    public void DetachVE()
    {
        if (!_attachedVE)
        {
            return;
        }

        _targetVE.DragEnter -=
            TargetVE_DragEnter;

        _targetVE.DragOver -=
            TargetVE_DragOver;

        _targetVE.DragLeave -=
            TargetVE_DragLeave;

        _targetVE.DragDrop -=
            TargetVE_DragDrop;

        _targetVE.AllowDrop =
            false;

        _attachedVE =
            false;
    }

    // ============================================================
    // DRAG ENTER
    // ============================================================

    private void TargetVE_DragEnter(
        object? sender,
        Forms.DragEventArgs e)
    {
        if (!HasSupportedDropVE(e.Data))
        {
            e.Effect =
                Forms.DragDropEffects.None;

            return;
        }

        e.Effect =
            Forms.DragDropEffects.Copy;

        DragEnteredVE?.Invoke(
            this,
            EventArgs.Empty);
    }

    // ============================================================
    // DRAG OVER
    // ============================================================

    private void TargetVE_DragOver(
        object? sender,
        Forms.DragEventArgs e)
    {
        e.Effect =
            HasSupportedDropVE(e.Data)
                ? Forms.DragDropEffects.Copy
                : Forms.DragDropEffects.None;
    }

    // ============================================================
    // DRAG LEAVE
    // ============================================================

    private void TargetVE_DragLeave(
        object? sender,
        EventArgs e)
    {
        DragEndedVE?.Invoke(
            this,
            EventArgs.Empty);
    }

    // ============================================================
    // DROP
    // ============================================================

    private void TargetVE_DragDrop(
        object? sender,
        Forms.DragEventArgs e)
    {
        try
        {
            if (!HasSupportedDropVE(e.Data))
            {
                e.Effect =
                    Forms.DragDropEffects.None;

                return;
            }

            object? data =
                e.Data?.GetData(
                    Forms.DataFormats.FileDrop);

            if (data is not string[] rawPaths)
            {
                e.Effect =
                    Forms.DragDropEffects.None;

                return;
            }

            if (rawPaths.Length == 0)
            {
                e.Effect =
                    Forms.DragDropEffects.None;

                return;
            }

            List<string> validPaths =
                new(
                    rawPaths.Length);

            HashSet<string> uniquePaths =
                new(
                    StringComparer.OrdinalIgnoreCase);

            foreach (string rawPath in rawPaths)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                string fullPath;

                try
                {
                    fullPath =
                        Path.GetFullPath(
                            rawPath);
                }
                catch
                {
                    continue;
                }

                if (
                    !File.Exists(fullPath) &&
                    !Directory.Exists(fullPath))
                {
                    continue;
                }

                if (!uniquePaths.Add(fullPath))
                {
                    continue;
                }

                validPaths.Add(
                    fullPath);
            }

            if (validPaths.Count == 0)
            {
                e.Effect =
                    Forms.DragDropEffects.None;

                return;
            }

            e.Effect =
                Forms.DragDropEffects.Copy;

            FilesDroppedVE?.Invoke(
                this,
                new FilesDroppedEventArgsVE(
                    validPaths.ToArray()));
        }
        finally
        {
            DragEndedVE?.Invoke(
                this,
                EventArgs.Empty);
        }
    }

    // ============================================================
    // VALIDATION
    // ============================================================

    private static bool HasSupportedDropVE(
        Forms.IDataObject? data)
    {
        if (data is null)
        {
            return false;
        }

        try
        {
            return
                data.GetDataPresent(
                    Forms.DataFormats.FileDrop);
        }
        catch
        {
            return false;
        }
    }

    // ============================================================
    // DISPOSE
    // ============================================================

    private void ThrowIfDisposedVE()
    {
        ObjectDisposedException.ThrowIf(
            _disposedVE,
            this);
    }

    public void Dispose()
    {
        if (_disposedVE)
        {
            return;
        }

        DetachVE();

        _disposedVE =
            true;
    }
}

/// <summary>
/// Información pública de los archivos y carpetas
/// soltados directamente sobre VisionEngine.
///
/// Debe ser public porque HostRendererVE expone
/// FilesDroppedVE como evento público.
/// </summary>
public sealed class FilesDroppedEventArgsVE : EventArgs
{
    private readonly string[] _pathsVE;
    private readonly string[] _filesVE;
    private readonly string[] _directoriesVE;

    public FilesDroppedEventArgsVE(
        IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(
            paths);

        List<string> normalizedPaths =
            new(
                paths.Count);

        List<string> files =
            new(
                paths.Count);

        List<string> directories =
            new(
                paths.Count);

        HashSet<string> uniquePaths =
            new(
                StringComparer.OrdinalIgnoreCase);

        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string fullPath;

            try
            {
                fullPath =
                    Path.GetFullPath(
                        path);
            }
            catch
            {
                continue;
            }

            if (!uniquePaths.Add(fullPath))
            {
                continue;
            }

            if (File.Exists(fullPath))
            {
                normalizedPaths.Add(
                    fullPath);

                files.Add(
                    fullPath);

                continue;
            }

            if (Directory.Exists(fullPath))
            {
                normalizedPaths.Add(
                    fullPath);

                directories.Add(
                    fullPath);
            }
        }

        _pathsVE =
            normalizedPaths.ToArray();

        _filesVE =
            files.ToArray();

        _directoriesVE =
            directories.ToArray();
    }

    public IReadOnlyList<string> PathsVE =>
        _pathsVE;

    public IReadOnlyList<string> FilesVE =>
        _filesVE;

    public IReadOnlyList<string> DirectoriesVE =>
        _directoriesVE;

    public bool HasFilesVE =>
        _filesVE.Length > 0;

    public bool HasDirectoriesVE =>
        _directoriesVE.Length > 0;

    public int CountVE =>
        _pathsVE.Length;
}