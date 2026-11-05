// UpdateInstaller.java
// Remake-only: the in-game updater's Android installer step (GamePatcher / AndroidUpdateInstaller in C#). The game
// rebuilds the new APK in its own data folder (<external or internal files>/_update/files/), then install() opens it in
// Android's package installer, shared through UpdateApkProvider (a content:// URI the installer may read once). Android
// asks the player to confirm the update; on Android 8+ only after the player allowed this app to install apps
// (canInstall / openInstallSettings). Needs REQUEST_INSTALL_PACKAGES and the provider in the manifest (the Editor's
// AndroidUpdateInstallerManifest).

package com.joppietoppie.gof2remake;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;

import java.io.File;

public class UpdateInstaller {
    public static boolean canInstall(Activity activity) {
        if (Build.VERSION.SDK_INT < 26) return true;
        return activity.getPackageManager().canRequestPackageInstalls();
    }

    public static boolean openInstallSettings(Activity activity) {
        if (Build.VERSION.SDK_INT < 26) return false;
        try {
            Intent intent = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + activity.getPackageName()));
            activity.startActivity(intent);
            return true;
        } catch (Exception e) {
            return false;
        }
    }

    /** Opens 'apkPath' (a file in the update folder) in the package installer; null, or what went wrong. */
    public static String install(Activity activity, String apkPath) {
        try {
            File apk = new File(apkPath);
            if (!apk.isFile()) return "no file " + apkPath;
            File staged = UpdateApkProvider.find(activity, apk.getName());
            if (staged == null || !staged.getCanonicalPath().equals(apk.getCanonicalPath()))
                return "not in the update folder: " + apkPath;
            Uri uri = Uri.parse("content://" + activity.getPackageName() + UpdateApkProvider.AUTHORITY_SUFFIX + "/" + apk.getName());
            Intent intent = new Intent(Intent.ACTION_VIEW);
            intent.setDataAndType(uri, "application/vnd.android.package-archive");
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
            activity.startActivity(intent);
            return null;
        } catch (Exception e) {
            return e.toString();
        }
    }
}
