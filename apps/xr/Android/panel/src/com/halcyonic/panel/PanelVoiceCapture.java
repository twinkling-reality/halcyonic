package com.halcyonic.panel;

import android.media.AudioFormat;
import android.media.AudioRecord;
import android.media.MediaRecorder;
import android.os.SystemClock;
import java.util.Arrays;
import java.util.concurrent.ExecutorService;

/** One bounded in-memory microphone capture. No Android speech recognizer or background service. */
final class PanelVoiceCapture {
    interface Finished { void finished(byte[] wav, boolean cancelled, boolean failed); }
    private final AudioRecord recorder;
    private volatile boolean ended, send, failed;
    private boolean released;
    private final long started;
    PanelVoiceCapture() {
        int minimum = AudioRecord.getMinBufferSize(PanelVoice.RATE, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT);
        if (minimum <= 0) throw new IllegalStateException();
        AudioRecord made = new AudioRecord.Builder().setAudioSource(MediaRecorder.AudioSource.VOICE_RECOGNITION)
            .setAudioFormat(new AudioFormat.Builder().setSampleRate(PanelVoice.RATE).setChannelMask(AudioFormat.CHANNEL_IN_MONO)
                .setEncoding(AudioFormat.ENCODING_PCM_16BIT).build()).setBufferSizeInBytes(Math.max(minimum, 4096))
            .setPrivacySensitive(true).build();
        try {
            if (made.getState() != AudioRecord.STATE_INITIALIZED || made.getSampleRate() != PanelVoice.RATE) throw new IllegalStateException();
            made.startRecording();
            if (made.getRecordingState() != AudioRecord.RECORDSTATE_RECORDING) throw new IllegalStateException();
        } catch (RuntimeException error) { made.release(); throw error; }
        recorder = made; started = SystemClock.elapsedRealtime();
    }
    synchronized void stop(boolean submit) {
        // Cancellation may supersede a committed release while the worker finishes its last read.
        if (ended && submit) return;
        send = submit; ended = true;
        if (!released) try { recorder.stop(); } catch (RuntimeException ignored) {}
        if (!submit) release();
    }
    private synchronized void release() { if (!released) { recorder.release(); released = true; } }
    void read(ExecutorService worker, Finished finished) {
        try { worker.execute(() -> {
            short[] samples = new short[PanelVoice.MAX_SAMPLES]; int count = 0;
            try {
                while (!ended) {
                    if (count == samples.length || SystemClock.elapsedRealtime() - started >= PanelVoice.MAX_MS) { stop(true); break; }
                    int got = recorder.read(samples, count, Math.min(1024, samples.length-count), AudioRecord.READ_NON_BLOCKING);
                    if (got < 0) { if (!ended) { failed = true; stop(false); } break; }
                    count += got;
                    if (got == 0) Thread.sleep(5);
                }
            } catch (Exception error) { failed = true; stop(false); }
            finally { release(); }
            byte[] wav = send && !failed ? PanelVoice.wav(samples, count) : null;
            Arrays.fill(samples, (short)0);
            finished.finished(wav, !send, failed);
        }); } catch (RuntimeException error) { stop(false); throw error; }
    }
}
