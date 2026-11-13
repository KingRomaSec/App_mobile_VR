package com.vrkiosk;

import android.app.Activity;
import android.content.Intent;
import android.media.session.MediaSession;
import android.media.session.PlaybackState;
import android.os.Handler;
import android.os.Looper;
import android.view.KeyEvent;

import com.unity3d.player.UnityPlayer;

/**
 * Capte le bouton Play/Pause d'un casque audio (USB-C / jack 3.5mm / Bluetooth)
 * via une MediaSession et le transmet à Unity : UnitySendMessage(go, method, "down"|"up").
 */
public final class HeadsetButtonBridge {

    private static MediaSession session;

    private HeadsetButtonBridge() {}

    public static void start(final Activity activity, final String gameObjectName, final String methodName) {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (session != null) {
                    return;
                }

                session = new MediaSession(activity, "VRKioskHeadsetButton");
                session.setCallback(new MediaSession.Callback() {
                    @Override
                    @SuppressWarnings("deprecation")
                    public boolean onMediaButtonEvent(Intent intent) {
                        KeyEvent event = intent.getParcelableExtra(Intent.EXTRA_KEY_EVENT);
                        if (event == null) {
                            return false;
                        }

                        int code = event.getKeyCode();
                        boolean isPlayPause = code == KeyEvent.KEYCODE_HEADSETHOOK
                                || code == KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE
                                || code == KeyEvent.KEYCODE_MEDIA_PLAY
                                || code == KeyEvent.KEYCODE_MEDIA_PAUSE;
                        if (!isPlayPause) {
                            return false;
                        }

                        // Ignore les répétitions automatiques pendant un appui long
                        if (event.getRepeatCount() == 0) {
                            String state = event.getAction() == KeyEvent.ACTION_DOWN ? "down" : "up";
                            UnityPlayer.UnitySendMessage(gameObjectName, methodName, state);
                        }
                        return true;
                    }
                }, new Handler(Looper.getMainLooper()));

                session.setPlaybackState(new PlaybackState.Builder()
                        .setActions(PlaybackState.ACTION_PLAY_PAUSE
                                | PlaybackState.ACTION_PLAY
                                | PlaybackState.ACTION_PAUSE)
                        .setState(PlaybackState.STATE_PLAYING, 0, 1.0f)
                        .build());
                session.setActive(true);
            }
        });
    }

    public static void stop() {
        if (session != null) {
            session.setActive(false);
            session.release();
            session = null;
        }
    }
}
