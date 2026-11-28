// ClipEncoder.java
// Remake-only: the built-in screen recorder's encoder on Android (docs/notes/platforms.md "Screen recorder"; UI.ScreenRecorder
// drives it, Windows has Native/Recorder): an MP4 with H.264 video and AAC sound through the phone's own MediaCodec
// encoders and MediaMuxer. The game hands it what it draws and plays itself (I420 frames its shader packed on the GPU, in
// a direct ByteBuffer over the readback, and the float mix of its sound), so it needs no screen-capture consent and no
// microphone permission. Every call is synchronous under one lock: a frame is copied into the encoder's input image (its
// planes' strides, planar or semi-planar), the encoded output drained into the muxer, which starts once both tracks'
// formats are known (samples before that wait in 'pending'). A frame with no free input buffer is dropped. With an HDR
// screen: HEVC Main 10 (HDR10: BT.2020, PQ) from 16-bit planar frames into the encoder's P010 image, where the phone has
// such an encoder (an HEVC encoder taking COLOR_FormatYUVP010, Android 13 and later); start says "hdr" otherwise and the
// game records SDR.

package com.joppietoppie.gof2remake;

import android.media.Image;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.media.MediaCodecList;
import android.media.MediaMuxer;
import android.os.Build;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayDeque;

public final class ClipEncoder {
    private static final Object lock = new Object();
    private static final int MaxPending = 600;

    private static final class Pending {
        final boolean video;
        final byte[] data;
        final MediaCodec.BufferInfo info;
        Pending(boolean video, byte[] data, MediaCodec.BufferInfo info) { this.video = video; this.data = data; this.info = info; }
    }

    private static MediaCodec video, audio;
    private static MediaMuxer muxer;
    private static int videoTrack = -1, audioTrack = -1, width, height, sampleRate, dropped;
    private static boolean muxing, wantAudio, failed, hdr;
    private static final int ColorFormatYuvP010 = 54;   // MediaCodecInfo.CodecCapabilities.COLOR_FormatYUVP010 (API 33)
    private static long lastVideoUs, nextAudioUs = -1;
    private static String error = "";
    private static final ArrayDeque<Pending> pending = new ArrayDeque<>();
    private static byte[] row = new byte[0], pcm = new byte[0];

    private ClipEncoder() {}

    /** An HEVC encoder taking P010 with a Main 10 profile; null when the phone has none. */
    private static MediaCodecInfo hdrEncoder() {
        if (Build.VERSION.SDK_INT < 33) return null;
        for (MediaCodecInfo info : new MediaCodecList(MediaCodecList.REGULAR_CODECS).getCodecInfos()) {
            if (!info.isEncoder()) continue;
            for (String type : info.getSupportedTypes()) {
                if (!type.equalsIgnoreCase(MediaFormat.MIMETYPE_VIDEO_HEVC)) continue;
                MediaCodecInfo.CodecCapabilities caps = info.getCapabilitiesForType(type);
                boolean p010 = false, main10 = false;
                for (int f : caps.colorFormats) if (f == ColorFormatYuvP010) p010 = true;
                for (MediaCodecInfo.CodecProfileLevel pl : caps.profileLevels)
                    if (pl.profile == MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10
                        || pl.profile == MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10) main10 = true;
                if (p010 && main10) return info;
            }
        }
        return null;
    }

    /** Starts 'path': w x h (multiples of 4) at fps, 'bitrate' bit/s; 'rate' the sound's sample rate (0: no sound);
     *  'hdr10' HEVC Main 10 (16-bit frames; the light levels aren't written). Returns "" or what went wrong ("hdr": no
     *  such encoder). */
    public static String start(String path, int w, int h, int fps, int bitrate, int rate, boolean hdr10, int peakNits, int averageNits) {
        synchronized (lock) {
            releaseAll();
            width = w; height = h; sampleRate = rate; hdr = hdr10;
            dropped = 0; failed = false; error = ""; muxing = false; videoTrack = audioTrack = -1;
            lastVideoUs = 0; nextAudioUs = -1;
            pending.clear();
            try {
                MediaCodecInfo hdrCodec = hdr ? hdrEncoder() : null;
                if (hdr && hdrCodec == null) return "hdr";
                MediaFormat vf = MediaFormat.createVideoFormat(hdr ? MediaFormat.MIMETYPE_VIDEO_HEVC : MediaFormat.MIMETYPE_VIDEO_AVC, w, h);
                vf.setInteger(MediaFormat.KEY_COLOR_FORMAT, hdr ? ColorFormatYuvP010 : MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420Flexible);
                vf.setInteger(MediaFormat.KEY_BIT_RATE, bitrate);
                vf.setInteger(MediaFormat.KEY_FRAME_RATE, fps);
                vf.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);
                if (hdr) {
                    vf.setInteger(MediaFormat.KEY_PROFILE, MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10);
                    vf.setInteger(MediaFormat.KEY_COLOR_STANDARD, MediaFormat.COLOR_STANDARD_BT2020);
                    vf.setInteger(MediaFormat.KEY_COLOR_TRANSFER, MediaFormat.COLOR_TRANSFER_ST2084);
                } else {
                    vf.setInteger(MediaFormat.KEY_COLOR_STANDARD, MediaFormat.COLOR_STANDARD_BT709);
                    vf.setInteger(MediaFormat.KEY_COLOR_TRANSFER, MediaFormat.COLOR_TRANSFER_SDR_VIDEO);
                }
                vf.setInteger(MediaFormat.KEY_COLOR_RANGE, MediaFormat.COLOR_RANGE_LIMITED);
                video = hdr ? MediaCodec.createByCodecName(hdrCodec.getName()) : MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC);
                video.configure(vf, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
                video.start();
                wantAudio = rate > 0;
                if (wantAudio) {
                    MediaFormat af = MediaFormat.createAudioFormat(MediaFormat.MIMETYPE_AUDIO_AAC, rate, 2);
                    af.setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC);
                    af.setInteger(MediaFormat.KEY_BIT_RATE, 192000);
                    af.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, 65536);
                    audio = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC);
                    audio.configure(af, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
                    audio.start();
                }
                muxer = new MediaMuxer(path, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
                return "";
            } catch (Exception e) {
                releaseAll();
                return e.toString();
            }
        }
    }

    /** A frame at timeUs: width x height planar 4:2:0 (the Y plane, then U, then V), 8-bit or in HDR 16-bit samples. */
    public static void video(ByteBuffer yuv, long timeUs) {
        synchronized (lock) {
            if (video == null || failed) return;
            try {
                int index = video.dequeueInputBuffer(0);
                if (index < 0) { dropped++; drain(video, true, false); return; }
                Image image = video.getInputImage(index);
                ByteBuffer src = yuv.duplicate();
                int ySize = width * height, cSize = ySize / 4, bytes = hdr ? 2 : 1;
                if (image != null) {
                    Image.Plane[] planes = image.getPlanes();
                    copyPlane(src, 0, width, height, planes[0], bytes);
                    copyPlane(src, ySize * bytes, width / 2, height / 2, planes[1], bytes);
                    copyPlane(src, (ySize + cSize) * bytes, width / 2, height / 2, planes[2], bytes);
                } else if (hdr) {
                    // (no image: P010 as it is laid out, Y then U and V interleaved)
                    ByteBuffer in = video.getInputBuffer(index);
                    in.clear();
                    src.clear();
                    src.limit(ySize * 2);
                    in.put(src);
                    for (int i = 0; i < cSize; i++) {
                        in.putShort(src.getShort((ySize + i) * 2));
                        in.putShort(src.getShort((ySize + cSize + i) * 2));
                    }
                } else {
                    ByteBuffer in = video.getInputBuffer(index);   // (no image: assume plain I420)
                    in.clear();
                    src.clear();
                    src.limit(ySize + 2 * cSize);
                    in.put(src);
                }
                lastVideoUs = timeUs;
                video.queueInputBuffer(index, 0, (ySize + 2 * cSize) * bytes, timeUs, 0);
                drain(video, true, false);
            } catch (Exception e) { fail(e); }
        }
    }

    /** A plane of w x h samples of 'bytes' each (1, or 2 little-endian) at 'offset' into the image's plane, by its strides. */
    private static void copyPlane(ByteBuffer src, int offset, int w, int h, Image.Plane plane, int bytes) {
        ByteBuffer dst = plane.getBuffer();
        int rowStride = plane.getRowStride(), pixelStride = plane.getPixelStride(), rowBytes = w * bytes;
        if (row.length < rowBytes) row = new byte[rowBytes];
        for (int y = 0; y < h; y++) {
            src.limit(src.capacity());
            src.position(offset + y * rowBytes);
            src.get(row, 0, rowBytes);
            int base = y * rowStride;
            if (pixelStride == bytes) {
                dst.position(base);
                dst.put(row, 0, rowBytes);
            } else {
                for (int x = 0; x < w; x++)
                    for (int b = 0; b < bytes; b++) dst.put(base + x * pixelStride + b, row[x * bytes + b]);
            }
        }
    }

    /** Sound: 'frames' frames of 'channels' interleaved floats (the first two kept, mono doubled) at timeUs. */
    public static void audio(float[] samples, int frames, int channels, long timeUs) {
        synchronized (lock) {
            if (audio == null || failed || samples == null || frames <= 0 || channels <= 0) return;
            try {
                int bytes = frames * 4;
                if (pcm.length < bytes) pcm = new byte[bytes];
                ByteBuffer out = ByteBuffer.wrap(pcm).order(ByteOrder.LITTLE_ENDIAN);
                for (int i = 0; i < frames; i++) {
                    float l = samples[i * channels], r = channels > 1 ? samples[i * channels + 1] : l;
                    out.putShort((short) (Math.max(-1f, Math.min(1f, l)) * 32767f));
                    out.putShort((short) (Math.max(-1f, Math.min(1f, r)) * 32767f));
                }
                // The encoder's clock must only go forward: a block that overlaps the last one starts where it ended.
                long t = Math.max(timeUs, nextAudioUs);
                int done = 0;
                while (done < bytes) {
                    int index = audio.dequeueInputBuffer(5000);
                    if (index < 0) break;   // the encoder is behind: the rest of this block is lost
                    ByteBuffer in = audio.getInputBuffer(index);
                    in.clear();
                    int n = Math.min(in.remaining(), bytes - done) & ~3;
                    in.put(pcm, done, n);
                    audio.queueInputBuffer(index, 0, n, t, 0);
                    done += n;
                    t += (n / 4) * 1000000L / sampleRate;
                }
                nextAudioUs = t;
                drain(audio, false, false);
            } catch (Exception e) { fail(e); }
        }
    }

    /** Ends the file: the encoders flushed, the muxer closed. Returns "" or what went wrong (the file is then unusable). */
    public static String stop() {
        synchronized (lock) {
            if (video == null) return failed ? error : "";
            try {
                endStream(video, lastVideoUs);
                drain(video, true, true);
                if (audio != null) {
                    endStream(audio, Math.max(0, nextAudioUs));
                    drain(audio, false, true);
                }
                if (!muxing && videoTrack >= 0) { wantAudio = audioTrack >= 0; startMuxer(); }
                if (!muxing) failed = true;
            } catch (Exception e) { fail(e); }
            String result = failed ? (error.isEmpty() ? "nothing was recorded" : error) : "";
            try { if (muxing) muxer.stop(); } catch (Exception e) { if (result.isEmpty()) result = e.toString(); }
            releaseAll();
            return result;
        }
    }

    public static int dropped() { synchronized (lock) { return dropped; } }

    private static void endStream(MediaCodec codec, long timeUs) {
        int index = codec.dequeueInputBuffer(100000);
        if (index >= 0) codec.queueInputBuffer(index, 0, 0, timeUs, MediaCodec.BUFFER_FLAG_END_OF_STREAM);
    }

    /** The encoded output so far into the muxer (or 'pending' until it starts); 'toEnd': until the end of the stream. */
    private static void drain(MediaCodec codec, boolean isVideo, boolean toEnd) {
        MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
        int idle = 0;
        while (true) {
            int index = codec.dequeueOutputBuffer(info, toEnd ? 10000 : 0);
            if (index == MediaCodec.INFO_TRY_AGAIN_LATER) {
                if (!toEnd || ++idle > 200) return;   // (2 s without output at the end: give up on the rest)
                continue;
            }
            if (index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                if (!muxing) {
                    int track = muxer.addTrack(codec.getOutputFormat());
                    if (isVideo) videoTrack = track; else audioTrack = track;
                    if (videoTrack >= 0 && (!wantAudio || audioTrack >= 0)) startMuxer();
                }
                continue;
            }
            if (index < 0) continue;
            ByteBuffer data = codec.getOutputBuffer(index);
            boolean config = (info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0;
            if (!config && info.size > 0 && data != null) {
                data.position(info.offset);
                data.limit(info.offset + info.size);
                if (muxing) {
                    int track = isVideo ? videoTrack : audioTrack;
                    if (track >= 0) muxer.writeSampleData(track, data, info);   // (no track: the sound was given up)
                } else if (pending.size() < MaxPending) {
                    byte[] copy = new byte[info.size];
                    data.get(copy);
                    MediaCodec.BufferInfo held = new MediaCodec.BufferInfo();
                    held.set(0, info.size, info.presentationTimeUs, info.flags);
                    pending.add(new Pending(isVideo, copy, held));
                } else if (videoTrack >= 0) {
                    wantAudio = false;   // the sound never came: the picture alone
                    startMuxer();
                }
            }
            codec.releaseOutputBuffer(index, false);
            if ((info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) return;
        }
    }

    private static void startMuxer() {
        if (muxing) return;
        muxer.start();
        muxing = true;
        for (Pending p : pending) {
            int track = p.video ? videoTrack : audioTrack;
            if (track >= 0) muxer.writeSampleData(track, ByteBuffer.wrap(p.data), p.info);
        }
        pending.clear();
    }

    private static void fail(Exception e) {
        failed = true;
        if (error.isEmpty()) error = e.toString();
    }

    private static void releaseAll() {
        try { if (video != null) { video.stop(); video.release(); } } catch (Exception ignored) {}
        try { if (audio != null) { audio.stop(); audio.release(); } } catch (Exception ignored) {}
        try { if (muxer != null) muxer.release(); } catch (Exception ignored) {}
        video = audio = null;
        muxer = null;
        muxing = false;
        pending.clear();
    }
}
