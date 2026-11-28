// ClipProvider.java
// Remake-only: the screen recorder's clips on Android (docs/notes/platforms.md "Screen recorder"; UI.ClipsView): shares a
// clip with the system's video player or the share sheet (content://<package>.gof2clips/<name>, read only, only files
// directly in the Clips folder of the external or internal files, where ScreenRecorder records), and copies one into the
// gallery (MediaStore Movies/Galaxy on Fire 2, Android 10 and later). Registered (not exported, URI grants only) by the
// Editor's AndroidUpdateInstallerManifest, like UpdateApkProvider.

package com.joppietoppie.gof2remake;

import android.app.Activity;
import android.content.ContentProvider;
import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Context;
import android.content.Intent;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.Build;
import android.os.ParcelFileDescriptor;
import android.provider.MediaStore;
import android.provider.OpenableColumns;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileNotFoundException;
import java.io.InputStream;
import java.io.OutputStream;

public class ClipProvider extends ContentProvider {
    public static final String AUTHORITY_SUFFIX = ".gof2clips";

    /** The clip 'name' (no folders) in the external or internal Clips folder; null when there is none. */
    static File find(Context context, String name) {
        if (name == null || name.isEmpty() || name.contains("/") || name.contains("\\") || name.startsWith(".")) return null;
        File[] roots = { context.getExternalFilesDir(null), context.getFilesDir() };
        for (File root : roots) {
            if (root == null) continue;
            File f = new File(new File(root, "Clips"), name);
            if (f.isFile()) return f;
        }
        return null;
    }

    static Uri uriOf(Context context, String name) {
        return new Uri.Builder().scheme(ContentResolver.SCHEME_CONTENT)
            .authority(context.getPackageName() + AUTHORITY_SUFFIX).appendPath(name).build();
    }

    /** Opens the clip in the system's video player; false when nothing can play it. */
    public static boolean play(Activity activity, String name) {
        if (find(activity, name) == null) return false;
        Intent view = new Intent(Intent.ACTION_VIEW);
        view.setDataAndType(uriOf(activity, name), "video/mp4");
        view.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
        try { activity.startActivity(view); return true; } catch (Exception e) { return false; }
    }

    /** The share sheet with the clip; false when it couldn't open. */
    public static boolean share(Activity activity, String name) {
        if (find(activity, name) == null) return false;
        Intent send = new Intent(Intent.ACTION_SEND);
        send.setType("video/mp4");
        send.putExtra(Intent.EXTRA_STREAM, uriOf(activity, name));
        send.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        try {
            Intent chooser = Intent.createChooser(send, null);
            chooser.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            activity.startActivity(chooser);
            return true;
        } catch (Exception e) { return false; }
    }

    /** A copy in the gallery (Movies/Galaxy on Fire 2); "" or what went wrong. */
    public static String saveToGallery(Activity activity, String name) {
        File f = find(activity, name);
        if (f == null) return "no clip " + name;
        if (Build.VERSION.SDK_INT < 29) return "Android 10 or later";
        ContentResolver resolver = activity.getContentResolver();
        ContentValues values = new ContentValues();
        values.put(MediaStore.Video.Media.DISPLAY_NAME, name);
        values.put(MediaStore.Video.Media.MIME_TYPE, "video/mp4");
        values.put(MediaStore.Video.Media.RELATIVE_PATH, "Movies/Galaxy on Fire 2");
        values.put(MediaStore.Video.Media.IS_PENDING, 1);
        Uri item = resolver.insert(MediaStore.Video.Media.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY), values);
        if (item == null) return "the gallery refused it";
        try (InputStream in = new FileInputStream(f); OutputStream out = resolver.openOutputStream(item)) {
            byte[] buffer = new byte[1 << 16];
            int n;
            while ((n = in.read(buffer)) > 0) out.write(buffer, 0, n);
        } catch (Exception e) {
            resolver.delete(item, null, null);
            return e.toString();
        }
        values.clear();
        values.put(MediaStore.Video.Media.IS_PENDING, 0);
        resolver.update(item, values, null, null);
        return "";
    }

    File fileFor(Uri uri) { return find(getContext(), uri.getLastPathSegment()); }

    @Override public boolean onCreate() { return true; }

    @Override
    public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        File f = fileFor(uri);
        if (f == null) throw new FileNotFoundException(uri.toString());
        return ParcelFileDescriptor.open(f, ParcelFileDescriptor.MODE_READ_ONLY);
    }

    @Override public String getType(Uri uri) { return "video/mp4"; }

    @Override
    public Cursor query(Uri uri, String[] projection, String selection, String[] args, String sort) {
        File f = fileFor(uri);
        if (f == null) return null;
        MatrixCursor cursor = new MatrixCursor(new String[] { OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE });
        cursor.addRow(new Object[] { f.getName(), f.length() });
        return cursor;
    }

    @Override public Uri insert(Uri uri, ContentValues values) { throw new UnsupportedOperationException(); }
    @Override public int delete(Uri uri, String selection, String[] args) { throw new UnsupportedOperationException(); }
    @Override public int update(Uri uri, ContentValues values, String selection, String[] args) { throw new UnsupportedOperationException(); }
}
