// ModImportActivity.java
// Remake-only: the mod browser's "Import mod" on Android (Modding.ModImport). A see-through activity that opens the
// system's document picker (ACTION_OPEN_DOCUMENT, any file: zips come with several MIME types), copies the picked
// file into the folder the game named (the app's cache) on a worker thread and finishes. The game polls the static
// fields: state 0 waiting, 1 copied (resultPath / resultName), 2 cancelled, 3 failed (error). Registered in the
// unityLibrary manifest by the Editor's AndroidModImportActivity.
// startSave: the other way, the main menu's "Export saves" (MainMenuSaves): ACTION_CREATE_DOCUMENT, the game's file
// copied to where the player chose (Android/data, the Transfer folder, is out of other apps' reach); same fields.

package com.joppietoppie.gof2remake;

import android.app.Activity;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;

public class ModImportActivity extends Activity {
    static final int REQUEST = 0x6f2d;

    public static volatile int state;
    public static volatile String resultPath, resultName, error;
    static volatile String targetFolder, saveSource, saveName;

    /** Opens the picker; the copy goes into 'folder'. */
    public static void start(Activity from, String folder) {
        state = 0;
        resultPath = null;
        resultName = null;
        error = null;
        targetFolder = folder;
        saveSource = null;
        from.startActivity(new Intent(from, ModImportActivity.class));
    }

    /** Opens the "save as" picker; the file 'source' is copied to the picked place, offered as 'name'. */
    public static void startSave(Activity from, String source, String name) {
        state = 0;
        resultPath = null;
        resultName = null;
        error = null;
        saveSource = source;
        saveName = name;
        from.startActivity(new Intent(from, ModImportActivity.class));
    }

    @Override
    protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        if (saved != null) return;   // recreated (a turned screen): the picker's result still comes here
        Intent pick = new Intent(saveSource != null ? Intent.ACTION_CREATE_DOCUMENT : Intent.ACTION_OPEN_DOCUMENT);
        pick.addCategory(Intent.CATEGORY_OPENABLE);
        if (saveSource != null) {
            pick.setType("application/octet-stream");   // not application/json: some providers then add ".json"
            pick.putExtra(Intent.EXTRA_TITLE, saveName);
        } else pick.setType("*/*");
        try {
            startActivityForResult(pick, REQUEST);
        } catch (Exception e) {
            fail("no file picker on this device (" + e.getMessage() + ")");
        }
    }

    @Override
    protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request != REQUEST) return;
        final Uri uri = result == RESULT_OK && data != null ? data.getData() : null;
        if (uri == null) {
            state = 2;
            finish();
            return;
        }
        // Copied before finishing: the read permission on the picked document belongs to this activity.
        new Thread(new Runnable() {
            public void run() {
                if (saveSource != null) write(uri); else copy(uri);
                runOnUiThread(new Runnable() { public void run() { finish(); } });
            }
        }).start();
    }

    void copy(Uri uri) {
        String name = "mod.zip";
        try (Cursor c = getContentResolver().query(uri, new String[] { OpenableColumns.DISPLAY_NAME }, null, null, null)) {
            if (c != null && c.moveToFirst() && !c.isNull(0)) name = c.getString(0);
        } catch (Exception ignored) {
        }
        File folder = new File(targetFolder);
        folder.mkdirs();
        File out = new File(folder, "picked.tmp");
        try (InputStream in = getContentResolver().openInputStream(uri); OutputStream os = new FileOutputStream(out)) {
            if (in == null) throw new Exception("the file can't be read");
            byte[] buffer = new byte[1 << 16];
            int n;
            while ((n = in.read(buffer)) > 0) os.write(buffer, 0, n);
        } catch (Exception e) {
            out.delete();
            fail(e.getMessage());
            return;
        }
        resultName = name;
        resultPath = out.getAbsolutePath();
        state = 1;
    }

    void write(Uri uri) {
        try (InputStream in = new FileInputStream(saveSource); OutputStream os = getContentResolver().openOutputStream(uri, "wt")) {
            if (os == null) throw new Exception("the place can't be written");
            byte[] buffer = new byte[1 << 16];
            int n;
            while ((n = in.read(buffer)) > 0) os.write(buffer, 0, n);
        } catch (Exception e) {
            fail(e.getMessage());
            return;
        }
        String name = saveName;
        try (Cursor c = getContentResolver().query(uri, new String[] { OpenableColumns.DISPLAY_NAME }, null, null, null)) {
            if (c != null && c.moveToFirst() && !c.isNull(0)) name = c.getString(0);
        } catch (Exception ignored) {
        }
        resultName = name;
        resultPath = uri.toString();
        state = 1;
    }

    void fail(String message) {
        error = message != null ? message : "unknown error";
        state = 3;
        finish();
    }
}
