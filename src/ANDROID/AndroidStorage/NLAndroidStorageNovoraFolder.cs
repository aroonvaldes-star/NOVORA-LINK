using Android.Content;
using Android.Net;
using Android.Provider;
using Uri = Android.Net.Uri;

namespace NOVORA.AndroidStorage;

/// <summary>Persistent SAF grant for the exact public root used by ADB: /sdcard/NOVORA.</summary>
internal static class NLAndroidStorageNovoraFolder
{
    private const string Preferences = "novora_files";
    private const string Key = "novora_public_tree";

    internal static bool TryGet(Context context, out Uri tree)
    {
        string? stored = context.GetSharedPreferences(Preferences, FileCreationMode.Private)
            ?.GetString(Key, null);
        if (string.IsNullOrWhiteSpace(stored))
        {
            tree = null!;
            return false;
        }

        tree = Uri.Parse(stored)!;
        Uri selected = tree;
        return IsNovoraRoot(selected) && context.ContentResolver!.PersistedUriPermissions.Any(permission =>
            permission.Uri == selected &&
            permission.IsReadPermission &&
            permission.IsWritePermission);
    }

    internal static void Save(Context context, Uri tree, ActivityFlags flags)
    {
        if (!IsNovoraRoot(tree))
            throw new InvalidOperationException("Selecciona NOVORA directamente dentro del almacenamiento interno, no otra carpeta.");

        context.ContentResolver!.TakePersistableUriPermission(tree,
            flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission));
        context.GetSharedPreferences(Preferences, FileCreationMode.Private)!
            .Edit()!.PutString(Key, tree.ToString())!.Apply();
    }

    internal static Uri GetOrCreateDirectory(ContentResolver resolver, Uri tree, IEnumerable<string> segments)
    {
        string rootId = DocumentsContract.GetTreeDocumentId(tree)
            ?? throw new IOException("La carpeta NOVORA no es válida.");
        Uri current = DocumentsContract.BuildDocumentUriUsingTree(tree, rootId)
            ?? throw new IOException("No se pudo abrir la carpeta NOVORA.");

        foreach (string segment in segments)
        {
            string safe = NOVORA.Control.NLControlFileStorage.SafeName(segment);
            Uri children = DocumentsContract.BuildChildDocumentsUriUsingTree(tree,
                DocumentsContract.GetDocumentId(current))
                ?? throw new IOException("No se pudo abrir la carpeta NOVORA.");
            Uri? child = null;
            using (var cursor = resolver.Query(children, new[]
            {
                DocumentsContract.Document.ColumnDocumentId,
                DocumentsContract.Document.ColumnDisplayName,
                DocumentsContract.Document.ColumnMimeType
            }, null, null, null))
            {
                while (cursor?.MoveToNext() == true)
                {
                    if (cursor.GetString(1) == safe &&
                        cursor.GetString(2) == DocumentsContract.Document.MimeTypeDir)
                    {
                        child = DocumentsContract.BuildDocumentUriUsingTree(tree, cursor.GetString(0));
                        break;
                    }
                }
            }

            current = child ?? DocumentsContract.CreateDocument(resolver, current,
                DocumentsContract.Document.MimeTypeDir, safe)
                ?? throw new IOException("No se pudo crear la carpeta NOVORA.");
        }

        return current;
    }

    private static bool IsNovoraRoot(Uri tree) =>
        tree.Authority == "com.android.externalstorage.documents" &&
        DocumentsContract.GetTreeDocumentId(tree) == "primary:NOVORA";
}
