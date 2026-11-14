package com.vrkiosk;

import android.app.Activity;
import android.content.ContentResolver;
import android.content.ContentUris;
import android.content.Context;
import android.content.Intent;
import android.database.Cursor;
import android.graphics.Bitmap;
import android.media.AudioManager;
import android.net.Uri;
import android.os.Build;
import android.provider.MediaStore;
import android.provider.Settings;
import android.util.Size;

import com.unity3d.player.UnityPlayer;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Accès à la galerie vidéo Android (MediaStore), miniatures, volume système et réglages de l'app.
 */
public final class GalleryBridge {

    private static final ExecutorService THUMB_EXECUTOR = Executors.newSingleThreadExecutor();

    private GalleryBridge() {}

    /**
     * Liste les vidéos de la galerie, les plus récentes d'abord.
     * Retourne {"items":[{id,name,path,duration,width,height,size}, ...]}
     */
    public static String queryVideos(Context context) {
        JSONArray items = new JSONArray();
        String[] projection = {"_id", "_display_name", "_data", "duration", "width", "height", "_size"};

        try (Cursor cursor = context.getContentResolver().query(
                MediaStore.Video.Media.EXTERNAL_CONTENT_URI, projection, null, null, "date_added DESC")) {
            if (cursor != null) {
                while (cursor.moveToNext()) {
                    String path = cursor.getString(2);
                    if (path == null || !new File(path).exists()) {
                        continue;
                    }
                    JSONObject item = new JSONObject();
                    item.put("id", cursor.getLong(0));
                    item.put("name", cursor.getString(1) != null ? cursor.getString(1) : new File(path).getName());
                    item.put("path", path);
                    item.put("duration", cursor.getLong(3));
                    item.put("width", cursor.getInt(4));
                    item.put("height", cursor.getInt(5));
                    item.put("size", cursor.getLong(6));
                    items.put(item);
                }
            }
        } catch (Exception e) {
            android.util.Log.w("VRKiosk", "queryVideos: " + e);
        }

        try {
            JSONObject root = new JSONObject();
            root.put("items", items);
            return root.toString();
        } catch (Exception e) {
            return "{\"items\":[]}";
        }
    }

    /**
     * Génère (en arrière-plan) la miniature d'une vidéo dans outPath (JPEG)
     * puis appelle UnitySendMessage(go, method, "id|chemin") ("id|" en cas d'échec).
     */
    public static void requestThumbnail(final Context context, final long id, final String outPath,
                                        final String gameObject, final String method) {
        THUMB_EXECUTOR.execute(new Runnable() {
            @Override
            @SuppressWarnings("deprecation")
            public void run() {
                String result = "";
                try {
                    File out = new File(outPath);
                    if (out.exists() && out.length() > 0) {
                        result = outPath;
                    } else {
                        ContentResolver resolver = context.getContentResolver();
                        Bitmap bitmap;
                        if (Build.VERSION.SDK_INT >= 29) {
                            Uri uri = ContentUris.withAppendedId(MediaStore.Video.Media.EXTERNAL_CONTENT_URI, id);
                            bitmap = resolver.loadThumbnail(uri, new Size(384, 216), null);
                        } else {
                            bitmap = MediaStore.Video.Thumbnails.getThumbnail(
                                    resolver, id, MediaStore.Video.Thumbnails.MINI_KIND, null);
                        }
                        if (bitmap != null) {
                            File parent = out.getParentFile();
                            if (parent != null) parent.mkdirs();
                            try (FileOutputStream fos = new FileOutputStream(out)) {
                                bitmap.compress(Bitmap.CompressFormat.JPEG, 80, fos);
                            }
                            result = outPath;
                        }
                    }
                } catch (Exception e) {
                    android.util.Log.w("VRKiosk", "thumbnail " + id + ": " + e);
                }
                UnityPlayer.UnitySendMessage(gameObject, method, id + "|" + result);
            }
        });
    }

    /** Monte (direction > 0) ou baisse le volume média du téléphone. Retourne le volume en %. */
    public static int adjustVolume(Context context, int direction) {
        AudioManager am = (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);
        if (am == null) return -1;
        am.adjustStreamVolume(AudioManager.STREAM_MUSIC,
                direction > 0 ? AudioManager.ADJUST_RAISE : AudioManager.ADJUST_LOWER, 0);
        return getVolumePercent(context);
    }

    public static int getVolumePercent(Context context) {
        AudioManager am = (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);
        if (am == null) return -1;
        int max = am.getStreamMaxVolume(AudioManager.STREAM_MUSIC);
        return max > 0 ? Math.round(am.getStreamVolume(AudioManager.STREAM_MUSIC) * 100f / max) : 0;
    }

    /** Ouvre la page "Infos de l'application" (pour accorder une permission refusée définitivement). */
    public static void openAppSettings(final Activity activity) {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                Intent intent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                        Uri.fromParts("package", activity.getPackageName(), null));
                intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                activity.startActivity(intent);
            }
        });
    }

    /** Désépingle l'application si elle a été épinglée (Lock Task). */
    public static void stopLockTask(final Activity activity) {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    activity.stopLockTask();
                } catch (Exception ignored) {
                }
            }
        });
    }
}
