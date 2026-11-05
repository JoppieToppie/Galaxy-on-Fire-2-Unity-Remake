// UpdateApkProvider.java
// Remake-only: shares the in-game updater's rebuilt APK with Android's package installer (UpdateInstaller). Read only, and
// only files directly in the update folder (<external or internal files>/_update/files/, where PatchInstall stages them;
// Unity's persistentDataPath is the external one when there is one). content://<package>.gof2update/<name>. Registered
// (not exported, URI grants only) by the Editor's AndroidUpdateInstallerManifest.

package com.joppietoppie.gof2remake;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;

import java.io.File;
import java.io.FileNotFoundException;

public class UpdateApkProvider extends ContentProvider {
    public static final String AUTHORITY_SUFFIX = ".gof2update";

    /** The staged file 'name' (no folders), in the external or internal update folder; null when there is none. */
    static File find(Context context, String name) {
        if (name == null || name.isEmpty() || name.contains("/") || name.contains("\\") || name.startsWith(".")) return null;
        File external = context.getExternalFilesDir(null);
        File[] roots = { external, context.getFilesDir() };
        for (File root : roots) {
            if (root == null) continue;
            File f = new File(new File(new File(root, "_update"), "files"), name);
            if (f.isFile()) return f;
        }
        return null;
    }

    File fileFor(Uri uri) {
        return find(getContext(), uri.getLastPathSegment());
    }

    @Override
    public boolean onCreate() {
        return true;
    }

    @Override
    public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        File f = fileFor(uri);
        if (f == null) throw new FileNotFoundException(uri.toString());
        return ParcelFileDescriptor.open(f, ParcelFileDescriptor.MODE_READ_ONLY);
    }

    @Override
    public String getType(Uri uri) {
        return "application/vnd.android.package-archive";
    }

    @Override
    public Cursor query(Uri uri, String[] projection, String selection, String[] args, String sort) {
        File f = fileFor(uri);
        if (f == null) return null;
        MatrixCursor cursor = new MatrixCursor(new String[] { OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE });
        cursor.addRow(new Object[] { f.getName(), f.length() });
        return cursor;
    }

    @Override
    public Uri insert(Uri uri, ContentValues values) {
        throw new UnsupportedOperationException();
    }

    @Override
    public int delete(Uri uri, String selection, String[] args) {
        throw new UnsupportedOperationException();
    }

    @Override
    public int update(Uri uri, ContentValues values, String selection, String[] args) {
        throw new UnsupportedOperationException();
    }
}
