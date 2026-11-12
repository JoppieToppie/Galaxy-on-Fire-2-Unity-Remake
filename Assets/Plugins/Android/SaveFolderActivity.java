// SaveFolderActivity.java
// Remake-only: the save folder option on Android (Data.SaveFolder). Scoped storage keeps every app out of the others'
// Android/data folders, so a folder that file managers and sync tools (Syncthing) can reach has to come from the Storage
// Access Framework: this see-through activity opens the system's folder picker (ACTION_OPEN_DOCUMENT_TREE), keeps the
// read / write grant across restarts (takePersistableUriPermission) and finishes; the game polls the static fields
// (state 0 waiting, 1 picked (resultUri / resultName), 2 cancelled, 3 failed (error)). The static helpers below read and
// write the folder's files through the ContentResolver (the tree's documents are no plain paths), as UTF-8 text: the
// saves are JSON. No permission in the manifest is needed. Registered in the unityLibrary manifest by the Editor's
// AndroidModImportActivity.

package com.joppietoppie.gof2remake;

import android.app.Activity;
import android.content.ContentResolver;
import android.content.Context;
import android.content.Intent;
import android.content.UriPermission;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.DocumentsContract;

import java.io.BufferedReader;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

public class SaveFolderActivity extends Activity {
    static final int REQUEST = 0x6f2e;

    public static volatile int state;
    public static volatile String resultUri, resultName, error;

    /** Opens the folder picker. */
    public static void start(Activity from) {
        state = 0;
        resultUri = null;
        resultName = null;
        error = null;
        from.startActivity(new Intent(from, SaveFolderActivity.class));
    }

    @Override
    protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        if (saved != null) return;   // recreated (a turned screen): the picker's result still comes here
        Intent pick = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
        pick.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION
                    | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION | Intent.FLAG_GRANT_PREFIX_URI_PERMISSION);
        try {
            startActivityForResult(pick, REQUEST);
        } catch (Exception e) {
            fail("no folder picker on this device (" + e.getMessage() + ")");
        }
    }

    @Override
    protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request != REQUEST) return;
        Uri tree = result == RESULT_OK && data != null ? data.getData() : null;
        if (tree == null) {
            state = 2;
            finish();
            return;
        }
        try {
            getContentResolver().takePersistableUriPermission(tree,
                    Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
            resultName = displayName(this, tree.toString());
            resultUri = tree.toString();
            state = 1;
        } catch (Exception e) {
            fail(e.getMessage());
            return;
        }
        finish();
    }

    void fail(String message) {
        error = message != null ? message : "unknown error";
        state = 3;
        finish();
    }

    // ---- the folder's files --------------------------------------------------------------------------------------

    static Uri children(Uri tree) {
        return DocumentsContract.buildChildDocumentsUriUsingTree(tree, DocumentsContract.getTreeDocumentId(tree));
    }

    /** A readable name for the folder: "Documents/GoF2" for the shared storage, else its display name. */
    public static String displayName(Context context, String treeUri) {
        Uri tree = Uri.parse(treeUri);
        String id = DocumentsContract.getTreeDocumentId(tree);
        if ("com.android.externalstorage.documents".equals(tree.getAuthority()) && id.indexOf(':') >= 0) {
            String volume = id.substring(0, id.indexOf(':')), path = id.substring(id.indexOf(':') + 1);
            String where = "primary".equals(volume) ? "" : volume + ": ";
            return where + (path.length() > 0 ? path : "/");
        }
        Uri doc = DocumentsContract.buildDocumentUriUsingTree(tree, id);
        try (Cursor c = context.getContentResolver().query(doc, new String[] { DocumentsContract.Document.COLUMN_DISPLAY_NAME }, null, null, null)) {
            if (c != null && c.moveToFirst() && !c.isNull(0)) return c.getString(0);
        } catch (Exception ignored) {
        }
        return id;
    }

    /** Whether the game still holds its grant on the folder (a deleted folder or a revoked grant loses it). */
    public static boolean granted(Context context, String treeUri) {
        for (UriPermission p : context.getContentResolver().getPersistedUriPermissions())
            if (p.getUri().toString().equals(treeUri) && p.isReadPermission() && p.isWritePermission()) return true;
        return false;
    }

    /** Lets the grant go (another folder or the default chosen). */
    public static void release(Context context, String treeUri) {
        try {
            context.getContentResolver().releasePersistableUriPermission(Uri.parse(treeUri),
                    Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
        } catch (Exception ignored) {
        }
    }

    /** The folder's files, one per line: name TAB size TAB last modified (ms). Throws when the folder can't be read. */
    public static String list(Context context, String treeUri) throws Exception {
        Uri tree = Uri.parse(treeUri);
        StringBuilder sb = new StringBuilder();
        try (Cursor c = context.getContentResolver().query(children(tree), new String[] {
                DocumentsContract.Document.COLUMN_DISPLAY_NAME, DocumentsContract.Document.COLUMN_SIZE,
                DocumentsContract.Document.COLUMN_LAST_MODIFIED, DocumentsContract.Document.COLUMN_MIME_TYPE }, null, null, null)) {
            if (c == null) throw new Exception("the folder can't be read");
            while (c.moveToNext()) {
                if (DocumentsContract.Document.MIME_TYPE_DIR.equals(c.getString(3))) continue;
                sb.append(c.getString(0)).append('\t').append(c.isNull(1) ? -1 : c.getLong(1)).append('\t')
                  .append(c.isNull(2) ? 0 : c.getLong(2)).append('\n');
            }
        }
        return sb.toString();
    }

    static Uri find(ContentResolver resolver, Uri tree, String name) {
        try (Cursor c = resolver.query(children(tree), new String[] {
                DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME }, null, null, null)) {
            if (c == null) return null;
            while (c.moveToNext())
                if (name.equals(c.getString(1))) return DocumentsContract.buildDocumentUriUsingTree(tree, c.getString(0));
        }
        return null;
    }

    /** A file's text, null when it isn't there. Throws when it can't be read. */
    public static String read(Context context, String treeUri, String name) throws Exception {
        ContentResolver resolver = context.getContentResolver();
        Uri doc = find(resolver, Uri.parse(treeUri), name);
        if (doc == null) return null;
        try (InputStream in = resolver.openInputStream(doc)) {
            if (in == null) throw new Exception(name + " can't be read");
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buffer = new byte[1 << 16];
            int n;
            while ((n = in.read(buffer)) > 0) out.write(buffer, 0, n);
            return new String(out.toByteArray(), StandardCharsets.UTF_8);
        }
    }

    /** Writes a file (made when it isn't there, else replaced). Throws when it can't be written. */
    public static void write(Context context, String treeUri, String name, String text) throws Exception {
        ContentResolver resolver = context.getContentResolver();
        Uri tree = Uri.parse(treeUri);
        Uri doc = find(resolver, tree, name);
        if (doc == null) {
            Uri parent = DocumentsContract.buildDocumentUriUsingTree(tree, DocumentsContract.getTreeDocumentId(tree));
            doc = DocumentsContract.createDocument(resolver, parent, "application/octet-stream", name);
            if (doc == null) throw new Exception(name + " can't be made in the folder");
        }
        try (OutputStream out = resolver.openOutputStream(doc, "wt")) {
            if (out == null) throw new Exception(name + " can't be written");
            out.write(text.getBytes(StandardCharsets.UTF_8));
        }
    }

    /** Deletes a file; true when it is gone (or wasn't there). */
    public static boolean delete(Context context, String treeUri, String name) throws Exception {
        ContentResolver resolver = context.getContentResolver();
        Uri doc = find(resolver, Uri.parse(treeUri), name);
        return doc == null || DocumentsContract.deleteDocument(resolver, doc);
    }

    // ---- the mods folder (ModManager.PickedFolder): its Mods copied in, its ModProjects synced both ways -------------

    static final String[] CHILD_COLUMNS = { DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME,
            DocumentsContract.Document.COLUMN_SIZE, DocumentsContract.Document.COLUMN_LAST_MODIFIED, DocumentsContract.Document.COLUMN_MIME_TYPE };

    /** The id of the folder 'name' in folder 'parentId' (made when missing). */
    static String childDir(ContentResolver resolver, Uri tree, String parentId, String name) throws Exception {
        try (Cursor c = resolver.query(DocumentsContract.buildChildDocumentsUriUsingTree(tree, parentId), CHILD_COLUMNS, null, null, null)) {
            if (c == null) throw new Exception("the mods folder can't be read");
            while (c.moveToNext())
                if (name.equals(c.getString(1)) && DocumentsContract.Document.MIME_TYPE_DIR.equals(c.getString(4))) return c.getString(0);
        }
        Uri made = DocumentsContract.createDocument(resolver, DocumentsContract.buildDocumentUriUsingTree(tree, parentId),
                DocumentsContract.Document.MIME_TYPE_DIR, name);
        if (made == null) throw new Exception(name + " can't be made in the mods folder");
        return DocumentsContract.getDocumentId(made);
    }

    /** Copies the folder's subfolder 'sub' (made when missing) into 'local' (the app's own), skipping files whose size and
     *  time match, and deletes what the folder no longer has. Returns the number of files copied. Throws when it can't be read. */
    public static int mirror(Context context, String treeUri, String sub, String local) throws Exception {
        Uri tree = Uri.parse(treeUri);
        ContentResolver resolver = context.getContentResolver();
        return mirror(resolver, tree, childDir(resolver, tree, DocumentsContract.getTreeDocumentId(tree), sub), new File(local));
    }

    static final class Remote {
        String id, parentId;
        long size, time;
    }

    static void listRemote(ContentResolver resolver, Uri tree, String dirId, String prefix, Map<String, Remote> files, Map<String, String> dirs) throws Exception {
        try (Cursor c = resolver.query(DocumentsContract.buildChildDocumentsUriUsingTree(tree, dirId), CHILD_COLUMNS, null, null, null)) {
            if (c == null) throw new Exception("the mods folder can't be read");
            while (c.moveToNext()) {
                String name = c.getString(1);
                if (name == null || name.startsWith(".") || name.contains("/")) continue;
                if (DocumentsContract.Document.MIME_TYPE_DIR.equals(c.getString(4))) {
                    dirs.put(prefix + name, c.getString(0));
                    listRemote(resolver, tree, c.getString(0), prefix + name + "/", files, dirs);
                    continue;
                }
                Remote r = new Remote();
                r.id = c.getString(0);
                r.parentId = dirId;
                r.size = c.isNull(2) ? -1 : c.getLong(2);
                r.time = c.isNull(3) ? 0 : c.getLong(3);
                files.put(prefix + name, r);
            }
        }
    }

    static void listLocal(File dir, String prefix, Map<String, long[]> files) {
        File[] inside = dir.listFiles();
        if (inside == null) return;
        for (File f : inside) {
            if (f.getName().startsWith(".")) continue;   // the builder's .<id>.import while it imports
            if (f.isDirectory()) listLocal(f, prefix + f.getName() + "/", files);
            else files.put(prefix + f.getName(), new long[] { f.length(), f.lastModified() });
        }
    }

    static void copy(InputStream in, OutputStream out, String name) throws Exception {
        if (in == null || out == null) throw new Exception(name + " can't be copied");
        byte[] buffer = new byte[1 << 16];
        int n;
        while ((n = in.read(buffer)) > 0) out.write(buffer, 0, n);
    }

    /** The id of the folder at 'path' ("a/b") under the sync root, made when missing ('dirs' kept in step; "" = the root). */
    static String remoteDir(ContentResolver resolver, Uri tree, Map<String, String> dirs, String path) throws Exception {
        String id = dirs.get(path);
        if (id != null) return id;
        int slash = path.lastIndexOf('/');
        String parent = remoteDir(resolver, tree, dirs, slash < 0 ? "" : path.substring(0, slash));
        id = childDir(resolver, tree, parent, path.substring(slash + 1));
        dirs.put(path, id);
        return id;
    }

    /** Two-way: the folder's subfolder 'sub' and 'local', against what both had at the last sync ('stateFile': size and
     *  time per file on each side, for this tree only). Changed on one side = copied to the other; changed on both or no
     *  past = the newer wins; gone on one side and unchanged on the other = deleted there. Returns "in out deleted". */
    public static String sync(Context context, String treeUri, String sub, String local, String stateFile) throws Exception {
        ContentResolver resolver = context.getContentResolver();
        Uri tree = Uri.parse(treeUri);
        String rootId = childDir(resolver, tree, DocumentsContract.getTreeDocumentId(tree), sub);
        File localRoot = new File(local);
        localRoot.mkdirs();
        String header = treeUri + "\t" + sub;

        Map<String, long[]> state = new HashMap<>();   // path -> local size, time, remote size, time
        File stateF = new File(stateFile);
        if (stateF.isFile()) {
            try (BufferedReader r = new BufferedReader(new InputStreamReader(new FileInputStream(stateF), StandardCharsets.UTF_8))) {
                if (header.equals(r.readLine())) {
                    String line;
                    while ((line = r.readLine()) != null) {
                        String[] p = line.split("\t");
                        if (p.length == 5) state.put(p[0], new long[] { Long.parseLong(p[1]), Long.parseLong(p[2]), Long.parseLong(p[3]), Long.parseLong(p[4]) });
                    }
                }
            }
        }

        Map<String, Remote> remote = new HashMap<>();
        Map<String, String> dirs = new HashMap<>();
        dirs.put("", rootId);
        listRemote(resolver, tree, rootId, "", remote, dirs);
        Map<String, long[]> mine = new HashMap<>();
        listLocal(localRoot, "", mine);

        Set<String> all = new HashSet<>(remote.keySet());
        all.addAll(mine.keySet());
        int in = 0, out = 0, deleted = 0;
        for (String path : all) {
            Remote r = remote.get(path);
            long[] l = mine.get(path), s = state.get(path);
            boolean localChanged = l != null && (s == null || l[0] != s[0] || l[1] != s[1]);
            boolean remoteChanged = r != null && (s == null || r.size != s[2] || r.time != s[3]);
            File file = new File(localRoot, path);
            boolean toRemote;
            if (l != null && r != null) {
                if (!localChanged && !remoteChanged) continue;
                toRemote = localChanged && (!remoteChanged || l[1] >= r.time);
            } else if (l != null) {
                if (s != null && !localChanged) { file.delete(); deleted++; continue; }   // deleted in the folder
                toRemote = true;
            } else {
                if (s != null && !remoteChanged) {   // deleted here
                    DocumentsContract.deleteDocument(resolver, DocumentsContract.buildDocumentUriUsingTree(tree, r.id));
                    deleted++;
                    continue;
                }
                toRemote = false;
            }
            if (toRemote) {
                Uri doc;
                if (r != null) doc = DocumentsContract.buildDocumentUriUsingTree(tree, r.id);
                else {
                    int slash = path.lastIndexOf('/');
                    String parent = remoteDir(resolver, tree, dirs, slash < 0 ? "" : path.substring(0, slash));
                    doc = DocumentsContract.createDocument(resolver, DocumentsContract.buildDocumentUriUsingTree(tree, parent),
                            "application/octet-stream", path.substring(slash + 1));
                    if (doc == null) throw new Exception(path + " can't be made in the mods folder");
                }
                try (InputStream i = new FileInputStream(file); OutputStream o = resolver.openOutputStream(doc, "wt")) {
                    copy(i, o, path);
                }
                out++;
            } else {
                file.getParentFile().mkdirs();
                File part = new File(file.getParentFile(), "." + file.getName() + ".part");
                try (InputStream i = resolver.openInputStream(DocumentsContract.buildDocumentUriUsingTree(tree, r.id));
                     OutputStream o = new FileOutputStream(part)) {
                    copy(i, o, path);
                }
                if (file.isDirectory()) deleteAll(file);
                file.delete();
                if (!part.renameTo(file)) throw new Exception(path + " can't be copied");
                if (r.time > 0) file.setLastModified(r.time);
                in++;
            }
        }

        // Folders left empty (a project deleted on one side) go on both sides.
        removeEmpty(localRoot, true);
        List<String> deepestFirst = new ArrayList<>(dirs.keySet());
        Collections.sort(deepestFirst, (a, b) -> b.length() - a.length());
        for (String path : deepestFirst) {
            if (path.isEmpty()) continue;
            try (Cursor c = resolver.query(DocumentsContract.buildChildDocumentsUriUsingTree(tree, dirs.get(path)), CHILD_COLUMNS, null, null, null)) {
                if (c == null || c.getCount() > 0) continue;
            }
            DocumentsContract.deleteDocument(resolver, DocumentsContract.buildDocumentUriUsingTree(tree, dirs.get(path)));
        }

        // What both sides have now is the next sync's past.
        remote.clear();
        dirs.clear();
        listRemote(resolver, tree, rootId, "", remote, dirs);
        mine.clear();
        listLocal(localRoot, "", mine);
        StringBuilder sb = new StringBuilder(header).append('\n');
        for (Map.Entry<String, long[]> e : mine.entrySet()) {
            Remote r = remote.get(e.getKey());
            if (r == null) continue;
            sb.append(e.getKey()).append('\t').append(e.getValue()[0]).append('\t').append(e.getValue()[1]).append('\t')
              .append(r.size).append('\t').append(r.time).append('\n');
        }
        try (OutputStream o = new FileOutputStream(stateF)) {
            o.write(sb.toString().getBytes(StandardCharsets.UTF_8));
        }
        return in + " " + out + " " + deleted;
    }

    /** Deletes the empty folders under 'dir' (and 'dir' itself unless 'keep'); true when 'dir' is empty now. */
    static boolean removeEmpty(File dir, boolean keep) {
        File[] inside = dir.listFiles();
        boolean empty = true;
        if (inside != null)
            for (File f : inside) {
                if (f.isDirectory() && removeEmpty(f, false)) continue;
                empty = false;
            }
        if (empty && !keep) dir.delete();
        return empty;
    }

    static int mirror(ContentResolver resolver, Uri tree, String parentId, File dir) throws Exception {
        if (dir.isFile()) dir.delete();
        if (!dir.isDirectory() && !dir.mkdirs()) throw new Exception("can't make " + dir);
        Set<String> keep = new HashSet<>();
        int copied = 0;
        try (Cursor c = resolver.query(DocumentsContract.buildChildDocumentsUriUsingTree(tree, parentId), new String[] {
                DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME,
                DocumentsContract.Document.COLUMN_SIZE, DocumentsContract.Document.COLUMN_LAST_MODIFIED,
                DocumentsContract.Document.COLUMN_MIME_TYPE }, null, null, null)) {
            if (c == null) throw new Exception("the mods folder can't be read");
            while (c.moveToNext()) {
                String name = c.getString(1);
                if (name == null || name.startsWith(".") || name.contains("/")) continue;   // never outside 'dir'
                keep.add(name);
                File target = new File(dir, name);
                if (DocumentsContract.Document.MIME_TYPE_DIR.equals(c.getString(4))) {
                    copied += mirror(resolver, tree, c.getString(0), target);
                    continue;
                }
                long size = c.isNull(2) ? -1 : c.getLong(2), time = c.isNull(3) ? 0 : c.getLong(3);
                // within 2 s: some file systems keep whole seconds
                if (target.isFile() && target.length() == size && (time == 0 || Math.abs(target.lastModified() - time) < 2000)) continue;
                if (target.isDirectory()) deleteAll(target);
                File part = new File(dir, name + ".part");
                try (InputStream in = resolver.openInputStream(DocumentsContract.buildDocumentUriUsingTree(tree, c.getString(0)));
                     OutputStream out = new FileOutputStream(part)) {
                    if (in == null) throw new Exception(name + " can't be read");
                    byte[] buffer = new byte[1 << 16];
                    int n;
                    while ((n = in.read(buffer)) > 0) out.write(buffer, 0, n);
                }
                if (target.exists()) target.delete();
                if (!part.renameTo(target)) throw new Exception(name + " can't be copied");
                if (time > 0) target.setLastModified(time);
                copied++;
            }
        }
        File[] had = dir.listFiles();
        if (had != null) for (File f : had) if (!keep.contains(f.getName())) deleteAll(f);
        return copied;
    }

    static void deleteAll(File f) {
        File[] inside = f.listFiles();
        if (inside != null) for (File g : inside) deleteAll(g);
        f.delete();
    }
}
